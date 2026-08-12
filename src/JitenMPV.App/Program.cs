using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Threading;
using JitenMPV.App.Media;
using JitenMPV.App.Popup;
using JitenMPV.App.ViewModels;
using JitenMPV.App.Views;
using JitenMPV.Core.Config;
using JitenMPV.Core.Install;
using JitenMPV.Core.Interaction;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Net;
using JitenMPV.Core.Plugin;
using JitenMPV.Core.Update;
using Microsoft.Extensions.Logging;

namespace JitenMPV.App;

sealed class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    public static void Main(string[] args)
    {
        // The binary an update replaced can only be deleted once its process has exited, which on
        // Windows is never during the update itself.
        SelfUpdater.CleanupPreviousVersion();

        if (args.Length >= 2 && args[0] == "plugin")
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                AttachConsole(-1);
            RunPlugin(args);
        }
        else if (args.Length >= 1 && args[0] is "install" or "uninstall")
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                AttachConsole(-1);
            Environment.ExitCode = RunInstall(args);
        }
        else if (args.Length >= 1 && args[0] == JitenHttp.ProbeVerb)
        {
            Environment.ExitCode = JitenHttp.RunProbeAsync().GetAwaiter().GetResult();
        }
        else
        {
            JitenHttp.BeginProbe(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    private static int RunInstall(string[] args)
    {
        var quiet = args.Contains("--quiet");
        var options = new InstallOptions
        {
            MpvConfigDir = ValueAfter(args, "--mpv-config-dir"),
            LuaOnly = args.Contains("--lua-only"),
            DryRun = args.Contains("--dry-run")
        };

        var uninstalling = args[0] == "uninstall";
        var result = uninstalling
            ? Installer.Uninstall(options, removeProgram: args.Contains("--all"))
            : Installer.Install(options);

        if (!quiet)
        {
            if (options.DryRun) Console.WriteLine("Dry run, nothing was written.");
            foreach (var step in result.Steps) Console.WriteLine(step);
        }

        if (!result.Success)
        {
            Console.Error.WriteLine($"Failed: {result.Error}");
            return 1;
        }

        if (!quiet)
        {
            if (result.Warning is { } warning) Console.WriteLine(warning);

            Console.WriteLine(uninstalling
                ? "Uninstalled. Restart mpv for it to take effect."
                : "Installed. Restart mpv and press Ctrl+J to configure.");
        }

        return 0;
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static StreamWriter? OpenLog(string dir)
    {
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            var name = attempt == 1 ? "debug.log" : $"debug-{attempt}.log";
            try
            {
                return new StreamWriter(Path.Combine(dir, name), append: false) { AutoFlush = true };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static void RunPlugin(string[] args)
    {
        Directory.CreateDirectory(SettingsManager.ConfigDir);
        using var logWriter = OpenLog(SettingsManager.ConfigDir);

        if (logWriter is not null)
        {
            Console.SetOut(logWriter);
            Console.SetError(logWriter);
        }

        var preloadSettings = SettingsManager.LoadAsync().GetAwaiter().GetResult();
        var logLevel = preloadSettings.DebugLogging ? LogLevel.Debug : LogLevel.Information;

        using var loggerFactory = LoggerFactory.Create(b =>
            b.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
                o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled;
            })
            .SetMinimumLevel(logLevel));
        var logger = loggerFactory.CreateLogger<PluginHost>();

        JitenHttp.BeginProbe(logger);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var pipePath = args[1];
        var mpvBackend = DetectMpvBackendAsync(pipePath, logger)
            .GetAwaiter().GetResult();

        var appBuilder = BuildAvaloniaApp(mpvBackend);
        appBuilder.Start((app, startArgs) =>
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            var presenter = new AvaloniaPopupPresenter();
            var reviewPresenter = new MiningReviewPresenter();
            var overwritePresenter = new MediaOverwritePresenter();
            var host = new PluginHost(
                pipePath,
                logger,
                presenter,
                reviewPresenter,
                overwritePresenter,
                args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2])
                    ? args[2]
                    : null);
            SettingsWindow? settingsWindow = null;
            bool settingsOpening = false;

            host.OpenSettingsRequested += () =>
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    if (settingsWindow is not null)
                    {
                        settingsWindow.Activate();
                        return;
                    }

                    if (settingsOpening) return;
                    settingsOpening = true;

                    try
                    {
                        host.PausePlayback();

                        var settings = await SettingsManager.LoadAsync(cts.Token);
                        var vm = new SettingsViewModel(settings);
                        settingsWindow = new SettingsWindow { DataContext = vm };

                        settingsWindow.SettingsSaved += s => host.ReloadSettings(s);

                        settingsWindow.Closed += (_, _) =>
                        {
                            settingsWindow = null;
                            host.ResumePlayback();
                        };

                        settingsWindow.Show();
                        settingsWindow.Topmost = true;
                        settingsWindow.Activate();
                        settingsWindow.Topmost = false;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to open settings window");
                        host.ResumePlayback();
                    }
                    finally
                    {
                        settingsOpening = false;
                    }
                });
            };

            Task.Run(async () =>
            {
                try
                {
                    await host.RunAsync(preloadSettings, cts.Token);
                    logger.LogWarning("RunAsync completed normally (unexpected)");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "RunAsync crashed");
                }
                finally
                {
                    lifetime.Shutdown();
                }
            });

            lifetime.Start(startArgs);
        }, args);
    }

    public static AppBuilder BuildAvaloniaApp(
        MpvWindowBackend mpvBackend = MpvWindowBackend.Unknown)
    {
        var builder = AppBuilder.Configure<App>();
        var requestedBackend = Environment.GetEnvironmentVariable("JITEN_MPV_WINDOWING");
        var waylandAvailable = OperatingSystem.IsLinux()
                               && !string.IsNullOrEmpty(
                                   Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        var forceX11 = string.Equals(
            requestedBackend, "x11", StringComparison.OrdinalIgnoreCase);
        var forceWayland = string.Equals(
            requestedBackend, "wayland", StringComparison.OrdinalIgnoreCase);

        // Follow mpv's actual video-output backend by default. A configured x11vk/x11egl mpv
        // exposes window-id and gets the complete X11 integration; a native-Wayland mpv does not,
        // so Jiten uses Wayland too on Plasma, GNOME, wlroots compositors and everything else.
        // The environment override remains useful for troubleshooting and takes precedence.
        var useWayland = waylandAvailable
                         && !forceX11
                         && (forceWayland || mpvBackend != MpvWindowBackend.X11);

        if (useWayland)
        {
            // KWin otherwise negotiates server-side decorations before the individual popup's
            // WindowDecorations=None reaches the backend. Suppressing that negotiation lets
            // Avalonia draw normal window chrome while keeping the dictionary popup frameless.
#pragma warning disable AVALONIA_WAYLAND_FORCE_CSD
            builder.With(new WaylandPlatformOptions { ForceDrawnDecorations = true })
#pragma warning restore AVALONIA_WAYLAND_FORCE_CSD
                .UseWayland()
                .UseSkia()
                .UseHarfBuzz();
        }
        else
            builder.UsePlatformDetect();

        return builder.WithInterFont()
                      .With(new FontManagerOptions { FontFallbacks = JapaneseFallbacks() })
                      .LogToTrace();
    }

    /// Determines the video window backend before Avalonia selects one. current-gpu-context is
    /// mpv's authoritative backend; window-id remains only the X11 window handle.
    private static async Task<MpvWindowBackend> DetectMpvBackendAsync(
        string pipePath,
        ILogger logger)
    {
        if (!OperatingSystem.IsLinux()
            || string.Equals(Environment.GetEnvironmentVariable("JITEN_MPV_WINDOWING"),
                "x11", StringComparison.OrdinalIgnoreCase))
            return MpvWindowBackend.Unknown;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var ipc = new MpvIpcClient(pipePath, logger);

        try
        {
            await ipc.ConnectAsync(cts.Token);
            var readLoop = ipc.RunAsync(cts.Token);
            try
            {
                const int attempts = 8;
                for (var attempt = 0; attempt < attempts; attempt++)
                {
                    var gpuContext = await ipc.GetPropertyAsync<string?>(
                        "current-gpu-context", cts.Token);
                    var backend = MpvWindowBackendDetector.FromGpuContext(gpuContext);
                    if (backend != MpvWindowBackend.Unknown)
                        return backend;
                    if (attempt < attempts - 1)
                        await Task.Delay(100, cts.Token);
                }

                return MpvWindowBackend.Unknown;
            }
            finally
            {
                await cts.CancelAsync();
                try
                {
                    await readLoop;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            logger.LogDebug(ex,
                "Could not probe mpv's window backend; using the desktop session default");
            return MpvWindowBackend.Unknown;
        }
    }

    /// Avalonia reads a comma-separated FontFamily as one family name rather than as a CSS-style
    /// fallback list, so Japanese coverage has to be registered with the font manager instead. The
    /// UI font has none of its own; without this the dictionary popup renders tofu wherever the
    /// platform does not silently substitute, which is every Linux install lacking a CJK font.
    /// Families that are not installed are skipped, so one list covers all three platforms.
    private static FontFallback[] JapaneseFallbacks()
    {
        string[] families =
        [
            "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic",
            "Hiragino Sans", "Hiragino Kaku Gothic ProN",
            "Noto Sans CJK JP", "Noto Sans JP", "Source Han Sans JP",
            "IPAGothic", "VL Gothic", "TakaoPGothic"
        ];

        return [.. families.Select(f => new FontFallback { FontFamily = new FontFamily(f) })];
    }
}

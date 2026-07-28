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
        else
        {
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

    private static void RunPlugin(string[] args)
    {
        Directory.CreateDirectory(SettingsManager.ConfigDir);
        var logFile = Path.Combine(SettingsManager.ConfigDir, "debug.log");
        using var logWriter = new StreamWriter(logFile, append: false) { AutoFlush = true };

        Console.SetOut(logWriter);
        Console.SetError(logWriter);

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

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var pipePath = args[1];

        var appBuilder = BuildAvaloniaApp();
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
                pipePath, logger, presenter, reviewPresenter, overwritePresenter);
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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .WithInterFont()
                     .With(new FontManagerOptions { FontFallbacks = JapaneseFallbacks() })
                     .LogToTrace();

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

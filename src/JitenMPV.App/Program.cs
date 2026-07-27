using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using JitenMPV.App.Media;
using JitenMPV.App.Popup;
using JitenMPV.App.ViewModels;
using JitenMPV.App.Views;
using JitenMPV.Core.Config;
using JitenMPV.Core.Plugin;
using Microsoft.Extensions.Logging;

namespace JitenMPV.App;

sealed class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "plugin")
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                AttachConsole(-1);
            RunPlugin(args);
        }
        else
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
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
                     .LogToTrace();
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using JitenMPV.App.Popup;
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

        using var loggerFactory = LoggerFactory.Create(b =>
            b.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            })
            .SetMinimumLevel(LogLevel.Debug));
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
            var host = new PluginHost(pipePath, logger, presenter);

            Task.Run(async () =>
            {
                try
                {
                    await host.RunAsync(cts.Token);
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

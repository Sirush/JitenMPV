using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using JitenMPV.Core.Config;
using JitenMPV.Core.Plugin;
using Microsoft.Extensions.Logging;

namespace JitenMPV.App;

sealed class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    public static async Task Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "plugin")
        {
            AttachConsole(-1);

            Directory.CreateDirectory(SettingsManager.ConfigDir);
            var logFile = Path.Combine(SettingsManager.ConfigDir, "debug.log");
            await using var logWriter = new StreamWriter(logFile, append: false) { AutoFlush = true };

            Console.SetOut(logWriter);
            Console.SetError(logWriter);

            using var loggerFactory = LoggerFactory.Create(b =>
                                                               b.AddSimpleConsole(o =>
                                                                {
                                                                    o.SingleLine = true;
                                                                    o.TimestampFormat = "HH:mm:ss ";
                                                                })
                                                                .SetMinimumLevel(LogLevel.Information));
            var logger = loggerFactory.CreateLogger<PluginHost>();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var host = new PluginHost(args[1], logger);
            await host.RunAsync(cts.Token);
        }
        else
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .WithInterFont()
                     .LogToTrace();
}
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using JitenMPV.Core.Plugin;
using Microsoft.Extensions.Logging;

namespace JitenMPV.App;

sealed class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        Console.WriteLine($"[JitenMPV] args({args.Length}): [{string.Join(", ", args)}]");

        if (args.Length >= 2 && args[0] == "plugin")
        {
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
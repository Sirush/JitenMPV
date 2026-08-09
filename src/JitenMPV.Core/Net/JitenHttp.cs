using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using JitenMPV.Core.Config;
using JitenMPV.Core.Install;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Net;

/// Every HttpClient in the app is built here so the macOS curl fallback covers all of them.
public static class JitenHttp
{
    public const string ProbeVerb = "tls-probe";

    private const string ProbeHost = "jiten.moe";

    private static readonly bool OnMacWithCurl =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && File.Exists(CurlHttpHandler.CurlPath);

    private static volatile bool _inProcessTlsVerified;

    public static bool UseCurl => OnMacWithCurl && !_inProcessTlsVerified;

    public static HttpMessageHandler CreateHandler()
        => OnMacWithCurl ? new AdaptiveTlsHandler() : NewSocketsHandler();

    public static HttpClient CreateClient() => new(CreateHandler());

    internal static HttpMessageHandler NewSocketsHandler()
        => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

    /// The tls-probe verb: one in-process TLS handshake, exit 0 on survival. Run as a child
    /// process so that when the Apple TLS interop dies with an uncatchable native fault, it
    /// takes this throwaway process instead of the plugin.
    public static async Task<int> RunProbeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(ProbeHost, 443, cts.Token);
            await using var ssl = new SslStream(tcp.GetStream());
            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = ProbeHost }, cts.Token);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    /// Requests stay on curl until the probe child proves in-process TLS survives here; only a
    /// clean exit upgrades, so a network failure or a killed probe leaves the safe transport on.
    public static void BeginProbe(ILogger logger)
    {
        if (!OnMacWithCurl || _inProcessTlsVerified) return;

        switch (ReadCachedVerdict())
        {
            case true:
                _inProcessTlsVerified = true;
                logger.LogInformation("In-process TLS verified earlier on this system; skipping the curl fallback");
                return;
            case false:
                logger.LogInformation("In-process TLS crashed earlier on this system; HTTP stays on the system curl");
                return;
        }

        _ = TaskHelper.RunSafe(async () =>
        {
            if (Environment.ProcessPath is not { } exe) return;

            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(ProbeVerb);

            using var process = Process.Start(psi);
            if (process is null) return;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                logger.LogInformation("TLS probe timed out; HTTP stays on the system curl");
                return;
            }

            if (process.ExitCode == 0)
            {
                _inProcessTlsVerified = true;
                WriteVerdict(tlsSafe: true);
                logger.LogInformation("In-process TLS verified; HTTP leaves the curl fallback");
            }
            else if (process.ExitCode >= 128)
            {
                // Only a signal death proves the TLS stack is broken; it is the sole verdict worth
                // remembering as bad.
                WriteVerdict(tlsSafe: false);
                logger.LogInformation(
                    "TLS probe crashed ({Code}); HTTP stays on the system curl", process.ExitCode);
            }
            else
            {
                // Exit 1 is a network or certificate failure: inconclusive, so the next session
                // probes again rather than pinning a healthy machine to curl.
                logger.LogInformation(
                    "TLS probe inconclusive ({Code}); HTTP stays on the system curl for this session",
                    process.ExitCode);
            }
        }, logger, "TLS probe");
    }

    private static string VerdictPath => Path.Combine(AppPaths.ConfigDir, "tls-probe-verdict");

    /// App and OS version are the only two things whose change can alter whether in-process TLS
    /// survives: an update swaps the bundled runtime, an OS update swaps Apple's side.
    private static string VerdictKey => $"{Installer.CurrentVersion}|{Environment.OSVersion.Version}";

    private static bool? ReadCachedVerdict()
    {
        try
        {
            var parts = File.ReadAllText(VerdictPath).Trim().Split('|');
            if (parts.Length == 3 && $"{parts[0]}|{parts[1]}" == VerdictKey)
                return parts[2] == "ok";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static void WriteVerdict(bool tlsSafe)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ConfigDir);
            File.WriteAllText(VerdictPath, $"{VerdictKey}|{(tlsSafe ? "ok" : "bad")}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

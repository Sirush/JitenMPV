using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JitenMPV.Core.Install;

/// Steps a deployed binary needs before mpv can spawn it, shared by install and self-update.
public static class ExecutableFile
{
    public static void SetExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// Gatekeeper on macOS 26 kills binaries whose ad-hoc signature was made on another machine,
    /// so the release's CI signature must be replaced with one made here.
    /// <returns>Null when signed, otherwise a warning for the user; the CI signature then stays in
    /// place, which Macs that accept foreign ad-hoc signatures still run.</returns>
    public static string? ResignAdHoc(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/codesign")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                ArgumentList = { "--force", "--sign", "-", path }
            });
            if (process is null) return "Warning: codesign could not be started; macOS may refuse to run JitenMPV.";

            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds))
                return "Warning: codesign did not finish; macOS may refuse to run JitenMPV.";

            return process.ExitCode == 0
                ? null
                : $"Warning: re-signing failed (codesign exit {process.ExitCode}: {stderr.Trim()}); macOS may refuse to run JitenMPV.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return $"Warning: re-signing failed ({ex.Message}); macOS may refuse to run JitenMPV.";
        }
    }
}

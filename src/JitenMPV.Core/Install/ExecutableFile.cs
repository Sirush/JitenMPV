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
    public static void ResignAdHoc(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/codesign")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "--force", "--sign", "-", path }
            });
            process?.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // The CI signature stays in place; Macs that accept foreign ad-hoc signatures still run it.
        }
    }
}

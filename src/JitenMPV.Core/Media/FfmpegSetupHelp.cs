namespace JitenMPV.Core.Media;

/// The command a user runs to get ffmpeg themselves, for the platforms where JitenMPV does not
/// offer a download button.
public static class FfmpegSetupHelp
{
    public static string ManualCommand => OperatingSystem.IsMacOS()
        ? "brew install ffmpeg"
        : OperatingSystem.IsLinux()
            ? LinuxCommand()
            : "winget install Gyan.FFmpeg";

    public static string Hint => OperatingSystem.IsWindows()
        ? "Or install it system-wide yourself:"
        : "Install it with your package manager:";

    /// Debian and Fedora ship mpv against the libav* shared libraries without the ffmpeg binary, so
    /// a working mpv there says nothing about ffmpeg being present. Arch's mpv depends on the
    /// ffmpeg package itself, so a missing ffmpeg there is unusual enough that a canned pacman line
    /// would be misleading; it returns empty and the caller shows nothing.
    private static string LinuxCommand()
    {
        var (id, idLike) = ReadOsRelease();

        bool Matches(params string[] names)
            => names.Any(n => id == n || idLike.Contains(n, StringComparison.Ordinal));

        if (Matches("arch")) return "";
        if (Matches("debian", "ubuntu")) return "sudo apt install ffmpeg";
        if (Matches("fedora", "rhel")) return "sudo dnf install ffmpeg-free";
        if (Matches("suse", "opensuse")) return "sudo zypper install ffmpeg";

        return "";
    }

    private static (string Id, string IdLike) ReadOsRelease()
    {
        try
        {
            string id = "", idLike = "";
            foreach (var line in File.ReadLines("/etc/os-release"))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;

                var key = line[..separator];
                var value = line[(separator + 1)..].Trim('"', '\'');

                if (key == "ID") id = value;
                else if (key == "ID_LIKE") idLike = value;
            }

            return (id, idLike);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ("", "");
        }
    }
}

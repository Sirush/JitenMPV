using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using JitenMPV.Core.Media;

namespace JitenMPV.App.Media;

/// Plays the preview clip through the platform's stock CLI player (afplay on macOS,
/// pw-play/paplay/aplay on Linux); .NET has no built-in audio output off Windows and a playback
/// package is not worth the weight for a two-second clip.
public sealed class CliAudioPreview : IAudioPreview
{
    private readonly string? _playerExe = LocatePlayer();
    private Process? _process;
    private string? _clipPath;

    public bool IsSupported => _playerExe is not null;

    public void Play(WaveformData wave, double start, double end)
    {
        if (_playerExe is null || wave.IsEmpty) return;

        var wav = WavClipEncoder.Encode(wave, start, end);
        if (wav.Length == 0) return;

        Stop();

        var path = Path.Combine(Path.GetTempPath(), "jiten-mpv",
            "preview-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, wav);
            _process = Process.Start(new ProcessStartInfo(_playerExe)
            {
                ArgumentList = { path },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            // Deleted in Stop rather than here, so the player is not raced before it opens the file.
            _clipPath = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            TryDelete(path);
        }
    }

    public void Stop()
    {
        if (_process is { } process)
        {
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
            }

            process.Dispose();
            _process = null;
        }

        if (_clipPath is { } path)
        {
            TryDelete(path);
            _clipPath = null;
        }
    }

    public void Dispose() => Stop();

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// Absolute well-known paths are probed in addition to PATH because mpv launched from Finder
    /// or a desktop shortcut inherits no login-shell PATH (same reason as FfmpegLocator).
    private static string? LocatePlayer()
    {
        if (OperatingSystem.IsMacOS())
            return File.Exists("/usr/bin/afplay") ? "/usr/bin/afplay" : null;

        if (!OperatingSystem.IsLinux()) return null;

        string[] names = ["pw-play", "paplay", "aplay"];
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(':', StringSplitOptions.RemoveEmptyEntries);
        string[] wellKnownDirs = ["/usr/bin", "/usr/local/bin", "/bin"];

        foreach (var name in names)
        {
            foreach (var dir in pathDirs)
                if (File.Exists(Path.Combine(dir, name)))
                    return Path.Combine(dir, name);

            foreach (var dir in wellKnownDirs)
                if (File.Exists(Path.Combine(dir, name)))
                    return Path.Combine(dir, name);
        }

        return null;
    }
}

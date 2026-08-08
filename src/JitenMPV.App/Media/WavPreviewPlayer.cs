using System;
using System.Runtime.InteropServices;
using JitenMPV.Core.Media;

namespace JitenMPV.App.Media;

public interface IAudioPreview : IDisposable
{
    bool IsSupported { get; }
    void Play(WaveformData wave, double start, double end);
    void Stop();
}

/// Plays the already-decoded PCM through winmm, so preview costs neither a second decode nor an
/// extra package reference. Non-Windows hosts use CliAudioPreview instead.
public sealed class WavPreviewPlayer : IAudioPreview
{
    private const int SndAsync = 0x0001;
    private const int SndMemory = 0x0004;
    private const int SndNoDefault = 0x0002;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(byte[]? data, IntPtr module, int flags);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySoundNull(IntPtr data, IntPtr module, int flags);

    private GCHandle _pinned;

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Play(WaveformData wave, double start, double end)
    {
        if (!IsSupported || wave.IsEmpty) return;

        var wav = WavClipEncoder.Encode(wave, start, end);
        if (wav.Length == 0) return;

        Stop();

        // SND_ASYNC returns immediately and keeps reading the buffer, so it must stay pinned until
        // playback is replaced or stopped.
        _pinned = GCHandle.Alloc(wav, GCHandleType.Pinned);
        PlaySound(wav, IntPtr.Zero, SndAsync | SndMemory | SndNoDefault);
    }

    public void Stop()
    {
        if (!IsSupported) return;

        PlaySoundNull(IntPtr.Zero, IntPtr.Zero, SndAsync | SndNoDefault);
        if (_pinned.IsAllocated) _pinned.Free();
    }

    public void Dispose() => Stop();
}

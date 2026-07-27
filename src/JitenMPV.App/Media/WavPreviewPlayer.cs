using System;
using System.IO;
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
/// extra package reference. Non-Windows hosts get the no-op below and the button stays hidden.
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

        var wav = BuildWav(wave, start, end);
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

    private static byte[] BuildWav(WaveformData wave, double start, double end)
    {
        var from = Math.Clamp((int)((start - wave.WindowStart) * wave.SampleRate), 0, wave.Pcm.Length);
        var to = Math.Clamp((int)((end - wave.WindowStart) * wave.SampleRate), from, wave.Pcm.Length);
        var samples = to - from;
        if (samples <= 0) return [];

        var dataBytes = samples * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(wave.SampleRate);
        writer.Write(wave.SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);

        var pcmBytes = new byte[dataBytes];
        Buffer.BlockCopy(wave.Pcm, from * 2, pcmBytes, 0, dataBytes);
        writer.Write(pcmBytes);
        writer.Flush();

        return stream.ToArray();
    }
}

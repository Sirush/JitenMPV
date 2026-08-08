using System;
using System.IO;
using JitenMPV.Core.Media;

namespace JitenMPV.App.Media;

public static class WavClipEncoder
{
    /// Encodes the selected slice of the already-decoded 16-bit mono preview PCM as a WAV file.
    public static byte[] Encode(WaveformData wave, double start, double end)
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

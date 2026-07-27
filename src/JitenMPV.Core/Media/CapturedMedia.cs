namespace JitenMPV.Core.Media;

public sealed record CapturedImage(
    byte[] Bytes,
    string ContentType,
    string FileName,
    int Frames,
    double Duration)
{
    public bool IsAnimated => Frames > 1;
}

public sealed record CapturedAudio(
    byte[] Bytes,
    string ContentType,
    string FileName,
    double Start,
    double End)
{
    public double Duration => End - Start;
}

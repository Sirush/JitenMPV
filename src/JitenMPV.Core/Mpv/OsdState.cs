namespace JitenMPV.Core.Mpv;

public sealed class OsdState
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Version { get; private set; }

    public bool Update(int width, int height)
    {
        if (width == Width && height == Height) return false;
        Width = width;
        Height = height;
        Version++;
        return true;
    }
}

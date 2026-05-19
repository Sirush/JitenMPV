namespace JitenMPV.Core.Mpv;

public sealed record OverlayBounds(double X0, double Y0, double X1, double Y1)
{
    public double Width => X1 - X0;
    public double Height => Y1 - Y0;
}

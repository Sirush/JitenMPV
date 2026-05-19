namespace JitenMPV.Core.Mpv;

public enum MouseEventType
{
    Move,
    LeftPress,
    LeftRelease,
    DoubleClick,
    Leave
}

public sealed record MouseEventArgs(MouseEventType Type, double X, double Y);

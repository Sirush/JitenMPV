namespace JitenMPV.Core.Rendering;

public sealed record WordRect(
    int TokenIndex, int WordId, byte ReadingIndex,
    float X, float Y, float Width, float Height);

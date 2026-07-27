namespace JitenMPV.Core.Media;

public static class MediaLimits
{
    /// CardMediaController.MaxFileBytes. The server refuses anything larger outright, so an encode
    /// above this is never uploaded.
    public const int UploadHardLimitBytes = 5 * 1024 * 1024;
}

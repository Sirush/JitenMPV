namespace JitenMPV.Core.Subtitles;

public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Media;
using JitenMPV.Core.Subtitles;

namespace JitenMPV.Core.Interaction;

public sealed record MiningDeckOption(int DeckId, string Name);

public sealed record MiningReviewData(
    string Spelling,
    string Reading,
    int WordId,
    byte ReadingIndex,
    CapturedImage? Poster,
    WaveformData Waveform,
    double AudioStart,
    double AudioEnd,
    double SubtitleStart,
    double SubtitleEnd,
    IReadOnlyList<SubtitleCue> Context,
    int CurrentCueIndex,
    string? SurfaceForm,
    CardMediaEntry? Existing,
    bool ImageRequested,
    bool AudioRequested,
    bool AnimatedRequested,
    bool AudioAvailable,
    bool TimelineLoaded,
    int AudioBitrateKbps,
    AnimationPlan? ClipPlan,
    /// Measures the clip's real size by encoding a sample. Run only when the user asks for a clip,
    /// so the window still opens immediately.
    Func<CancellationToken, Task<long?>>? MeasureClipSize,
    IReadOnlyList<MiningDeckOption> DeckOptions,
    int? PresetDeckId);

public sealed record MiningReviewResult(
    double AudioStart,
    double AudioEnd,
    string? Sentence,
    bool IncludeImage,
    bool IncludeAudio,
    bool Animated,
    int? DeckId);

public interface IMiningReviewPresenter
{
    /// Returns null when the user cancels; the capture is then discarded and nothing is uploaded.
    Task<MiningReviewResult?> ShowAsync(MiningReviewData data, CancellationToken ct);
}

public sealed record MediaOverwriteData(
    string Spelling, bool ReplacesImage, bool ReplacesAudio);

public enum MediaOverwriteChoice { Replace, SkipMedia, CancelMine }

public sealed record MediaOverwriteAnswer(MediaOverwriteChoice Choice, bool DontAskAgain);

/// The confirmation shown when the review popup is off but media would replace an existing file.
public interface IMediaOverwritePresenter
{
    Task<MediaOverwriteAnswer> ConfirmAsync(MediaOverwriteData data, CancellationToken ct);
}

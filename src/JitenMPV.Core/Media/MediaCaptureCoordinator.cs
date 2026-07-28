using JitenMPV.Core.Api;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;
using JitenMPV.Core.Interaction;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Plus;
using JitenMPV.Core.Subtitles;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

public sealed record MediaCaptureRequest(
    int WordId, byte ReadingIndex, string? SurfaceForm, string? SubtitleText,
    string Spelling, string Reading, int? DeckId);

public enum MediaCaptureOutcome
{
    Captured, Disabled, NotEntitled, NoFfmpeg, NoMedia, Failed, Cancelled, InFlight
}

public sealed record MediaCaptureResult(
    MediaCaptureOutcome Outcome,
    CapturedImage? Image = null,
    CapturedAudio? Audio = null,
    string? Sentence = null,
    int? DeckId = null,
    bool AnimationFellBack = false,
    bool FfmpegMissing = false)
{
    public bool Cancelled => Outcome == MediaCaptureOutcome.Cancelled;
    public bool HasUploads => Image is not null || Audio is not null;

    public static readonly MediaCaptureResult Disabled = new(MediaCaptureOutcome.Disabled);
    public static readonly MediaCaptureResult NotEntitled = new(MediaCaptureOutcome.NotEntitled);
    public static readonly MediaCaptureResult NoFfmpeg = new(MediaCaptureOutcome.NoFfmpeg);
    public static readonly MediaCaptureResult NoMedia = new(MediaCaptureOutcome.NoMedia);
    public static readonly MediaCaptureResult InFlight = new(MediaCaptureOutcome.InFlight);
    public static readonly MediaCaptureResult Cancel = new(MediaCaptureOutcome.Cancelled);
}

public sealed class MediaCaptureCoordinator(
    JitenApiClient api,
    JitenPlusService plus,
    SubtitleTimeline timeline,
    PluginSettings settings,
    ILogger logger,
    IMiningReviewPresenter? reviewPresenter = null,
    IMediaOverwritePresenter? overwritePresenter = null)
{
    /// Subtitle range fallback when mpv reports no cue, so a capture is still bounded.
    private const double FallbackHalfSpanSeconds = 1.5;

    private readonly MediaUploader _uploader = new(api, plus, logger);
    private readonly Lock _inFlightLock = new();
    private readonly HashSet<(int, byte)> _inFlight = [];

    private volatile PluginSettings _settings = settings;
    private volatile bool _overwriteAcceptedThisSession;
    private volatile bool _entitlementNoticeShown;
    private volatile bool _ffmpegNoticeShown;

    /// Hides every OSD layer the window screenshot would otherwise bake into the card. Set by
    /// PluginHost, which owns the popup and the status overlay.
    public Func<CancellationToken, Task>? PrepareWindowCapture { get; set; }

    public IReadOnlyList<MiningDeckOption> DeckOptions { get; set; } = [];

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    public void Reset()
    {
        _overwriteAcceptedThisSession = false;
        lock (_inFlightLock) _inFlight.Clear();
    }

    /// True once per session, so a lapsed subscriber is told what happened without being nagged.
    public bool ShouldReportNotEntitled()
    {
        if (_entitlementNoticeShown) return false;
        _entitlementNoticeShown = true;
        return true;
    }

    public bool ShouldReportNoFfmpeg()
    {
        if (_ffmpegNoticeShown) return false;
        _ffmpegNoticeShown = true;
        return true;
    }

    public async Task<MediaCaptureResult> CaptureAndConfirmAsync(
        MediaCaptureRequest request, MpvIpcClient ipc, CancellationToken ct)
    {
        var s = _settings;
        if (!s.MediaCaptureEnabled) return MediaCaptureResult.Disabled;
        if (!plus.Current.IsActive) return MediaCaptureResult.NotEntitled;
        if (!s.MediaCaptureImage && !s.MediaCaptureAudio) return MediaCaptureResult.Disabled;

        var ffmpegPath = (await FfmpegLocator.ResolveAsync(s.FfmpegPath, ct))?.ExecutablePath;

        // Without ffmpeg the mpv screenshot still works and uploads as PNG for the server to
        // normalize; audio, animation and burn-in are the parts that genuinely cannot run.
        if (ffmpegPath is null && !s.MediaCaptureImage)
            return MediaCaptureResult.NoFfmpeg;

        var key = (request.WordId, request.ReadingIndex);
        lock (_inFlightLock)
        {
            if (!_inFlight.Add(key)) return MediaCaptureResult.InFlight;
        }

        try
        {
            return await CaptureCoreAsync(request, ipc, ffmpegPath, s, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Media capture failed for {WordId}:{ReadingIndex}",
                request.WordId, request.ReadingIndex);
            return new MediaCaptureResult(MediaCaptureOutcome.Failed);
        }
        finally
        {
            lock (_inFlightLock) _inFlight.Remove(key);
        }
    }

    private async Task<MediaCaptureResult> CaptureCoreAsync(
        MediaCaptureRequest request, MpvIpcClient ipc, string? ffmpegPath,
        PluginSettings s, CancellationToken ct)
    {
        var props = await MpvCaptureProbe.ReadAsync(ipc, logger, ct);
        if (props is null) return new MediaCaptureResult(MediaCaptureOutcome.Failed);

        var (timebase, subStart, subEnd) = ResolveRange(props, request.SubtitleText);

        using var temp = new MediaTempFiles(logger);
        var runner = ffmpegPath is null ? null : new FfmpegRunner(ffmpegPath, logger);

        // Resolved at most once and shared, so the still and the animation burn from the same source.
        // Staging a copy can cost an ffmpeg run, so it is only done ahead of the screenshot when the
        // screenshot itself burns - that route already seeks and is not frame-sensitive.
        var burnSource = runner is null || s.MediaSubtitleBurn == MediaSubtitleBurn.None
            ? null
            : new SubtitleBurnSource(runner, temp, logger);

        string? subtitleFilter = null;
        if (burnSource is not null && s.MediaSubtitleBurn == MediaSubtitleBurn.Original)
            subtitleFilter = await burnSource.ResolveAsync(timebase, ct);

        // Taken before anything else awaitable that could let playback advance: MpvFrame mode is
        // position-sensitive.
        CapturedImage? still = null;
        if (s.MediaCaptureImage)
        {
            var shooter = new ScreenshotCapture(runner, temp, s, logger);
            if (s.MediaSubtitleBurn == MediaSubtitleBurn.Colored && PrepareWindowCapture is not null)
                await PrepareWindowCapture(ct);

            still = await shooter.CaptureAsync(new ScreenshotRequest(
                timebase, s.MediaSubtitleBurn, s.MediaImageSource,
                subStart, subEnd, props.TimePos, subtitleFilter), ipc, ct);
        }

        var existingTask = FetchExistingAsync(request, ct);

        var audioWanted = s.MediaCaptureAudio && runner is not null && timebase.AudioTrackIndex is not null;
        var margin = Math.Max(0, s.MediaAudioWindowMarginSeconds);
        var wave = WaveformData.Empty;
        var audioCapture = runner is null ? null : new AudioCapture(runner, temp, s, logger);

        if (audioWanted && audioCapture is not null)
        {
            wave = await audioCapture.DecodeWindowAsync(
                timebase,
                timebase.Clamp(timebase.SubtitleToAudioTime(subStart) - margin),
                timebase.Clamp(timebase.SubtitleToAudioTime(subEnd) + margin),
                ct);
        }

        var audioSubStart = timebase.SubtitleToAudioTime(subStart);
        var audioSubEnd = timebase.SubtitleToAudioTime(subEnd);
        var (selStart, selEnd) = WaveformSampler.AutoTrim(wave, audioSubStart, audioSubEnd, s);

        var existing = await existingTask;

        var context = timeline.Around(TimeSpan.FromSeconds(subStart), Math.Max(0, s.MediaSentenceContextLines));
        var currentIndex = timeline.IndexInWindow(TimeSpan.FromSeconds(subStart), Math.Max(0, s.MediaSentenceContextLines));

        var includeImage = s.MediaCaptureImage && still is not null;
        var includeAudio = audioWanted && !wave.IsEmpty;
        var animated = s.MediaCaptureImageAnimated;
        string? sentence = null;
        int? deckId = request.DeckId;

        if (s.MediaReviewPopup && reviewPresenter is not null)
        {
            var clipStart = timebase.Clamp(timebase.SubtitleToVideoTime(subStart));
            var clipEnd = timebase.Clamp(timebase.SubtitleToVideoTime(subEnd));
            var clipPossible = runner is not null && timebase.IsSeekableFile;

            var clipPlan = clipPossible ? AnimationBudget.Solve(clipEnd - clipStart, s) : null;
            Func<CancellationToken, Task<long?>>? measureClip = clipPossible
                ? async token =>
                {
                    if (burnSource is not null)
                        subtitleFilter = await burnSource.ResolveAsync(timebase, token);
                    return await new AnimationCapture(runner!, temp, s, logger).EstimateBytesAsync(
                        timebase, s.MediaSubtitleBurn, subtitleFilter, clipStart, clipEnd, token);
                }
                : null;

            var answer = await reviewPresenter.ShowAsync(new MiningReviewData(
                request.Spelling, request.Reading, request.WordId, request.ReadingIndex,
                still, wave, selStart, selEnd, audioSubStart, audioSubEnd,
                context, currentIndex, request.SurfaceForm, existing,
                includeImage, includeAudio, animated, includeAudio, timeline.IsLoaded,
                s.MediaAudioBitrateKbps, clipPlan, measureClip, DeckOptions, request.DeckId), ct);

            if (answer is null) return MediaCaptureResult.Cancel;

            includeImage &= answer.IncludeImage;
            includeAudio &= answer.IncludeAudio;
            animated = answer.Animated;
            selStart = answer.AudioStart;
            selEnd = answer.AudioEnd;
            sentence = answer.Sentence;
            deckId = answer.DeckId ?? deckId;
        }
        else if (NeedsOverwriteConfirmation(existing, includeImage, includeAudio, s))
        {
            var answer = overwritePresenter is not null
                ? await overwritePresenter.ConfirmAsync(new MediaOverwriteData(
                    request.Spelling,
                    includeImage && Replaces(existing?.Image),
                    includeAudio && Replaces(existing?.Audio)), ct)
                : new MediaOverwriteAnswer(MediaOverwriteChoice.Replace, false);

            switch (answer.Choice)
            {
                case MediaOverwriteChoice.CancelMine: return MediaCaptureResult.Cancel;
                case MediaOverwriteChoice.SkipMedia: return MediaCaptureResult.NoMedia;
            }

            if (answer.DontAskAgain) _overwriteAcceptedThisSession = true;
        }

        var animationFellBack = false;
        CapturedImage? image = includeImage ? still : null;

        if (includeImage && animated && runner is not null && timebase.IsSeekableFile)
        {
            var animator = new AnimationCapture(runner, temp, s, logger);
            if (burnSource is not null)
                subtitleFilter = await burnSource.ResolveAsync(timebase, ct);

            var clip = await animator.CaptureAsync(
                timebase, s.MediaSubtitleBurn, subtitleFilter,
                timebase.Clamp(timebase.SubtitleToVideoTime(subStart)),
                timebase.Clamp(timebase.SubtitleToVideoTime(subEnd)), ct);

            if (clip is not null) image = clip;
            else animationFellBack = still is not null;
        }

        CapturedAudio? audio = null;
        if (includeAudio && audioCapture is not null)
            audio = await audioCapture.CaptureAsync(timebase, selStart, selEnd, audioSubStart, audioSubEnd, ct);

        var ffmpegMissing = runner is null
                            && (s.MediaCaptureAudio || s.MediaCaptureImageAnimated
                                || s.MediaSubtitleBurn == MediaSubtitleBurn.Original);

        if (image is null && audio is null)
            return MediaCaptureResult.NoMedia with { FfmpegMissing = ffmpegMissing };

        return new MediaCaptureResult(
            MediaCaptureOutcome.Captured, image, audio, sentence, deckId,
            animationFellBack, ffmpegMissing);
    }

    public Task<MediaUploadOutcome> UploadAsync(
        int wordId, byte readingIndex, MediaCaptureResult capture, CancellationToken ct)
        => _uploader.UploadAsync(wordId, readingIndex, capture.Image, capture.Audio, ct);

    private bool NeedsOverwriteConfirmation(
        CardMediaEntry? existing, bool includeImage, bool includeAudio, PluginSettings s)
    {
        if (s.MediaOverwritePrompt == MediaOverwritePrompt.Never) return false;
        if (s.MediaOverwritePrompt == MediaOverwritePrompt.OncePerSession && _overwriteAcceptedThisSession)
            return false;

        return (includeImage && Replaces(existing?.Image)) || (includeAudio && Replaces(existing?.Audio));
    }

    /// An inherited file belongs to a sibling reading; uploading creates this form's own copy
    /// instead of replacing anything.
    private static bool Replaces(CardMediaFile? file) => file is { Inherited: false };

    private async Task<CardMediaEntry?> FetchExistingAsync(MediaCaptureRequest request, CancellationToken ct)
    {
        try
        {
            return await api.GetCardMediaAsync(request.WordId, request.ReadingIndex, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Card media existence check failed");
            return null;
        }
    }

    private (MediaTimebase, double, double) ResolveRange(MpvCaptureProps props, string? subtitleText)
    {
        // The subtitle file's own timestamps are immune to sub-delay, so they are preferred over
        // mpv's reported range whenever pre-parsing has loaded them.
        if (subtitleText is not null && timeline.IsLoaded
            && timeline.At(TimeSpan.FromSeconds(props.TimePos)) is { } cue
            && string.Equals(Normalize(cue.Text), Normalize(subtitleText), StringComparison.Ordinal))
        {
            var fileTimebase = props.ToTimebase() with { RangeIsFileTime = true };
            return (fileTimebase, cue.Start.TotalSeconds, cue.End.TotalSeconds);
        }

        var timebase = props.ToTimebase();
        var start = props.SubStart ?? props.TimePos - FallbackHalfSpanSeconds;
        var end = props.SubEnd ?? props.TimePos + FallbackHalfSpanSeconds;
        return (timebase, start, end < start ? start + FallbackHalfSpanSeconds : end);
    }

    private static string Normalize(string text)
        => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
}

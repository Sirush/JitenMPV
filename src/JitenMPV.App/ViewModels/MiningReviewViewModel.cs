using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JitenMPV.App.Media;
using JitenMPV.Core.Interaction;
using JitenMPV.Core.Media;
using JitenMPV.Core.Plugin;
using JitenMPV.Core.Text;

namespace JitenMPV.App.ViewModels;

public partial class MiningReviewViewModel : ViewModelBase
{
    /// The server's own cap on a custom sentence (StudyController), mirrored so the counter warns
    /// before SentenceFormatter silently trims.
    private const int MaxSentenceLength = 150;

    private const string CueSeparator = "   ";

    private readonly MiningReviewData _data;
    private readonly IAudioPreview _preview;
    private readonly List<(int Start, int Length)> _cueRanges = [];
    private bool _measureStarted;

    public event Action<MiningReviewResult?>? Completed;

    public MiningReviewViewModel(MiningReviewData data, IAudioPreview preview)
    {
        _data = data;
        _preview = preview;

        Headword = data.Spelling;
        SurfaceForm = data.SurfaceForm;

        var segments = FuriganaParser.ForSpelling(data.Spelling, data.Reading);
        ShowFurigana = segments is not null;
        FuriganaSegments = segments is null
            ? []
            : [..segments.Select(FuriganaItem.From)];
        Reading = FuriganaParser.ToKana(data.Reading);
        ShowReading = !ShowFurigana && Reading.Length > 0 && Reading != data.Spelling;

        Peaks = data.Waveform.Peaks;
        WindowStart = data.Waveform.WindowStart;
        WindowDuration = data.Waveform.WindowDuration;
        SubtitleStart = data.SubtitleStart;
        SubtitleEnd = data.SubtitleEnd;
        SelectionStart = data.AudioStart;
        SelectionEnd = data.AudioEnd;

        IncludeImage = data.ImageRequested && data.Poster is not null;
        IncludeAudio = data.AudioRequested && !data.Waveform.IsEmpty;
        Animated = data.AnimatedRequested;

        HasPoster = data.Poster is not null;
        HasAudio = data.AudioAvailable && !data.Waveform.IsEmpty;
        PreviewSupported = preview.IsSupported && HasAudio;
        TimelineLoaded = data.TimelineLoaded;

        if (data.Poster is { } poster)
        {
            PosterCaption = $"Screenshot - {poster.Bytes.Length / 1024} KB";
            Poster = TryDecode(poster.Bytes);
        }
        else
        {
            PosterCaption = "No screenshot was taken";
        }

        ReplacesImage = IncludeImage && data.Existing?.Image is { Inherited: false };
        ReplacesAudio = IncludeAudio && data.Existing?.Audio is { Inherited: false };
        HasOverwriteWarning = ReplacesImage || ReplacesAudio;
        OverwriteMessage = BuildOverwriteMessage();

        foreach (var deck in data.DeckOptions)
            Decks.Add(new StudyDeckOption(deck.DeckId, deck.Name));
        SelectedDeck = Decks.FirstOrDefault(d => d.DeckId == data.PresetDeckId) ?? Decks.FirstOrDefault();

        BuildContextText(data);
        UpdateSentence();

        if (Animated) _ = MeasureClipAsync();
    }

    public string Headword { get; }
    public IReadOnlyList<FuriganaItem> FuriganaSegments { get; }
    public bool ShowFurigana { get; }
    public string Reading { get; }
    public bool ShowReading { get; }
    public string? SurfaceForm { get; }
    public float[] Peaks { get; }
    public double WindowStart { get; }
    public double WindowDuration { get; }
    public double SubtitleStart { get; }
    public double SubtitleEnd { get; }
    public bool HasPoster { get; }
    public bool HasAudio { get; }
    public bool PreviewSupported { get; }
    public bool TimelineLoaded { get; }
    public string PosterCaption { get; } = "";
    public Bitmap? Poster { get; }
    public bool ReplacesImage { get; }
    public bool ReplacesAudio { get; }
    public bool HasOverwriteWarning { get; }
    public string OverwriteMessage { get; }

    public ObservableCollection<StudyDeckOption> Decks { get; } = [];

    [ObservableProperty] private StudyDeckOption? _selectedDeck;
    [ObservableProperty] private bool _includeImage;
    [ObservableProperty] private bool _includeAudio;
    [ObservableProperty] private bool _animated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipSummary))]
    private long? _clipSizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipSummary))]
    private bool _isMeasuringClip;
    [ObservableProperty] private string _contextText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioSummary))]
    private double _selectionStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioSummary))]
    private double _selectionEnd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SentenceCounter))]
    [NotifyPropertyChangedFor(nameof(SentenceOverLimit))]
    private string _sentencePreview = "";

    [ObservableProperty] private int _sentenceSelectionStart;
    [ObservableProperty] private int _sentenceSelectionEnd;

    public string AudioSummary
    {
        get
        {
            var duration = Math.Max(0, SelectionEnd - SelectionStart);
            // Opus is VBR, so this only has to be close enough to warn before the size cap bites.
            var kb = (int)(duration * _data.AudioBitrateKbps / 8);
            return $"{duration:0.00} s - about {kb} KB";
        }
    }

    public bool HasClipPlan => _data.ClipPlan is not null;

    /// Length and smoothness are known up front; the size arrives once a sample has been measured.
    public string ClipSummary => _data.ClipPlan is { } plan
        ? $"{plan.Duration:0.0} s, {plan.Fps} pictures per second{ClipSizeSuffix}"
        : "";

    private string ClipSizeSuffix => ClipSizeBytes switch
    {
        null when IsMeasuringClip => ", working out the size...",
        null => "",
        var bytes => $", about {Size(bytes.Value)}"
    };

    private static string Size(long bytes) => bytes >= 1_000_000
        ? $"{bytes / 1_000_000.0:0.#} MB"
        : $"{bytes / 1000} KB";

    public string SentenceCounter => $"{SentencePreview.Length} / {MaxSentenceLength}";
    public bool SentenceOverLimit => SentencePreview.Length >= MaxSentenceLength;

    partial void OnSentenceSelectionStartChanged(int value) => UpdateSentence();
    partial void OnSentenceSelectionEndChanged(int value) => UpdateSentence();

    partial void OnAnimatedChanged(bool value)
    {
        if (value) _ = MeasureClipAsync();
    }

    /// Measured once per window: the clip's settings and time range cannot change while it is open.
    private async Task MeasureClipAsync()
    {
        if (_measureStarted || _data.MeasureClipSize is not { } measure) return;
        _measureStarted = true;

        IsMeasuringClip = true;
        try
        {
            ClipSizeBytes = await measure(CancellationToken.None);
        }
        catch (Exception)
        {
            // A size we could not work out just leaves the label showing length and smoothness.
            ClipSizeBytes = null;
        }
        finally
        {
            IsMeasuringClip = false;
        }
    }

    [RelayCommand]
    private void PlayPreview()
    {
        if (!PreviewSupported) return;
        _preview.Play(_data.Waveform, SelectionStart, SelectionEnd);
    }

    [RelayCommand]
    private void Confirm()
    {
        _preview.Stop();
        Completed?.Invoke(new MiningReviewResult(
            SelectionStart, SelectionEnd,
            string.IsNullOrEmpty(SentencePreview) ? null : SentencePreview,
            IncludeImage, IncludeAudio, Animated,
            SelectedDeck?.DeckId));
    }

    [RelayCommand]
    private void Cancel()
    {
        _preview.Stop();
        Completed?.Invoke(null);
    }

    [RelayCommand]
    private void KeepExistingImage() => IncludeImage = false;

    [RelayCommand]
    private void KeepExistingAudio() => IncludeAudio = false;

    /// A poster that cannot be decoded still uploads fine; only the preview is lost.
    private static Bitmap? TryDecode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string BuildOverwriteMessage() => (ReplacesImage, ReplacesAudio) switch
    {
        (true, true) => "This card already has a screenshot and audio. Mining will replace them.",
        (true, false) => "This card already has a screenshot. Mining will replace it.",
        (false, true) => "This card already has audio. Mining will replace it.",
        _ => ""
    };

    private void BuildContextText(MiningReviewData data)
    {
        var text = "";
        var currentStart = 0;
        var currentLength = 0;

        for (var i = 0; i < data.Context.Count; i++)
        {
            if (i > 0) text += CueSeparator;
            var start = text.Length;
            var line = data.Context[i].Text.ReplaceLineEndings(" ");
            text += line;
            _cueRanges.Add((start, line.Length));

            if (i == data.CurrentCueIndex)
            {
                currentStart = start;
                currentLength = line.Length;
            }
        }

        ContextText = text;
        SentenceSelectionStart = currentStart;
        SentenceSelectionEnd = currentStart + currentLength;
    }

    /// Snaps outward to whole lines when the drag crosses a separator, keeps the mined word inside
    /// the selection, then runs the result through the same formatter the mining path uses.
    private void UpdateSentence()
    {
        if (ContextText.Length == 0)
        {
            SentencePreview = "";
            return;
        }

        var (start, end) = Normalize(SentenceSelectionStart, SentenceSelectionEnd);
        (start, end) = SnapToLines(start, end);
        (start, end) = KeepSurfaceForm(start, end);

        var selected = ContextText[start..end].Trim();
        SentencePreview = SentenceFormatter.WithMarkers(selected, SurfaceForm) ?? "";
    }

    private (int, int) Normalize(int a, int b)
    {
        var start = Math.Clamp(Math.Min(a, b), 0, ContextText.Length);
        var end = Math.Clamp(Math.Max(a, b), start, ContextText.Length);
        return start == end ? (0, ContextText.Length) : (start, end);
    }

    private (int, int) SnapToLines(int start, int end)
    {
        // Intra-line word selection stays exact; only a selection that already spans a separator is
        // widened to whole lines, which is what "drag to extend across lines" should mean.
        var startCue = _cueRanges.FindIndex(r => start >= r.Start && start <= r.Start + r.Length);
        var endCue = _cueRanges.FindIndex(r => end >= r.Start && end <= r.Start + r.Length);
        if (startCue < 0 || endCue < 0 || startCue == endCue) return (start, end);

        return (_cueRanges[startCue].Start, _cueRanges[endCue].Start + _cueRanges[endCue].Length);
    }

    private (int, int) KeepSurfaceForm(int start, int end)
    {
        if (string.IsNullOrEmpty(SurfaceForm)) return (start, end);

        var wordAt = ContextText.IndexOf(SurfaceForm, StringComparison.Ordinal);
        if (wordAt < 0) return (start, end);

        return (Math.Min(start, wordAt), Math.Max(end, wordAt + SurfaceForm.Length));
    }
}

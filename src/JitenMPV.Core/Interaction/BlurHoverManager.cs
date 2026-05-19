using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Rendering;

namespace JitenMPV.Core.Interaction;

public sealed class BlurHoverManager
{
    private readonly Lock _lock = new();
    private readonly HashSet<(int WordId, byte ReadingIndex)> _revealedWords = [];
    private readonly HashSet<KnownState> _blurStates;
    private (int WordId, byte ReadingIndex)? _currentHover;
    private CancellationTokenSource? _unhoverCts;

    public event Action? WordUnrevealed;

    public BlurHoverManager(PluginSettings settings)
    {
        _blurStates = [..settings.BlurStates.Select(s => (KnownState)s)];
    }

    public HashSet<(int, byte)>? GetRevealedSnapshot()
    {
        lock (_lock) return _revealedWords.Count == 0 ? null : [.._revealedWords];
    }

    public bool UpdateHover(WordRect? hoveredWord, ParseCacheEntry entry, PluginSettings settings)
    {
        if (!settings.BlurEnabled || !settings.BlurRevealOnHover)
            return false;

        if (hoveredWord is null)
        {
            if (_currentHover is null) return false;

            var prev = _currentHover.Value;
            _currentHover = null;

            TaskHelper.CancelAndDispose(ref _unhoverCts);
            _unhoverCts = new CancellationTokenSource();
            var cts = _unhoverCts;

            _ = Task.Delay(settings.BlurRevealDelayMs, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                bool removed;
                lock (_lock) { removed = _revealedWords.Remove(prev); }
                if (removed)
                    WordUnrevealed?.Invoke();
            }, TaskScheduler.Default);

            return false;
        }

        var key = (hoveredWord.WordId, hoveredWord.ReadingIndex);

        if (!entry.VocabStates.TryGetValue(key, out var state) || !_blurStates.Contains(state))
            return false;

        if (_currentHover == key)
            return false;

        _unhoverCts?.Cancel();
        _currentHover = key;

        lock (_lock) { return _revealedWords.Add(key); }
    }

    public bool HasRevealed
    {
        get { lock (_lock) return _revealedWords.Count > 0; }
    }

    public bool IsRevealed(int wordId, byte readingIndex)
    {
        lock (_lock) return _revealedWords.Contains((wordId, readingIndex));
    }

    public void Reset()
    {
        TaskHelper.CancelAndDispose(ref _unhoverCts);
        lock (_lock) { _revealedWords.Clear(); }
        _currentHover = null;
    }
}

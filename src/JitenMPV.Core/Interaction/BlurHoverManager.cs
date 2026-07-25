using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Rendering;

namespace JitenMPV.Core.Interaction;

public sealed class BlurHoverManager
{
    private readonly Lock _lock = new();
    private readonly HashSet<(int WordId, byte ReadingIndex)> _revealedWords = [];
    private HashSet<KnownState> _blurStates;
    private volatile bool _blurEnabled;
    private volatile bool _blurRevealOnHover;
    private volatile int _blurRevealDelayMs;
    private (int WordId, byte ReadingIndex)? _currentHover;

    /// Cancelled on Reset so pending un-reveals from the previous subtitle cannot remove a word
    /// that the current subtitle has since revealed.
    private CancellationTokenSource _subtitleCts = new();

    public event Action? WordUnrevealed;

    public BlurHoverManager(PluginSettings settings)
    {
        _blurEnabled = settings.BlurEnabled;
        _blurRevealOnHover = settings.BlurRevealOnHover;
        _blurRevealDelayMs = settings.BlurRevealDelayMs;
        _blurStates = [..settings.BlurStates.Select(s => (KnownState)s)];
    }

    public void UpdateBlurStates(PluginSettings settings)
    {
        _blurEnabled = settings.BlurEnabled;
        _blurRevealOnHover = settings.BlurRevealOnHover;
        _blurRevealDelayMs = settings.BlurRevealDelayMs;
        _blurStates = [..settings.BlurStates.Select(s => (KnownState)s)];
    }

    public HashSet<(int, byte)>? GetRevealedSnapshot()
    {
        lock (_lock) return _revealedWords.Count == 0 ? null : [.._revealedWords];
    }

    public bool UpdateHover(WordRect? hoveredWord, ParseCacheEntry entry)
    {
        if (!_blurEnabled || !_blurRevealOnHover)
            return false;

        (int WordId, byte ReadingIndex)? next = null;
        if (hoveredWord is not null)
        {
            var key = (hoveredWord.WordId, hoveredWord.ReadingIndex);
            if (entry.VocabStates.TryGetValue(key, out var state) && _blurStates.Contains(state))
                next = key;
        }

        (int WordId, byte ReadingIndex)? previous;
        bool revealed;
        CancellationToken token;

        lock (_lock)
        {
            if (_currentHover == next) return false;

            previous = _currentHover;
            _currentHover = next;
            token = _subtitleCts.Token;
            revealed = next is not null && _revealedWords.Add(next.Value);
        }

        // Every transition away from a word schedules its re-blur, including word-to-word moves.
        if (previous is not null)
            ScheduleUnreveal(previous.Value, token);

        return revealed;
    }

    private void ScheduleUnreveal((int WordId, byte ReadingIndex) word, CancellationToken token)
    {
        _ = Task.Delay(_blurRevealDelayMs, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;

            bool removed;
            lock (_lock)
            {
                if (_currentHover == word) return;
                removed = _revealedWords.Remove(word);
            }

            if (removed)
                WordUnrevealed?.Invoke();
        }, TaskScheduler.Default);
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
        CancellationTokenSource stale;
        lock (_lock)
        {
            _revealedWords.Clear();
            _currentHover = null;
            stale = _subtitleCts;
            _subtitleCts = new CancellationTokenSource();
        }

        stale.Cancel();
        stale.Dispose();
    }
}

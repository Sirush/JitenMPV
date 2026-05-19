using JitenMPV.Core.Cache;
using JitenMPV.Core.Rendering;

namespace JitenMPV.Core.Interaction;

public sealed class PopupManager
{
    private readonly PopupDataBuilder _dataBuilder;
    private readonly IPopupPresenter _presenter;

    private (int WordId, byte ReadingIndex)? _currentWord;
    private volatile bool _mouseOverPopup;

    public bool IsVisible => _presenter.IsVisible;
    public (int WordId, byte ReadingIndex)? CurrentWord => _currentWord;
    public event Action<PopupAction>? ActionClicked;

    public PopupManager(PopupDataBuilder dataBuilder, IPopupPresenter presenter)
    {
        _dataBuilder = dataBuilder;
        _presenter = presenter;
        _presenter.ActionClicked += action => ActionClicked?.Invoke(action);
        _presenter.MouseEntered += () => _mouseOverPopup = true;
        _presenter.MouseLeft += () => _mouseOverPopup = false;
    }

    public bool IsMouseOverPopup => _mouseOverPopup;

    public async Task ShowAsync(WordRect word, ParseCacheEntry entry, CancellationToken ct)
    {
        var key = (word.WordId, word.ReadingIndex);
        if (_currentWord == key) return;

        _currentWord = key;
        await ShowForKeyAsync(key, entry, ct);
    }

    public async Task HideAsync(CancellationToken ct)
    {
        if (!_presenter.IsVisible) return;

        _currentWord = null;
        _mouseOverPopup = false;
        await _presenter.HideAsync(ct);
    }

    public async Task RefreshAsync(ParseCacheEntry entry, CancellationToken ct)
    {
        if (_currentWord is not { } key || !_presenter.IsVisible) return;
        await ShowForKeyAsync(key, entry, ct);
    }

    private async Task ShowForKeyAsync((int WordId, byte ReadingIndex) key, ParseCacheEntry entry, CancellationToken ct)
    {
        if (!entry.VocabDetails.TryGetValue(key, out var readerWord)) return;

        var token = entry.Tokens.Find(t => t.WordId == key.WordId && t.ReadingIndex == key.ReadingIndex);
        if (token is null) return;

        var cachedState = entry.VocabStates.GetValueOrDefault(key);
        var data = _dataBuilder.Build(readerWord, token, cachedState);
        await _presenter.ShowAsync(data, ct);
    }

    public void Reset()
    {
        _currentWord = null;
        _mouseOverPopup = false;
    }
}

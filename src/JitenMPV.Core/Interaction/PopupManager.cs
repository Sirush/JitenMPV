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
    public bool RequiresPointerTransferGrace =>
        _presenter.RequiresPointerTransferGrace;
    public (int WordId, byte ReadingIndex)? CurrentWord => _currentWord;
    public event Action<PopupAction>? ActionClicked;
    public event Action<int>? DeckSelected;
    public event Action<bool>? VisibilityChanged;

    public PopupManager(PopupDataBuilder dataBuilder, IPopupPresenter presenter)
    {
        _dataBuilder = dataBuilder;
        _presenter = presenter;
        _presenter.ActionClicked += action => ActionClicked?.Invoke(action);
        _presenter.DeckSelected += deckId => DeckSelected?.Invoke(deckId);
        _presenter.MouseEntered += () => _mouseOverPopup = true;
        _presenter.MouseLeft += () => _mouseOverPopup = false;
    }

    public bool IsMouseOverPopup => _mouseOverPopup;

    public async Task ShowAsync(
        WordRect word, ParseCacheEntry entry, PopupPointerPosition pointer, CancellationToken ct)
    {
        var key = (word.WordId, word.ReadingIndex);
        if (_currentWord == key) return;

        _currentWord = key;
        await ShowForKeyAsync(key, entry, pointer, ct);
    }

    public async Task HideAsync(CancellationToken ct)
    {
        if (!_presenter.IsVisible) return;

        _currentWord = null;
        _mouseOverPopup = false;
        await _presenter.HideAsync(ct);
        VisibilityChanged?.Invoke(false);
    }

    public async Task RefreshAsync(ParseCacheEntry entry, CancellationToken ct)
    {
        if (_currentWord is not { } key || !_presenter.IsVisible) return;
        await UpdateForKeyAsync(key, entry, ct);
    }

    private async Task ShowForKeyAsync(
        (int WordId, byte ReadingIndex) key, ParseCacheEntry entry,
        PopupPointerPosition pointer, CancellationToken ct)
    {
        var data = BuildPopupData(key, entry);
        if (data is null) return;
        bool wasVisible = _presenter.IsVisible;
        await _presenter.ShowAsync(data, pointer, ct);
        if (!wasVisible && _presenter.IsVisible)
            VisibilityChanged?.Invoke(true);
    }

    private async Task UpdateForKeyAsync((int WordId, byte ReadingIndex) key, ParseCacheEntry entry, CancellationToken ct)
    {
        var data = BuildPopupData(key, entry);
        if (data is null) return;
        await _presenter.UpdateAsync(data, ct);
    }

    private PopupData? BuildPopupData((int WordId, byte ReadingIndex) key, ParseCacheEntry entry)
    {
        if (!entry.VocabDetails.TryGetValue(key, out var readerWord)) return null;

        var token = entry.Tokens.Find(t => t.WordId == key.WordId && t.ReadingIndex == key.ReadingIndex);
        if (token is null) return null;

        var cachedState = entry.VocabStates.GetValueOrDefault(key);
        return _dataBuilder.Build(readerWord, token, cachedState);
    }

    public void Reset()
    {
        _currentWord = null;
        _mouseOverPopup = false;
    }
}

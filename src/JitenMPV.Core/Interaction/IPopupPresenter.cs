namespace JitenMPV.Core.Interaction;

public interface IPopupPresenter
{
    bool IsVisible { get; }
    PopupSupportLevel SupportLevel { get; }
    bool RequiresPointerTransferGrace { get; }

    void UpdateWindowContext(PopupWindowContext context);
    Task ShowAsync(PopupData data, PopupPointerPosition pointer, CancellationToken ct);
    Task UpdateAsync(PopupData data, CancellationToken ct);
    Task HideAsync(CancellationToken ct);

    event Action<PopupAction>? ActionClicked;
    /// Carries the study-deck id chosen from the popup's deck picker.
    event Action<int>? DeckSelected;
    event Action? MouseEntered;
    event Action? MouseLeft;
    event Action<PopupSupportLevel>? SupportLevelChanged;
}

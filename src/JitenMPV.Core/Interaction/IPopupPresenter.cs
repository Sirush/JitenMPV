namespace JitenMPV.Core.Interaction;

public interface IPopupPresenter
{
    bool IsVisible { get; }

    Task ShowAsync(PopupData data, CancellationToken ct);
    Task HideAsync(CancellationToken ct);

    event Action<PopupAction>? ActionClicked;
    event Action? MouseEntered;
    event Action? MouseLeft;
}

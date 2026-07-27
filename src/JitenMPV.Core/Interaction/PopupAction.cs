namespace JitenMPV.Core.Interaction;

public enum PopupAction
{
    NeverForget,
    Blacklist,
    Suspend,
    Forget,
    ReviewAgain,
    ReviewHard,
    ReviewGood,
    ReviewEasy,
    Mine,
    RotateForward,
    RotateBackward
}

public static class PopupActions
{
    public static bool IsReview(this PopupAction action)
        => action is PopupAction.ReviewAgain or PopupAction.ReviewHard
                  or PopupAction.ReviewGood or PopupAction.ReviewEasy;

    /// Keybind dictionaries are keyed by PopupAction name; unknown names are not review actions.
    public static bool IsReviewKeybind(string action)
        => Enum.TryParse<PopupAction>(action, out var parsed) && parsed.IsReview();
}

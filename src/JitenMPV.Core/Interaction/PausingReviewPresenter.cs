namespace JitenMPV.Core.Interaction;

/// Holds playback still while the review window is open, so the trim the user is dragging still
/// matches what is on screen behind it.
public sealed class PausingReviewPresenter(
    IMiningReviewPresenter inner, Action pause, Action resume) : IMiningReviewPresenter
{
    public async Task<MiningReviewResult?> ShowAsync(MiningReviewData data, CancellationToken ct)
    {
        pause();
        try
        {
            return await inner.ShowAsync(data, ct);
        }
        finally
        {
            resume();
        }
    }
}

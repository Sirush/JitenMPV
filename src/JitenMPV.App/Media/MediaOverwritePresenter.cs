using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using JitenMPV.App.Views;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Media;

public sealed class MediaOverwritePresenter : IMediaOverwritePresenter
{
    public async Task<MediaOverwriteAnswer> ConfirmAsync(MediaOverwriteData data, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new MediaOverwriteDialog(data);
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();

            await using var reg = ct.Register(() => Dispatcher.UIThread.Post(dialog.Close));

            dialog.Show();
            dialog.Activate();
            await closed.Task;

            return dialog.Answer;
        });
    }
}

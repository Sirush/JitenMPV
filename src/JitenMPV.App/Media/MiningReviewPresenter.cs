using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using JitenMPV.App.ViewModels;
using JitenMPV.App.Views;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Media;

public sealed class MiningReviewPresenter : IMiningReviewPresenter
{
    public async Task<MiningReviewResult?> ShowAsync(MiningReviewData data, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            using IAudioPreview preview = System.OperatingSystem.IsWindows()
                ? new WavPreviewPlayer()
                : new CliAudioPreview();
            var completion = new TaskCompletionSource<MiningReviewResult?>();

            var vm = new MiningReviewViewModel(data, preview);
            var window = new MiningReviewWindow { DataContext = vm };

            vm.Completed += result =>
            {
                completion.TrySetResult(result);
                window.Close();
            };

            // Closing by any other route - the window chrome, Esc - means cancel.
            window.Closed += (_, _) => completion.TrySetResult(null);

            await using var reg = ct.Register(() => Dispatcher.UIThread.Post(window.Close));

            window.Show();
            window.Activate();

            return await completion.Task;
        });
    }
}

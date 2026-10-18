using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using JitenMPV.App.Platform;
using JitenMPV.App.ViewModels;
using JitenMPV.App.Views;
using JitenMPV.Core.Tracking;

namespace JitenMPV.App.Media;

public sealed class EpisodePickerPresenter : IEpisodePickerPresenter
{
    private EpisodePickerWindow? _open;

    public async Task<EpisodePickerResult?> ShowAsync(EpisodePickerData data, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // A second press brings the open picker forward instead of stacking another.
            if (_open is { } open)
            {
                open.Activate();
                return null;
            }

            var completion = new TaskCompletionSource<EpisodePickerResult?>();
            var vm = new EpisodePickerViewModel(data);
            var window = new EpisodePickerWindow { DataContext = vm };
            _open = window;

            vm.Completed += result =>
            {
                completion.TrySetResult(result);
                window.Close();
            };
            window.Closed += (_, _) =>
            {
                _open = null;
                completion.TrySetResult(null);
            };

            await using var reg = ct.Register(() => Dispatcher.UIThread.Post(window.Close));

            window.Show();
            MacOsPluginIntegration.ActivateApplication();
            if (!System.OperatingSystem.IsWindows())
            {
                window.Topmost = false;
                window.Topmost = true;
            }
            window.Activate();


            return await completion.Task;
        });
    }
}

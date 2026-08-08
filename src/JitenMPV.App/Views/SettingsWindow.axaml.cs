using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using JitenMPV.App.ViewModels;
using JitenMPV.Core.Config;

namespace JitenMPV.App.Views;

public partial class SettingsWindow : Window
{
    public event Action<PluginSettings>? SettingsSaved;

    private SettingsViewModel? _subscribedVm;
    private bool _pendingSaveFlushed;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_subscribedVm is not null)
            {
                _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
                _subscribedVm.SettingsApplied -= OnSettingsApplied;
            }
            _subscribedVm = DataContext as SettingsViewModel;
            if (_subscribedVm is not null)
            {
                _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
                _subscribedVm.SettingsApplied += OnSettingsApplied;
            }
        };
    }

    private void OnSettingsApplied(PluginSettings settings) => SettingsSaved?.Invoke(settings);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedTabIndex))
            ContentScroll.Offset = default;
    }

    /// An edit still held by the autosave debounce would otherwise be lost to the close.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _pendingSaveFlushed) return;

        if (_subscribedVm is { HasPendingAutoSave: true } vm)
        {
            e.Cancel = true;
            _ = FlushThenCloseAsync(vm);
        }
    }

    private async Task FlushThenCloseAsync(SettingsViewModel vm)
    {
        await vm.FlushPendingSaveAsync();
        _pendingSaveFlushed = true;
        Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

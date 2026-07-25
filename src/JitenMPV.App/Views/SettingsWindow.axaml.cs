using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using JitenMPV.App.ViewModels;
using JitenMPV.Core.Config;

namespace JitenMPV.App.Views;

public partial class SettingsWindow : Window
{
    public event Action<PluginSettings>? SettingsSaved;

    private INotifyPropertyChanged? _subscribedVm;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_subscribedVm is not null)
                _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = DataContext as INotifyPropertyChanged;
            if (_subscribedVm is not null)
                _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedTabIndex))
            ContentScroll.Offset = default;
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            var settings = vm.ToPluginSettings();
            await SettingsManager.SaveAsync(settings);
            SettingsSaved?.Invoke(settings);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

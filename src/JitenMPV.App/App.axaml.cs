using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using JitenMPV.App.ViewModels;
using JitenMPV.App.Views;
using JitenMPV.Core.Config;

namespace JitenMPV.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var settings = await SettingsManager.LoadAsync();
                var vm = new SettingsViewModel(settings);
                desktop.MainWindow = new SettingsWindow { DataContext = vm };
            }
            catch (Exception ex)
            {
                desktop.MainWindow = new Avalonia.Controls.Window
                {
                    Title = "JitenMPV - Error",
                    Content = new Avalonia.Controls.TextBlock { Text = $"Failed to load settings: {ex.Message}" }
                };
            }
        }
    }
}
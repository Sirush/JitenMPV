using System;
using Avalonia.Controls;
using JitenMPV.App.Platform;

namespace JitenMPV.App.Views;

public partial class DictionaryPopupWindow : Window
{
    public DictionaryPopupWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        WindowsPopupWindowInterop.ConfigureNoActivate(this);
        MacOsPluginIntegration.ConfigurePopupWindow(this);
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Views;

public partial class MediaOverwriteDialog : Window
{
    private MediaOverwriteChoice _choice = MediaOverwriteChoice.CancelMine;

    public MediaOverwriteDialog()
    {
        InitializeComponent();
    }

    public MediaOverwriteDialog(MediaOverwriteData data) : this()
    {
        HeadlineText.Text = $"{data.Spelling} already has something attached.";
        DetailText.Text = Describe(data);
    }

    public MediaOverwriteAnswer Answer => new(_choice, DontAskCheck.IsChecked == true);

    private static string Describe(MediaOverwriteData data) => (data.ReplacesImage, data.ReplacesAudio) switch
    {
        (true, true) => "Mining this word again will replace its screenshot and its audio.",
        (true, false) => "Mining this word again will replace its screenshot.",
        (false, true) => "Mining this word again will replace its audio.",
        _ => "Mining this word again will replace what is already there."
    };

    private void OnReplace(object? sender, RoutedEventArgs e) => Finish(MediaOverwriteChoice.Replace);
    private void OnSkipMedia(object? sender, RoutedEventArgs e) => Finish(MediaOverwriteChoice.SkipMedia);
    private void OnCancelMine(object? sender, RoutedEventArgs e) => Finish(MediaOverwriteChoice.CancelMine);

    private void Finish(MediaOverwriteChoice choice)
    {
        _choice = choice;
        Close();
    }
}

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace JitenMPV.App.Overlay;

/// A frameless, transparent, never-activated window sized to mpv's client area. Click-through is
/// applied per platform on top of this; Avalonia's own hit testing is off so no pointer event can
/// be claimed here even where the OS layer leaks one.
internal sealed class SubtitleOverlayWindow : Window
{
    public SubtitleOverlayWindow()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.Manual;
        Topmost = false;
        IsHitTestVisible = false;
        Focusable = false;
        Width = 1;
        Height = 1;
    }
}

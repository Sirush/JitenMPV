using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ApplyWin32NoActivate();
        else if (OperatingSystem.IsMacOS())
            ApplyMacOsFullscreenOverlay();
    }

    private void ApplyWin32NoActivate()
    {
        var handle = TryGetPlatformHandle();
        if (handle is null) return;

        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        var hwnd = handle.Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void ApplyMacOsFullscreenOverlay()
    {
        var handle = TryGetPlatformHandle();
        if (handle is null
            || !string.Equals(
                handle.HandleDescriptor, "NSWindow",
                StringComparison.OrdinalIgnoreCase))
            return;

        const nuint CanJoinAllSpaces = 1 << 0;
        const nuint FullScreenPrimary = 1 << 7;
        const nuint FullScreenAuxiliary = 1 << 8;
        const nint AccessoryActivationPolicy = 1;
        var application = objc_msgSend_intptr(
            objc_getClass("NSApplication"),
            sel_registerName("sharedApplication"));
        objc_msgSend_bool_nint(
            application,
            sel_registerName("setActivationPolicy:"),
            AccessoryActivationPolicy);

        var getBehavior = sel_registerName("collectionBehavior");
        var setBehavior = sel_registerName("setCollectionBehavior:");
        var behavior = objc_msgSend_nuint(handle.Handle, getBehavior);
        objc_msgSend_void_nuint(
            handle.Handle,
            setBehavior,
            (behavior & ~FullScreenPrimary)
            | CanJoinAllSpaces
            | FullScreenAuxiliary);

        // The window is first mapped before OnOpened. Bring it forward again after admitting it to
        // the active fullscreen Space; this does not activate JitenMPV or take focus from mpv.
        objc_msgSend_void(handle.Handle, sel_registerName("orderFrontRegardless"));
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nuint objc_msgSend_nuint(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_nuint(
        IntPtr receiver, IntPtr selector, nuint value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_intptr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool_nint(
        IntPtr receiver, IntPtr selector, nint value);
}

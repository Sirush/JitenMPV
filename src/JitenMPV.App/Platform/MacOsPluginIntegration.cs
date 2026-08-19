using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace JitenMPV.App.Platform;

internal static class MacOsPluginIntegration
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    /// <summary>
    /// Runs JitenMPV as an accessory application while it is hosted by mpv. Accessory applications
    /// can show and activate settings or review windows without acquiring a separate Dock icon.
    /// </summary>
    public static void ConfigureApplication()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        const nint accessoryActivationPolicy = 1;
        var application = SharedApplication();
        if (application != IntPtr.Zero)
        {
            SendBoolNInt(
                application,
                sel_registerName("setActivationPolicy:"),
                accessoryActivationPolicy);
        }
    }

    /// <summary>
    /// Gives a user-requested application window keyboard focus. Dictionary popups never call this
    /// and therefore continue to leave focus with mpv.
    /// </summary>
    public static void ActivateApplication()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var application = SharedApplication();
        if (application != IntPtr.Zero)
        {
            SendVoidBool(
                application,
                sel_registerName("activateIgnoringOtherApps:"),
                true);
        }
    }

    /// <summary>
    /// Allows the dictionary popup to share mpv's fullscreen Space without making it a fullscreen
    /// window itself. The behavior is scoped to this NSWindow and does not activate JitenMPV.
    /// </summary>
    public static void ConfigurePopupWindow(Window window)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var handle = window.TryGetPlatformHandle();
        if (handle is null
            || !string.Equals(
                handle.HandleDescriptor, "NSWindow",
                StringComparison.OrdinalIgnoreCase))
            return;

        const nuint canJoinAllSpaces = 1 << 0;
        const nuint fullScreenPrimary = 1 << 7;
        const nuint fullScreenAuxiliary = 1 << 8;
        var behavior = SendNUInt(
            handle.Handle,
            sel_registerName("collectionBehavior"));
        SendVoidNUInt(
            handle.Handle,
            sel_registerName("setCollectionBehavior:"),
            (behavior & ~fullScreenPrimary)
            | canJoinAllSpaces
            | fullScreenAuxiliary);

        // OnOpened runs after the first map. Reorder the window after assigning its Space behavior
        // so it appears over an mpv window that is already fullscreen, without taking keyboard focus.
        SendVoid(handle.Handle, sel_registerName("orderFrontRegardless"));
    }

    /// Takes the subtitle overlay out of the event path entirely, so mpv keeps every click the
    /// window is drawn over.
    public static bool SetIgnoresMouseEvents(Window window)
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        var handle = window.TryGetPlatformHandle();
        if (handle is null
            || !string.Equals(
                handle.HandleDescriptor, "NSWindow",
                StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            SendVoidBool(
                handle.Handle,
                sel_registerName("setIgnoresMouseEvents:"),
                true);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static IntPtr SharedApplication() =>
        SendIntPtr(
            objc_getClass("NSApplication"),
            sel_registerName("sharedApplication"));

    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nuint SendNUInt(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidNUInt(
        IntPtr receiver, IntPtr selector, nuint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidBool(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBoolNInt(
        IntPtr receiver, IntPtr selector, nint value);
}

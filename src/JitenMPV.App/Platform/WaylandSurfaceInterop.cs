using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NWayland.Protocols.Wayland;

namespace JitenMPV.App.Platform;

internal enum WaylandCommitBehavior
{
    WithNextFrame,
    NoCommit
}

internal sealed class WaylandSurfaceContext
{
    public required WlSurface Surface { get; init; }
    public required object Globals { get; init; }
    public required double Scale { get; init; }
}

/// <summary>
/// The single seam between application code and Avalonia's worker-owned Wayland surface.
/// Avalonia does not currently publish this as a platform feature, so its version-sensitive
/// reflection is kept entirely inside this adapter.
/// </summary>
internal interface IWaylandSurfaceInterop
{
    ValueTask InvokeAsync(
        Action<WaylandSurfaceContext> action,
        WaylandCommitBehavior commitBehavior,
        CancellationToken ct);
}

internal static class WaylandSurfaceInterop
{
    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int _reportedMissingMember;

    public static bool IsNativeWayland(Window window) =>
        window.PlatformImpl?.GetType().Assembly.GetName().Name == "Avalonia.Wayland";

    public static IWaylandSurfaceInterop? TryCreate(
        Window window,
        bool createSurfaceIfMissing)
    {
        var impl = window.PlatformImpl;
        if (impl?.GetType().Assembly.GetName().Name != "Avalonia.Wayland")
            return null;

        var surfaceProxy = GetSurfaceProxy(impl);
        if (surfaceProxy is null && createSurfaceIfMissing)
        {
            // This is the only remaining workaround for the missing public Avalonia lifecycle
            // hook. Keeping it here means protocol backends no longer know platform internals.
            var showMethod = impl.GetType().GetMethod(
                "Show", InstanceFlags, null, [typeof(bool), typeof(bool)], null);
            if (showMethod is null)
                ReportMissingMember("Show(Boolean, Boolean)");
            else
                showMethod.Invoke(impl, [false, false]);
            surfaceProxy = GetSurfaceProxy(impl);
        }

        var client = FindProperty(impl.GetType(), "Client")?.GetValue(impl);
        var invokeMethod = client?.GetType()
            .GetMethods(InstanceFlags)
            .SingleOrDefault(method => method.Name == "InvokeOobAsync"
                                       && method.IsGenericMethodDefinition
                                       && method.GetGenericArguments().Length == 1
                                       && method.GetParameters().Length == 1);

        if (surfaceProxy is null)
            ReportMissingMember("SurfaceProxy/_surfaceProxy");
        else if (client is null)
            ReportMissingMember("Client");
        else if (invokeMethod is null)
            ReportMissingMember("Client.InvokeOobAsync<T>(Func<T>)");

        return surfaceProxy is null || client is null || invokeMethod is null
            ? null
            : new AvaloniaWaylandSurfaceInterop(
                window, surfaceProxy, client, invokeMethod);
    }

    private static object? GetSurfaceProxy(object impl) =>
        FindProperty(impl.GetType(), "SurfaceProxy")?.GetValue(impl)
        ?? FindField(impl.GetType(), "_surfaceProxy")?.GetValue(impl);

    private static bool TryGetSurface(
        object surfaceProxy,
        out object globals,
        out WlSurface surface)
    {
        globals = null!;
        surface = null!;

        var target = FindProperty(surfaceProxy.GetType(), "ProxyTarget")
            ?.GetValue(surfaceProxy);
        if (target is null)
            return false;

        globals = FindProperty(target.GetType(), "Globals")?.GetValue(target)!;
        surface = FindProperty(target.GetType(), "WlSurface")?.GetValue(target)
                  as WlSurface ?? null!;
        return globals is not null && surface is not null;
    }

    private static void ReportMissingMember(string member)
    {
        if (Interlocked.Exchange(ref _reportedMissingMember, 1) == 0)
            Trace.TraceWarning(
                "Native Wayland popup integration could not resolve Avalonia member '{0}'; "
                + "falling back to approximate placement.", member);
    }

    private static FieldInfo? FindField(Type? type, string name)
    {
        while (type is not null)
        {
            var field = type.GetField(
                name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field is not null)
                return field;
            type = type.BaseType;
        }

        return null;
    }

    private static PropertyInfo? FindProperty(Type? type, string name)
    {
        while (type is not null)
        {
            var property = type.GetProperty(
                name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (property is not null)
                return property;
            type = type.BaseType;
        }

        return null;
    }

    private sealed class AvaloniaWaylandSurfaceInterop(
        Window window,
        object surfaceProxy,
        object client,
        MethodInfo invokeMethod) : IWaylandSurfaceInterop
    {
        public async ValueTask InvokeAsync(
            Action<WaylandSurfaceContext> action,
            WaylandCommitBehavior commitBehavior,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var closedInvoke = invokeMethod.MakeGenericMethod(typeof(bool));
            Func<bool> callback = () =>
            {
                ct.ThrowIfCancellationRequested();
                if (!TryGetSurface(surfaceProxy, out var globals, out var surface))
                    return false;

                action(new WaylandSurfaceContext
                {
                    Surface = surface,
                    Globals = globals,
                    Scale = window.RenderScaling
                });
                return true;
            };

            var task = (Task)(closedInvoke.Invoke(client, [callback])
                              ?? throw new InvalidOperationException(
                                  "Avalonia Wayland worker returned no task."));
            await task.WaitAsync(ct);

            // Protocol state is synchronized with wl_surface state. Ask Avalonia for a frame
            // instead of committing its surface behind the toolkit's back.
            if (commitBehavior == WaylandCommitBehavior.WithNextFrame)
                window.InvalidateVisual();
        }
    }
}

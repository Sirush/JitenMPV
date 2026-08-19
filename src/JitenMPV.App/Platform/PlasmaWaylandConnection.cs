using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using JitenMPV.App.Popup;
using JitenMPV.Core.Interaction;
using NWayland.Protocols.Plasma.PlasmaShell;
using NWayland.Protocols.Plasma.PlasmaWindowManagement;
using NWayland.Protocols.Wayland;

namespace JitenMPV.App.Platform;

internal sealed class PlasmaWaylandConnectionStore
{
    private readonly ConditionalWeakTable<object, PlasmaWaylandConnection> _connections = new();
    private PlasmaWaylandConnection? _current;

    public PlasmaWaylandConnection? Current => _current;

    public event Action<PlasmaWaylandConnection>? ConnectionChanged;

    public PlasmaWaylandConnection Get(object globals)
    {
        var connection = _connections.GetValue(
            globals, static value => new PlasmaWaylandConnection(value));
        if (!ReferenceEquals(connection, _current))
        {
            _current = connection;
            ConnectionChanged?.Invoke(connection);
        }

        return connection;
    }
}

internal sealed class PlasmaWaylandConnection
{
    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int _reportedMissingBindMethod;

    private readonly object _gate = new();
    private readonly object _globals;
    private readonly ConditionalWeakTable<WlSurface, SurfaceState> _surfaces = new();
    private readonly Dictionary<uint, TrackedWindow> _windows = [];
    private readonly OrgKdePlasmaShell? _shell;
    private readonly OrgKdePlasmaWindowManagement? _management;

    public GeometryProviderStatus GeometryStatus { get; private set; }
    public bool HasPopupSurface => _shell is not null;

    public event Action? GeometryChanged;

    public PlasmaWaylandConnection(object globals)
    {
        _globals = globals;
        GeometryStatus = GeometryProviderStatus.Initializing;
        try
        {
            _shell = Bind<OrgKdePlasmaShell>(globals, 1, 8, null);
        }
        catch (Exception)
        {
            _shell = null;
        }

        try
        {
            var listener = new OrgKdePlasmaWindowManagement.Listener.Relay
            {
                OnWindowWithUuid = (management, _, uuid) =>
                    AddWindow(management, uuid)
            };
            _management = Bind<OrgKdePlasmaWindowManagement>(
                globals, 13, 19, listener);

            GeometryStatus = _management is { Version: >= 13 }
                ? GeometryProviderStatus.Ready
                : GeometryProviderStatus.Unsupported;
        }
        catch (Exception)
        {
            _management = null;
            GeometryStatus = GeometryProviderStatus.Faulted;
        }
    }

    public void Attach(WlSurface wlSurface)
    {
        if (_shell is null)
            return;
        _surfaces.GetValue(wlSurface, surface =>
        {
            var plasmaSurface = _shell.GetSurface(surface);
            plasmaSurface.SetRole(
                (uint)OrgKdePlasmaSurface.RoleEnum.Onscreendisplay);
            if (plasmaSurface.IsSetSkipTaskbarAvailable)
                plasmaSurface.SetSkipTaskbar(1);
            if (plasmaSurface.IsSetSkipSwitcherAvailable)
                plasmaSurface.SetSkipSwitcher(1);

            // Plasma shell roles are non-activating by default but remain pointer-interactive.
            return new SurfaceState(plasmaSurface);
        });
    }

    public void SetPosition(
        WlSurface wlSurface,
        int x,
        int y,
        OutputInfo output)
    {
        Attach(wlSurface);
        if (!_surfaces.TryGetValue(wlSurface, out var state))
            return;

        if (ResolveOutput(output) is { } wlOutput)
            state.Surface.SetOutput(wlOutput);
        state.Surface.SetPosition(x, y);
    }

    public void Detach(WlSurface wlSurface)
    {
        if (!_surfaces.TryGetValue(wlSurface, out var state))
            return;
        state.Surface.Destroy();
        _surfaces.Remove(wlSurface);
    }

    public bool TryGetGeometry(
        PopupWindowContext context,
        out ClientGeometry geometry)
    {
        lock (_gate)
        {
            var candidates = _windows.Values
                .Where(window => Geometry(window) is not null)
                .ToArray();

            var window = context.ProcessId is { } processId
                ? candidates.FirstOrDefault(
                    candidate => candidate.ProcessId == (uint)processId)
                : null;

            if (window is null && !string.IsNullOrWhiteSpace(context.AppId))
            {
                var appMatches = candidates
                    .Where(candidate => string.Equals(
                        candidate.AppId, context.AppId,
                        StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToArray();
                window = appMatches.Length == 1 ? appMatches[0] : null;

                if (window is null && !string.IsNullOrWhiteSpace(context.Title))
                {
                    var titleMatches = candidates
                        .Where(candidate => string.Equals(
                            candidate.AppId, context.AppId,
                            StringComparison.OrdinalIgnoreCase)
                            && string.Equals(
                                candidate.Title, context.Title,
                                StringComparison.Ordinal))
                        .Take(2)
                        .ToArray();
                    window = titleMatches.Length == 1 ? titleMatches[0] : null;
                }
            }

            // Old/external launchers may not pass an app ID. Keep their former single-mpv
            // behavior as an explicit compatibility path, never as a guess after a supplied
            // identity failed to match.
            if (window is null
                && string.IsNullOrWhiteSpace(context.AppId))
            {
                var mpvWindows = candidates
                    .Where(candidate => string.Equals(
                        candidate.AppId, "mpv",
                        StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToArray();
                window = mpvWindows.Length == 1 ? mpvWindows[0] : null;
            }

            if (window is null || Geometry(window) is not { } client)
            {
                geometry = default;
                return false;
            }

            geometry = new ClientGeometry(
                new GlobalLogicalPoint(client.X, client.Y),
                new LogicalSize(client.Width, client.Height));
            return true;
        }
    }

    private void AddWindow(
        OrgKdePlasmaWindowManagement management,
        string uuid)
    {
        var tracked = new TrackedWindow();
        var listener = new OrgKdePlasmaWindow.Listener.Relay
        {
            OnUnmapped = _ => RemoveWindow(tracked),
            OnAppIdChanged = (_, appId) =>
            {
                lock (_gate)
                    tracked.AppId = appId;
            },
            OnTitleChanged = (_, title) =>
            {
                lock (_gate)
                    tracked.Title = title;
            },
            OnPidChanged = (_, processId) =>
            {
                lock (_gate)
                    tracked.ProcessId = processId;
            },
            OnGeometry = (_, x, y, width, height) =>
            {
                lock (_gate)
                    tracked.FrameGeometry = new LogicalRect(
                        x, y, width, height);
                GeometryChanged?.Invoke();
            },
            OnClientGeometry = (_, x, y, width, height) =>
            {
                lock (_gate)
                    tracked.ClientGeometry = new LogicalRect(
                        x, y, width, height);
                GeometryChanged?.Invoke();
            }
        };

        var proxy = management.GetWindowByUuid(uuid, listener);
        tracked.Proxy = proxy;
        lock (_gate)
            _windows[proxy.Id] = tracked;
    }

    private static LogicalRect? Geometry(TrackedWindow window) =>
        window.ClientGeometry ?? window.FrameGeometry;

    private void RemoveWindow(TrackedWindow tracked)
    {
        lock (_gate)
        {
            if (tracked.Proxy is not null)
                _windows.Remove(tracked.Proxy.Id);
        }

        if (tracked.Proxy is { IsDisposed: false, Version: >= 4 })
            tracked.Proxy.Destroy();
        GeometryChanged?.Invoke();
    }

    private static T? Bind<T>(
        object globals,
        uint minimumVersion,
        uint maximumVersion,
        object? listener)
    {
        MethodInfo bindMethod;
        try
        {
            bindMethod = globals.GetType()
                .GetMethods(InstanceFlags)
                .Single(method => method.Name == "Bind"
                                  && method.IsGenericMethodDefinition
                                  && method.GetParameters().Length == 3);
        }
        catch (InvalidOperationException)
        {
            if (Interlocked.Exchange(ref _reportedMissingBindMethod, 1) == 0)
                Trace.TraceWarning(
                    "Plasma Wayland integration could not resolve Avalonia member "
                    + "'{0}.Bind<T>(UInt32, UInt32, listener)'; falling back to approximate "
                    + "placement.",
                    globals.GetType().FullName);
            throw;
        }

        return (T?)bindMethod.MakeGenericMethod(typeof(T))
            .Invoke(globals, [minimumVersion, maximumVersion, listener]);
    }

    private WlOutput? ResolveOutput(OutputInfo wanted)
    {
        var tracker = _globals.GetType().GetProperty(
            "Outputs", InstanceFlags)?.GetValue(_globals);
        if (tracker?.GetType().GetProperty(
                "Outputs", InstanceFlags)?.GetValue(tracker)
            is not IEnumerable outputs)
            return null;

        object? descriptionMatch = null;
        object? geometryMatch = null;
        foreach (var output in outputs)
        {
            if (output is null)
                continue;

            var type = output.GetType();
            var name = type.GetProperty(
                "OutputName", InstanceFlags)?.GetValue(output) as string;
            if (!string.IsNullOrWhiteSpace(wanted.ConnectorName)
                && string.Equals(
                    name,
                    wanted.ConnectorName,
                    StringComparison.OrdinalIgnoreCase))
                return WlOutputFrom(output);

            var description = type.GetProperty(
                "OutputDescription", InstanceFlags)?.GetValue(output) as string;
            if (descriptionMatch is null
                && !string.IsNullOrWhiteSpace(wanted.Description)
                && string.Equals(
                    description,
                    wanted.Description,
                    StringComparison.Ordinal))
                descriptionMatch = output;

            if (geometryMatch is null && OutputGeometryMatches(output, wanted.Bounds))
                geometryMatch = output;
        }

        return WlOutputFrom(descriptionMatch ?? geometryMatch);
    }

    private static bool OutputGeometryMatches(
        object output,
        LogicalRect wanted)
    {
        var type = output.GetType();
        var position = type.GetProperty(
            "LogicalPosition", InstanceFlags)?.GetValue(output);
        var size = type.GetProperty(
            "LogicalSize", InstanceFlags)?.GetValue(output);
        if (position is null || size is null)
            return false;

        return ReadNumber(position, "X") == wanted.X
               && ReadNumber(position, "Y") == wanted.Y
               && ReadNumber(size, "Width") == wanted.Width
               && ReadNumber(size, "Height") == wanted.Height;
    }

    private static double? ReadNumber(object value, string propertyName)
    {
        var raw = value.GetType().GetProperty(
            propertyName, InstanceFlags)?.GetValue(value);
        return raw is null ? null : Convert.ToDouble(raw);
    }

    private static WlOutput? WlOutputFrom(object? output) =>
        output?.GetType().GetProperty(
            "WlOutput", InstanceFlags)?.GetValue(output) as WlOutput;

    internal readonly record struct ClientGeometry(
        GlobalLogicalPoint Origin,
        LogicalSize Size);

    private sealed class TrackedWindow
    {
        public OrgKdePlasmaWindow? Proxy { get; set; }
        public string? AppId { get; set; }
        public string? Title { get; set; }
        public uint ProcessId { get; set; }
        public LogicalRect? FrameGeometry { get; set; }
        public LogicalRect? ClientGeometry { get; set; }
    }

    private sealed record SurfaceState(OrgKdePlasmaSurface Surface);
}

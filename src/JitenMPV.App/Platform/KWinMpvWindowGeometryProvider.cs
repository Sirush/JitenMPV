using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JitenMPV.App.Popup;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Platform;

/// <summary>
/// Normalizes KWin's v13+ foreign-toplevel geometry behind the shared provider interface.
/// Client geometry is preferred when the compositor supports v18; older compositors expose
/// frame geometry only.
/// </summary>
internal sealed class KWinMpvWindowGeometryProvider : IMpvWindowGeometryProvider
{
    private static readonly TimeSpan InitialGeometryTimeout =
        TimeSpan.FromMilliseconds(750);
    private static readonly OutputInfo UnknownOutput = new(
        null, null, default, default, 1, null);

    private readonly PlasmaWaylandConnectionStore _connections;
    private PlasmaWaylandConnection? _connection;

    public KWinMpvWindowGeometryProvider(
        PlasmaWaylandConnectionStore connections)
    {
        _connections = connections;
        _connections.ConnectionChanged += SetConnection;
    }

    public event Action? GeometryChanged;

    public GeometryProviderStatus Status =>
        _connection?.GeometryStatus ?? GeometryProviderStatus.Initializing;

    public async ValueTask<MpvWindowGeometry?> GetGeometryAsync(
        PopupWindowContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var connection = _connection;
        if (connection is null)
            return null;

        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (connection.TryGetGeometry(context, out var geometry))
            {
                return new MpvWindowGeometry(
                    geometry.Origin,
                    geometry.Size,
                    UnknownOutput,
                    context.IsFullscreen,
                    1);
            }

            var changed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnGeometryChanged() => changed.TrySetResult();

            connection.GeometryChanged += OnGeometryChanged;
            try
            {
                // Close the race between the first query and event subscription.
                if (connection.TryGetGeometry(context, out geometry))
                    continue;

                var remaining =
                    InitialGeometryTimeout - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                    return null;

                try
                {
                    await changed.Task.WaitAsync(remaining, ct);
                }
                catch (TimeoutException)
                {
                    return null;
                }
            }
            finally
            {
                connection.GeometryChanged -= OnGeometryChanged;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _connections.ConnectionChanged -= SetConnection;
        SetConnection(null);
        return ValueTask.CompletedTask;
    }

    private void SetConnection(PlasmaWaylandConnection? connection)
    {
        if (ReferenceEquals(connection, _connection))
            return;
        if (_connection is not null)
            _connection.GeometryChanged -= OnGeometryChanged;
        _connection = connection;
        if (_connection is not null)
            _connection.GeometryChanged += OnGeometryChanged;
    }

    private void OnGeometryChanged() => GeometryChanged?.Invoke();
}

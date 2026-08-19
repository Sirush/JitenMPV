using System;
using JitenMPV.Core.Config;

namespace JitenMPV.App.Popup;

public readonly record struct SurfacePoint(double X, double Y);
public readonly record struct GlobalLogicalPoint(double X, double Y);
public readonly record struct LogicalSize(double Width, double Height);

public readonly record struct LogicalRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public sealed record OutputInfo(
    string? ConnectorName,
    string? Description,
    LogicalRect Bounds,
    LogicalRect WorkingArea,
    double Scale,
    object? NativeHandle);

public sealed record MpvWindowGeometry(
    GlobalLogicalPoint ClientOrigin,
    LogicalSize ClientSize,
    OutputInfo Output,
    bool IsFullscreen,
    double Scale);

public sealed record PopupPlacementRequest(
    PopupPositionMode PositionMode,
    PopupAnchor FixedAnchor,
    double Offset,
    GlobalLogicalPoint? Pointer);

public sealed record PopupPlacement(
    GlobalLogicalPoint Position,
    OutputInfo Output);

public interface IPopupPositionCalculator
{
    PopupPlacement Calculate(
        PopupPlacementRequest request,
        MpvWindowGeometry geometry,
        LogicalSize popupSize);
}

/// <summary>
/// Owns every above/below, fixed-anchor, edge-flipping and clamping rule. Platform code only
/// supplies normalized logical geometry and applies the resulting position.
/// </summary>
public sealed class PopupPositionCalculator : IPopupPositionCalculator
{
    public PopupPlacement Calculate(
        PopupPlacementRequest request,
        MpvWindowGeometry geometry,
        LogicalSize popupSize)
    {
        var workArea = geometry.Output.WorkingArea;
        var anchor = request.PositionMode == PopupPositionMode.Fixed
            ? request.FixedAnchor
            : request.Pointer is null
                ? PopupAnchor.BottomCenter
                : request.FixedAnchor;

        var candidate = request.PositionMode == PopupPositionMode.Fixed
                        || request.Pointer is null
            ? AnchoredPosition(workArea, popupSize, anchor, request.Offset)
            : CursorRelativePosition(
                request.Pointer.Value, workArea, popupSize,
                request.PositionMode, request.Offset);

        return new PopupPlacement(
            new GlobalLogicalPoint(
                Clamp(candidate.X, workArea.X,
                    Math.Max(workArea.X, workArea.Right - popupSize.Width)),
                Clamp(candidate.Y, workArea.Y,
                    Math.Max(workArea.Y, workArea.Bottom - popupSize.Height))),
            geometry.Output);
    }

    private static GlobalLogicalPoint CursorRelativePosition(
        GlobalLogicalPoint pointer,
        LogicalRect workArea,
        LogicalSize popupSize,
        PopupPositionMode positionMode,
        double offset)
    {
        var x = pointer.X - popupSize.Width / 2;

        if (positionMode == PopupPositionMode.BelowSubtitle)
        {
            var below = pointer.Y + offset;
            return new GlobalLogicalPoint(
                x,
                below + popupSize.Height > workArea.Bottom
                    ? pointer.Y - popupSize.Height - offset
                    : below);
        }

        var above = pointer.Y - popupSize.Height - offset;
        return new GlobalLogicalPoint(
            x,
            above < workArea.Y ? pointer.Y + offset : above);
    }

    private static GlobalLogicalPoint AnchoredPosition(
        LogicalRect workArea,
        LogicalSize popupSize,
        PopupAnchor anchor,
        double offset)
    {
        var x = anchor switch
        {
            PopupAnchor.TopLeft or PopupAnchor.BottomLeft => workArea.X + offset,
            PopupAnchor.TopRight or PopupAnchor.BottomRight =>
                workArea.Right - popupSize.Width - offset,
            _ => workArea.X + (workArea.Width - popupSize.Width) / 2
        };

        var top = anchor is PopupAnchor.TopLeft
            or PopupAnchor.TopCenter
            or PopupAnchor.TopRight;
        return new GlobalLogicalPoint(
            x,
            top
                ? workArea.Y + offset
                : workArea.Bottom - popupSize.Height - offset);
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Min(Math.Max(value, minimum), maximum);
}

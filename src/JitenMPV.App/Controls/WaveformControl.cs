using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace JitenMPV.App.Controls;

/// Peak display with draggable selection handles over a fixed decode window.
public sealed class WaveformControl : Control
{
    private const double HandleGrabPx = 12;
    private const double HandleWidthPx = 3;
    private const double MinSelectionSeconds = 0.20;

    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#0F0F12"));
    private static readonly IBrush SubtitleBand = new SolidColorBrush(Color.Parse("#1E1E24"));
    private static readonly IBrush DimPeak = new SolidColorBrush(Color.Parse("#3F3F46"));
    private static readonly IBrush AccentPeak = new SolidColorBrush(Color.Parse("#C084FC"));
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.Parse("#E9D5FF"));
    private static readonly IPen PlayheadPen = new Pen(new SolidColorBrush(Color.Parse("#FAFAFA")), 1);

    private enum Drag { None, Start, End, New }

    private Drag _drag = Drag.None;
    private double _dragAnchor;

    public static readonly StyledProperty<float[]?> PeaksProperty =
        AvaloniaProperty.Register<WaveformControl, float[]?>(nameof(Peaks));

    public static readonly StyledProperty<double> WindowStartProperty =
        AvaloniaProperty.Register<WaveformControl, double>(nameof(WindowStart));

    public static readonly StyledProperty<double> WindowDurationProperty =
        AvaloniaProperty.Register<WaveformControl, double>(nameof(WindowDuration), 1.0);

    public static readonly StyledProperty<double> SelectionStartProperty =
        AvaloniaProperty.Register<WaveformControl, double>(
            nameof(SelectionStart), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> SelectionEndProperty =
        AvaloniaProperty.Register<WaveformControl, double>(
            nameof(SelectionEnd), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> SubtitleStartProperty =
        AvaloniaProperty.Register<WaveformControl, double>(nameof(SubtitleStart));

    public static readonly StyledProperty<double> SubtitleEndProperty =
        AvaloniaProperty.Register<WaveformControl, double>(nameof(SubtitleEnd));

    public static readonly StyledProperty<double> PlayheadPositionProperty =
        AvaloniaProperty.Register<WaveformControl, double>(nameof(PlayheadPosition), double.NaN);

    public float[]? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public double WindowStart { get => GetValue(WindowStartProperty); set => SetValue(WindowStartProperty, value); }
    public double WindowDuration { get => GetValue(WindowDurationProperty); set => SetValue(WindowDurationProperty, value); }
    public double SelectionStart { get => GetValue(SelectionStartProperty); set => SetValue(SelectionStartProperty, value); }
    public double SelectionEnd { get => GetValue(SelectionEndProperty); set => SetValue(SelectionEndProperty, value); }
    public double SubtitleStart { get => GetValue(SubtitleStartProperty); set => SetValue(SubtitleStartProperty, value); }
    public double SubtitleEnd { get => GetValue(SubtitleEndProperty); set => SetValue(SubtitleEndProperty, value); }
    public double PlayheadPosition { get => GetValue(PlayheadPositionProperty); set => SetValue(PlayheadPositionProperty, value); }

    static WaveformControl()
    {
        AffectsRender<WaveformControl>(
            PeaksProperty, WindowStartProperty, WindowDurationProperty,
            SelectionStartProperty, SelectionEndProperty,
            SubtitleStartProperty, SubtitleEndProperty, PlayheadPositionProperty);
    }

    public WaveformControl()
    {
        Cursor = new Cursor(StandardCursorType.Ibeam);
    }

    private double WindowEnd => WindowStart + Math.Max(WindowDuration, 0.001);

    // The only time/pixel conversion in the control, so render and hit-test cannot drift apart.
    private double TimeToX(double time)
        => (time - WindowStart) / Math.Max(WindowDuration, 0.001) * Bounds.Width;

    private double XToTime(double x)
        => WindowStart + Math.Clamp(x / Math.Max(Bounds.Width, 1), 0, 1) * Math.Max(WindowDuration, 0.001);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        context.FillRectangle(Background, new Rect(0, 0, width, height));

        if (SubtitleEnd > SubtitleStart)
        {
            var bandLeft = TimeToX(SubtitleStart);
            var bandRight = TimeToX(SubtitleEnd);
            context.FillRectangle(SubtitleBand,
                new Rect(bandLeft, 0, Math.Max(1, bandRight - bandLeft), height));
        }

        var peaks = Peaks;
        if (peaks is { Length: > 0 })
        {
            var mid = height / 2;
            var selLeft = TimeToX(SelectionStart);
            var selRight = TimeToX(SelectionEnd);
            var columnWidth = width / peaks.Length;

            for (var i = 0; i < peaks.Length; i++)
            {
                var x = i * columnWidth;
                var amplitude = Math.Max(1, peaks[i] * (height / 2 - 2));
                var inSelection = x + columnWidth / 2 >= selLeft && x + columnWidth / 2 <= selRight;
                context.FillRectangle(inSelection ? AccentPeak : DimPeak,
                    new Rect(x, mid - amplitude, Math.Max(1, columnWidth - 0.5), amplitude * 2));
            }
        }

        DrawHandle(context, TimeToX(SelectionStart), height);
        DrawHandle(context, TimeToX(SelectionEnd), height);

        var playhead = PlayheadPosition;
        if (!double.IsNaN(playhead) && playhead >= WindowStart && playhead <= WindowEnd)
        {
            var x = TimeToX(playhead);
            context.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, height));
        }
    }

    private static void DrawHandle(DrawingContext context, double x, double height)
        => context.FillRectangle(HandleBrush, new Rect(x - HandleWidthPx / 2, 0, HandleWidthPx, height));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (WindowDuration <= 0) return;

        var x = e.GetPosition(this).X;
        var startDistance = Math.Abs(x - TimeToX(SelectionStart));
        var endDistance = Math.Abs(x - TimeToX(SelectionEnd));

        if (startDistance <= HandleGrabPx || endDistance <= HandleGrabPx)
        {
            _drag = startDistance <= endDistance ? Drag.Start : Drag.End;
        }
        else
        {
            _drag = Drag.New;
            _dragAnchor = XToTime(x);
            SetSelection(_dragAnchor, _dragAnchor + MinSelectionSeconds);
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var x = e.GetPosition(this).X;
        if (_drag == Drag.None)
        {
            var overHandle = Math.Abs(x - TimeToX(SelectionStart)) <= HandleGrabPx
                             || Math.Abs(x - TimeToX(SelectionEnd)) <= HandleGrabPx;
            Cursor = new Cursor(overHandle ? StandardCursorType.SizeWestEast : StandardCursorType.Ibeam);
            return;
        }

        var time = XToTime(x);
        switch (_drag)
        {
            case Drag.Start:
                SetSelection(time, SelectionEnd);
                break;
            case Drag.End:
                SetSelection(SelectionStart, time);
                break;
            case Drag.New:
                SetSelection(Math.Min(_dragAnchor, time), Math.Max(_dragAnchor, time));
                break;
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = Drag.None;
        e.Pointer.Capture(null);
    }

    private void SetSelection(double start, double end)
    {
        var windowEnd = WindowEnd;
        var minWidth = Math.Min(MinSelectionSeconds, WindowDuration);

        start = Math.Clamp(start, WindowStart, windowEnd - minWidth);
        end = Math.Clamp(end, start + minWidth, windowEnd);

        SelectionStart = start;
        SelectionEnd = end;
    }
}

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Controls;

/// Mora/contour diagram in the Reader extension's visual language: a polyline stepping between a
/// high and a low row, a filled dot per mora, and a hollow dot for the following particle.
public sealed class PitchDiagramControl : Control
{
    private const double StepX = 18;
    private const double PadX = 9;
    private const double DiagramHeight = 34;
    private const double HighY = 5;
    private const double LowY = 17;
    private const double Radius = 3;
    private const double TextOffset = 8;
    private const double FontSize = 9;
    private const double StrokeWidth = 1.5;

    public static readonly StyledProperty<PitchDiagramRow?> RowProperty =
        AvaloniaProperty.Register<PitchDiagramControl, PitchDiagramRow?>(nameof(Row));

    public PitchDiagramRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    static PitchDiagramControl()
    {
        AffectsRender<PitchDiagramControl>(RowProperty);
        AffectsMeasure<PitchDiagramControl>(RowProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
        => Row is { } row
            ? new Size(row.Diagram.Pattern.Count * StepX, DiagramHeight)
            : default;

    public override void Render(DrawingContext context)
    {
        if (Row is not { } row) return;

        var pattern = row.Diagram.Pattern;
        var morae = row.Diagram.Morae;
        if (pattern.Count == 0) return;

        var color = Color.TryParse(row.Color, out var parsed) ? parsed : Colors.Gray;
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, StrokeWidth);
        // Naming a family here would bypass the fallback chain registered in BuildAvaloniaApp,
        // which is what supplies the kana glyphs the default UI font lacks.
        var typeface = new Typeface(FontFamily.Default, weight: FontWeight.Bold);

        var points = new Point[pattern.Count];
        for (var i = 0; i < pattern.Count; i++)
            points[i] = new Point(PadX + i * StepX, pattern[i] ? HighY : LowY);

        for (var i = 1; i < points.Length; i++)
            context.DrawLine(pen, points[i - 1], points[i]);

        for (var i = 0; i < points.Length; i++)
        {
            // The trailing point is the particle after the word, drawn hollow to mark that it is
            // not part of the word itself.
            var isParticle = i == points.Length - 1;
            context.DrawEllipse(isParticle ? Brushes.White : brush, pen, points[i], Radius, Radius);

            if (isParticle || i >= morae.Count) continue;

            var text = new FormattedText(morae[i], CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, FontSize, brush);
            context.DrawText(text, new Point(points[i].X - text.Width / 2, points[i].Y + TextOffset));
        }
    }
}

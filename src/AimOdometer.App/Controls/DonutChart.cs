using System.Windows;
using System.Windows.Media;

namespace AimOdometer.App.Controls;

/// <summary>One share of the donut.</summary>
public sealed record DonutSlice(double Value, Brush Brush);

/// <summary>Ring chart of shares (e.g. distance per game). Put any text in the middle with an overlay.</summary>
public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(
        nameof(Slices), typeof(IReadOnlyList<DonutSlice>), typeof(DonutChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(DonutChart),
        new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<DonutSlice>? Slices
    {
        get => (IReadOnlyList<DonutSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2;
        drawingContext.DrawEllipse(null, new Pen((Brush)FindResource("SurfaceElevatedBrush"), Thickness), center, radius, radius);

        var slices = Slices?.Where(s => s.Value > 0).ToList();
        var total = slices?.Sum(s => s.Value) ?? 0;
        if (slices is not { Count: > 0 } || total <= 0)
        {
            return;
        }

        if (slices.Count == 1)
        {
            drawingContext.DrawEllipse(null, new Pen(slices[0].Brush, Thickness), center, radius, radius);
            return;
        }

        var gapDegrees = 2.0;
        var angle = -90.0;
        foreach (var slice in slices)
        {
            var sweep = (slice.Value / total * 360) - gapDegrees;
            if (sweep > 0.5)
            {
                drawingContext.DrawGeometry(null, new Pen(slice.Brush, Thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat },
                    Arc(center, radius, angle + (gapDegrees / 2), sweep));
            }

            angle += slice.Value / total * 360;
        }
    }

    private static StreamGeometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        static Point On(Point c, double r, double degrees)
        {
            var rad = degrees * Math.PI / 180;
            return new Point(c.X + (r * Math.Cos(rad)), c.Y + (r * Math.Sin(rad)));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(On(center, radius, startDegrees), isFilled: false, isClosed: false);
            ctx.ArcTo(On(center, radius, startDegrees + sweepDegrees), new Size(radius, radius), 0,
                sweepDegrees > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }
}

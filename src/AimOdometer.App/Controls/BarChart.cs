using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AimOdometer.App.Controls;

/// <summary>One bar: its value, an axis label and the text shown on hover.</summary>
public sealed record ChartBar(double Value, string Label, string Tooltip, bool Highlight = false);

/// <summary>
/// Minimal, fast bar chart drawn directly with DrawingContext: rounded bars, a few axis labels and a hover readout.
/// No chart library needed for the handful of charts this app has.
/// </summary>
public sealed class BarChart : FrameworkElement
{
    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars), typeof(IReadOnlyList<ChartBar>), typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((BarChart)d)._hover = -1));

    private const double LabelHeight = 20;
    private const double TooltipHeight = 26;
    private int _hover = -1;

    public BarChart()
    {
        MinHeight = 120;
        Focusable = false;
    }

    public IReadOnlyList<ChartBar>? Bars
    {
        get => (IReadOnlyList<ChartBar>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    private Brush Resource(string key) => (Brush)FindResource(key);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexAt(e.GetPosition(this).X);
        if (index != _hover)
        {
            _hover = index;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }

    private int IndexAt(double x)
    {
        var bars = Bars;
        if (bars is not { Count: > 0 } || ActualWidth <= 0)
        {
            return -1;
        }

        var index = (int)(x / (ActualWidth / bars.Count));
        return index >= 0 && index < bars.Count ? index : -1;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Transparent background so the whole area receives mouse events.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var bars = Bars;
        if (bars is not { Count: > 0 })
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var chartTop = TooltipHeight + 4;
        var chartHeight = Math.Max(10, ActualHeight - LabelHeight - chartTop);
        var slot = ActualWidth / bars.Count;
        var gap = Math.Clamp(slot * 0.25, 1, 8);
        var barWidth = Math.Max(1, slot - gap);
        var max = Math.Max(bars.Max(b => b.Value), 1e-9);
        var radius = Math.Min(4, barWidth / 2);

        var normal = Resource("BarBrush");
        var accent = Resource("AccentBrush");
        var label = Resource("TextMutedBrush");
        drawingContext.DrawRectangle(Resource("BorderBrush"), null, new Rect(0, chartTop + chartHeight, ActualWidth, 1));

        for (var i = 0; i < bars.Count; i++)
        {
            var height = bars[i].Value <= 0 ? 0 : Math.Max(3, bars[i].Value / max * chartHeight);
            var rect = new Rect((i * slot) + (gap / 2), chartTop + chartHeight - height, barWidth, height);
            if (height > 0)
            {
                drawingContext.DrawRoundedRectangle(i == _hover || bars[i].Highlight ? accent : normal, null, rect, radius, radius);
            }
        }

        // Axis labels: first, middle and last bar (more would collide on narrow windows).
        foreach (var i in new[] { 0, bars.Count / 2, bars.Count - 1 }.Distinct())
        {
            var text = Text(bars[i].Label, 11, label, dpi);
            var x = Math.Clamp((i * slot) + (slot / 2) - (text.Width / 2), 0, Math.Max(0, ActualWidth - text.Width));
            drawingContext.DrawText(text, new Point(x, chartTop + chartHeight + 4));
        }

        if (_hover >= 0 && _hover < bars.Count)
        {
            var tip = Text(bars[_hover].Tooltip, 12, Resource("TextPrimaryBrush"), dpi);
            var width = tip.Width + 16;
            var x = Math.Clamp((_hover * slot) + (slot / 2) - (width / 2), 0, Math.Max(0, ActualWidth - width));
            drawingContext.DrawRoundedRectangle(Resource("SurfaceElevatedBrush"), new Pen(Resource("BorderStrongBrush"), 1),
                new Rect(x, 0, width, TooltipHeight - 2), 6, 6);
            drawingContext.DrawText(tip, new Point(x + 8, (TooltipHeight - 2 - tip.Height) / 2));
        }
    }

    private static FormattedText Text(string text, double size, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text, Segoe UI"), size, brush, dpi);
}

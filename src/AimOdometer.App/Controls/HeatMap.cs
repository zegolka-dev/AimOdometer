using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AimOdometer.App.Controls;

/// <summary>Distance per weekday (rows, starting with the culture's first day) and hour (columns).</summary>
public sealed record HeatMapData(double[,] Values, DayOfWeek FirstDay, CultureInfo Culture, Func<double, string> FormatValue);

/// <summary>Activity heat map: 7 x 24 cells, brighter = more distance. Hover shows the exact value.</summary>
public sealed class HeatMap : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(HeatMapData), typeof(HeatMap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LabelWidth = 34;
    private const double HeaderHeight = 18;
    private const double FooterHeight = 26;
    private (int Row, int Hour) _hover = (-1, -1);

    public HeatMapData? Data
    {
        get => (HeatMapData?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, HeaderHeight + (7 * 22) + FooterHeight);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        var cell = (ActualWidth - LabelWidth) / 24;
        var row = (int)Math.Floor((p.Y - HeaderHeight) / 22);
        var hour = (int)Math.Floor((p.X - LabelWidth) / cell);
        var hover = row is >= 0 and < 7 && hour is >= 0 and < 24 ? (row, hour) : (-1, -1);
        if (hover != _hover)
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = (-1, -1);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Data is not { } data)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var label = (Brush)FindResource("TextMutedBrush");
        var empty = (Brush)FindResource("SurfaceElevatedBrush");
        var accent = (Color)FindResource("AccentColor");
        var cell = (ActualWidth - LabelWidth) / 24;
        var max = 0.0;
        foreach (var v in data.Values)
        {
            max = Math.Max(max, v);
        }

        for (var hour = 0; hour < 24; hour += 6)
        {
            drawingContext.DrawText(Text(hour.ToString("00", CultureInfo.InvariantCulture) + ":00", label, dpi), new Point(LabelWidth + (hour * cell), 0));
        }

        for (var row = 0; row < 7; row++)
        {
            var day = (DayOfWeek)(((int)data.FirstDay + row) % 7);
            var y = HeaderHeight + (row * 22);
            drawingContext.DrawText(Text(data.Culture.DateTimeFormat.GetAbbreviatedDayName(day), label, dpi), new Point(0, y + 3));
            for (var hour = 0; hour < 24; hour++)
            {
                var value = data.Values[(int)day, hour];
                var rect = new Rect(LabelWidth + (hour * cell) + 1, y + 1, Math.Max(1, cell - 2), 20);
                Brush fill = value <= 0 || max <= 0
                    ? empty
                    : new SolidColorBrush(Color.FromArgb((byte)(60 + (195 * Math.Sqrt(value / max))), accent.R, accent.G, accent.B));
                drawingContext.DrawRoundedRectangle(fill, (row, hour) == _hover ? new Pen(Brushes.White, 1) : null, rect, 3, 3);
            }
        }

        if (_hover is ( >= 0, >= 0))
        {
            var day = (DayOfWeek)(((int)data.FirstDay + _hover.Row) % 7);
            var text = $"{data.Culture.DateTimeFormat.GetDayName(day)} {_hover.Hour:00}:00–{_hover.Hour + 1:00}:00 · {data.FormatValue(data.Values[(int)day, _hover.Hour])}";
            drawingContext.DrawText(Text(text, (Brush)FindResource("TextPrimaryBrush"), dpi), new Point(LabelWidth, HeaderHeight + (7 * 22) + 6));
        }
    }

    private static FormattedText Text(string text, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Variable Text, Segoe UI"), 11, brush, dpi);
}

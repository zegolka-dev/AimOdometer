using System.Windows;
using System.Windows.Media;

namespace AimOdometer.App.Services;

/// <summary>Categorical chart colors from the palette, in a fixed order.</summary>
public static class SeriesBrushes
{
    private static readonly string[] Keys = ["Series1Color", "Series2Color", "Series3Color", "Series4Color", "Series5Color", "Series6Color"];

    public static Brush For(int index) => Brush(index < Keys.Length ? Keys[index] : "SeriesOtherColor");

    public static Brush Other => Brush("SeriesOtherColor");

    private static SolidColorBrush Brush(string key)
    {
        var brush = new SolidColorBrush((Color)Application.Current.FindResource(key));
        brush.Freeze();
        return brush;
    }
}

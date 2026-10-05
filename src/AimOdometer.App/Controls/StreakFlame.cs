using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AimOdometer.Core.Fun;

namespace AimOdometer.App.Controls;

/// <summary>
/// The streak flame, drawn as vectors so it is sharp at any size: 18 px next to a name on a leaderboard, 40 px and
/// more on the overview. Its look follows <see cref="StreakTiers"/>: the flame grows and changes colour at 20, 50,
/// 100, 200, 300, 400, 500 and 1000 days, gains a hot core, a glow, sparks and a halo, and from 100 days it flickers
/// when shown large (not with Windows animations off). <see cref="IsLit"/> false shows it grey: today does not count yet.
/// </summary>
public sealed class StreakFlame : FrameworkElement
{
    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(
        nameof(Days), typeof(int), typeof(StreakFlame),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((StreakFlame)d).Update()));

    public static readonly DependencyProperty IsLitProperty = DependencyProperty.Register(
        nameof(IsLit), typeof(bool), typeof(StreakFlame),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((StreakFlame)d).Update()));

    // A 24 x 24 box; the flame stands on its bottom centre.
    private static readonly Geometry Outer = Frozen(
        "M12,1.6 C12.9,5.4 17.2,7.6 18.7,11.6 C20.6,16.6 17.6,22.4 12,22.6 C6.4,22.4 3.4,17 5.3,12.4 " +
        "C6.1,10.4 7.5,9.2 8.3,7.4 C8.9,9.3 9.7,10.5 10.8,11.2 C10.5,7.9 11.1,4.6 12,1.6 Z");

    private static readonly Geometry Core = Frozen(
        "M12,10.6 C12.6,13.1 15.5,14.6 15.7,17.4 C15.9,20 14.2,21.7 12,21.7 C9.8,21.7 8.1,20.2 8.3,17.8 " +
        "C8.4,16.1 9.6,15.2 10.3,14 C10.7,15 11.2,15.5 11.8,15.7 C11.5,14 11.6,12.2 12,10.6 Z");

    // Where sparks fly, in box units, and their size.
    private static readonly (double X, double Y, double R)[] SparkSpots =
        [(19.6, 6.2, 1.5), (4.6, 7.6, 1.2), (21.2, 13.4, 1.0), (2.8, 14.6, 0.9), (16.2, 2.4, 0.9), (7.4, 2.8, 0.8)];

    private static readonly Look Grey = new(0.86, ["#9CA3AF", "#6B7280", "#4B5563"], ["#D1D5DB", "#9CA3AF"], null, 0, false);

    private static readonly Look[] Looks =
    [
        new(0.78, ["#FFC24A", "#FF8A1F", "#F2611A"], null, null, 0, false),                                   // spark
        new(0.86, ["#FFD452", "#FF7A1A", "#E8431C"], ["#FFF4C2", "#FFC233"], null, 0, false),                 // flame
        new(0.92, ["#FFE066", "#FF5A1F", "#D61F3C"], ["#FFF8DC", "#FFB020"], "#FF5A1F", 0, false),           // blaze
        new(0.96, ["#F9A8D4", "#A855F7", "#6D28D9"], ["#FFFFFF", "#F0ABFC"], "#A855F7", 2, false),           // neon
        new(1.00, ["#A5F3FC", "#38BDF8", "#2563EB"], ["#FFFFFF", "#BAE6FD"], "#38BDF8", 3, false),           // blue
        new(1.00, ["#E0E7FF", "#22D3EE", "#8B5CF6"], ["#FFFFFF", "#CFFAFE"], "#22D3EE", 4, true),            // plasma
        new(1.00, ["#FEF9C3", "#FACC15", "#EA580C"], ["#FFFFFF", "#FEF08A"], "#FACC15", 4, true),            // gold
        new(1.00, ["#F0ABFC", "#818CF8", "#22D3EE", "#4ADE80"], ["#FFFFFF", "#E0E7FF"], "#A78BFA", 5, true), // aurora
        new(1.00, ["#FDE68A", "#F472B6", "#8B5CF6", "#22D3EE"], ["#FFFFFF", "#FEF3C7"], "#F472B6", 6, true), // legend
    ];

    // Readable text colours for the streak number next to a flame of each tier (4.5:1 on the dark surfaces).
    private static readonly Brush[] TextBrushes = [.. new[]
    {
        "#FFB347", "#FFA04D", "#FF8A5C", "#D8B4FE", "#7DD3FC", "#67E8F9", "#FDE047", "#C4B5FD", "#F9A8D4",
    }.Select(c =>
    {
        var b = new SolidColorBrush(Parse(c));
        b.Freeze();
        return (Brush)b;
    })];

    private readonly ScaleTransform _flicker = new();
    private Look _look = Looks[0];

    public StreakFlame()
    {
        Focusable = false;
        SnapsToDevicePixels = false;
        RenderTransformOrigin = new Point(0.5, 0.95);
        RenderTransform = _flicker;
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => Flicker(false);
        SizeChanged += (_, _) => Update();
    }

    /// <summary>Days in a row; 0 draws nothing.</summary>
    public int Days
    {
        get => (int)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    /// <summary>False: the streak is alive but today has not counted yet (grey flame).</summary>
    public bool IsLit
    {
        get => (bool)GetValue(IsLitProperty);
        set => SetValue(IsLitProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsNaN(Width) ? 24 : Width,
        double.IsNaN(Height) ? 24 : Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Days < 1 || ActualWidth <= 0)
        {
            return;
        }

        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        drawingContext.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        // Sparks and the halo only where there is room for them (not in an 18 px leaderboard icon).
        var roomy = Math.Min(ActualWidth, ActualHeight) >= 28;
        if (roomy && _look.Halo && _look.Glow is { } halo)
        {
            var color = Parse(halo);
            var brush = new RadialGradientBrush(Color.FromArgb(90, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
            brush.Freeze();
            drawingContext.DrawEllipse(brush, null, new Point(12, 14), 12, 12);
        }

        // The flame grows with the tier: scaled around the point it stands on.
        drawingContext.PushTransform(new ScaleTransform(_look.Size, _look.Size, 12, 22.6));
        drawingContext.DrawGeometry(Vertical(_look.Outer), null, Outer);
        if (_look.Core is { } core)
        {
            drawingContext.DrawGeometry(Vertical(core), null, Core);
        }

        drawingContext.Pop();

        if (roomy && IsLit && _look.Glow is { } spark)
        {
            var brush = new SolidColorBrush(Parse(_look.Core?[^1] ?? spark));
            brush.Freeze();
            foreach (var (x, y, r) in SparkSpots.Take(_look.Sparks))
            {
                drawingContext.DrawGeometry(brush, null, Star(x, y, r));
            }
        }
    }

    private void Update()
    {
        var tier = StreakTiers.For(Days);
        _look = tier is null ? Looks[0] : IsLit ? Looks[tier.Index] : Grey;
        var size = Math.Min(ActualWidth, ActualHeight);
        Effect = IsLit && _look.Glow is { } glow && size > 0
            ? new DropShadowEffect { Color = Parse(glow), BlurRadius = Math.Max(6, size * 0.4), ShadowDepth = 0, Opacity = 0.85 }
            : null;
        Flicker(IsLit && tier is { Index: >= 3 } && size >= 32 && SystemParameters.ClientAreaAnimation && IsLoaded);
        InvalidateVisual();
    }

    private void Flicker(bool on)
    {
        if (!on)
        {
            _flicker.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _flicker.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            return;
        }

        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        _flicker.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.06, TimeSpan.FromSeconds(0.9))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        _flicker.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.97, TimeSpan.FromSeconds(1.3))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
    }

    private static LinearGradientBrush Vertical(string[] colors)
    {
        // Top of the flame is the lightest; the base the deepest.
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        for (var i = 0; i < colors.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop(Parse(colors[i]), colors.Length == 1 ? 0 : (double)i / (colors.Length - 1)));
        }

        brush.Freeze();
        return brush;
    }

    private static StreamGeometry Star(double x, double y, double r)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            var k = r * 0.28;
            c.BeginFigure(new Point(x, y - r), true, true);
            c.LineTo(new Point(x + k, y - k), true, false);
            c.LineTo(new Point(x + r, y), true, false);
            c.LineTo(new Point(x + k, y + k), true, false);
            c.LineTo(new Point(x, y + r), true, false);
            c.LineTo(new Point(x - k, y + k), true, false);
            c.LineTo(new Point(x - r, y), true, false);
            c.LineTo(new Point(x - k, y - k), true, false);
        }

        g.Freeze();
        return g;
    }

    /// <summary>The colour of the streak number next to the flame.</summary>
    public static Brush TextBrush(int days) => StreakTiers.For(days) is { } tier ? TextBrushes[tier.Index] : Brushes.Gray;

    /// <summary>The tier's name ("Neon", "Legend"...) in the current language.</summary>
    public static string TierName(int days) => StreakTiers.For(days) is { } tier ? Localization.Loc.Instance[$"Streak.Tier.{tier.Id}"] : string.Empty;

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Geometry Frozen(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    /// <summary>How one tier is drawn: flame size (of the box), outer and core gradients, glow colour, sparks, halo.</summary>
    private sealed record Look(double Size, string[] Outer, string[]? Core, string? Glow, int Sparks, bool Halo);
}

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AimOdometer.App.Controls;

/// <summary>How an element animates each time it becomes visible.</summary>
public enum AppearMotion
{
    None,

    /// <summary>Opacity only (backdrops).</summary>
    Fade,

    /// <summary>Fade plus a slight zoom from 96 % (dialogs).</summary>
    Pop,
}

/// <summary>
/// Attached entrance animations: <c>c:Motion.Appear="Pop"</c> replays every time the element turns visible.
/// Skipped when Windows animations are off (Settings › Accessibility › Visual effects).
/// </summary>
public static partial class Motion
{
    /// <summary>
    /// Text for a TextBlock whose first number rolls from its previous value to the new one, like an odometer
    /// ("241 m" counts up from 0 on first show, then only the difference rolls on later updates).
    /// </summary>
    public static readonly DependencyProperty CountUpProperty = DependencyProperty.RegisterAttached(
        "CountUp", typeof(string), typeof(Motion), new PropertyMetadata(null, OnCountUpChanged));

    private static readonly DependencyProperty ShownValueProperty = DependencyProperty.RegisterAttached(
        "ShownValue", typeof(double), typeof(Motion), new PropertyMetadata(0.0));

    private static readonly TimeSpan CountDuration = TimeSpan.FromMilliseconds(700);

    public static string? GetCountUp(DependencyObject element) => (string?)element.GetValue(CountUpProperty);

    public static void SetCountUp(DependencyObject element, string? value) => element.SetValue(CountUpProperty, value);

    [GeneratedRegex(@"\d(?:[\d\u00A0\u202F ,.]*\d)?")]
    private static partial Regex NumberPattern();

    private static void OnCountUpChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        var text = e.NewValue as string ?? string.Empty;
        var culture = Localization.Loc.Instance.Culture;
        var match = NumberPattern().Match(text);
        if (!SystemParameters.ClientAreaAnimation || !match.Success || !TryParse(match.Value, culture, out var target, out var decimals))
        {
            block.Text = text;
            return;
        }

        var from = (double)block.GetValue(ShownValueProperty);
        block.SetValue(ShownValueProperty, target);
        if (from == target)
        {
            block.Text = text;
            return;
        }

        var clock = Stopwatch.StartNew();
        void Tick(object? sender, EventArgs args)
        {
            if (!ReferenceEquals(GetCountUp(block), text) || clock.Elapsed >= CountDuration)
            {
                CompositionTarget.Rendering -= Tick;
                if (ReferenceEquals(GetCountUp(block), text))
                {
                    block.Text = text; // the exact formatted string, whatever the culture's grouping
                }

                return;
            }

            var t = clock.Elapsed / CountDuration;
            var eased = 1 - Math.Pow(1 - t, 3);
            var value = from + ((target - from) * eased);
            block.Text = string.Concat(text.AsSpan(0, match.Index), value.ToString("N" + decimals, culture), text.AsSpan(match.Index + match.Length));
        }

        CompositionTarget.Rendering += Tick;
    }

    private static bool TryParse(string number, CultureInfo culture, out double value, out int decimals)
    {
        var separator = culture.NumberFormat.NumberDecimalSeparator;
        var clean = number.Replace("\u00A0", "", StringComparison.Ordinal).Replace("\u202F", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal).Replace(culture.NumberFormat.NumberGroupSeparator, "", StringComparison.Ordinal);
        var dot = clean.LastIndexOf(separator, StringComparison.Ordinal);
        decimals = dot < 0 ? 0 : clean.Length - dot - separator.Length;
        return double.TryParse(clean, NumberStyles.Number, culture, out value);
    }

    public static readonly DependencyProperty AppearProperty = DependencyProperty.RegisterAttached(
        "Appear", typeof(AppearMotion), typeof(Motion), new PropertyMetadata(AppearMotion.None, OnAppearChanged));

    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };
    private static readonly BackEase Settle = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };

    public static AppearMotion GetAppear(DependencyObject element) => (AppearMotion)element.GetValue(AppearProperty);

    public static void SetAppear(DependencyObject element, AppearMotion value) => element.SetValue(AppearProperty, value);

    private static void OnAppearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnIsVisibleChanged;
        if ((AppearMotion)e.NewValue != AppearMotion.None)
        {
            element.IsVisibleChanged += OnIsVisibleChanged;
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not UIElement element || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut });
        if (GetAppear(element) != AppearMotion.Pop)
        {
            return;
        }

        if (element.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
        {
            scale = new ScaleTransform();
            element.RenderTransform = scale;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var zoom = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Settle };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
    }
}

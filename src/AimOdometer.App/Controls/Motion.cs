using System.Windows;
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
public static class Motion
{
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

using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AimOdometer.App.Services;
using AimOdometer.App.ViewModels;
using AimOdometer.Win32;

namespace AimOdometer.App.Views;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The listener is disposed in OnClosed.")]
public partial class MainWindow : Window
{
    private RawMouseListener? _rawMouse;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old)
            {
                old.PropertyChanged -= OnViewModelChanged;
            }

            if (e.NewValue is MainViewModel vm)
            {
                vm.PropertyChanged += OnViewModelChanged;
            }
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Dwmapi.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
        UpdateRawMouse();
    }

    protected override void OnClosed(EventArgs e)
    {
        _rawMouse?.Dispose();
        base.OnClosed(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasOverlay))
        {
            UpdateRawMouse();
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentPage))
        {
            AnimatePageIn();
        }
    }

    /// <summary>A short fade and slide when switching pages; skipped when Windows animations are turned off.</summary>
    private void AnimatePageIn()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        ((TranslateTransform)PageHost.RenderTransform).BeginAnimation(
            TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = ease });
    }

    /// <summary>Raw mouse input is read only while onboarding or calibration is open (it costs CPU at high polling rates).</summary>
    private void UpdateRawMouse()
    {
        if (!IsLoaded && PresentationSource.FromVisual(this) is null)
        {
            return;
        }

        if (ViewModel is { HasOverlay: true } vm && _rawMouse is null)
        {
            _rawMouse = new RawMouseListener(this);
            _rawMouse.Report += vm.OnRawReport;
        }
        else if (ViewModel is not { HasOverlay: true } && _rawMouse is not null)
        {
            _rawMouse.Dispose();
            _rawMouse = null;
        }
    }
}

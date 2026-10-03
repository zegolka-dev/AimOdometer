using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AimOdometer.App.Localization;
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
        ApplyLanguage();
        Loc.Instance.LanguageChanged += OnLanguageChanged;
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
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        _rawMouse?.Dispose();
        base.OnClosed(e);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    // Built-in controls (DatePicker, number formatting in bindings) read the inherited Language, which is en-US by default.
    private void ApplyLanguage() => Language = XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsCalibrationVisible))
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

    /// <summary>Raw mouse input is read only while the DPI calibration is open (it costs CPU at high polling rates).</summary>
    private void UpdateRawMouse()
    {
        // The native handle exists from SourceInitialized on; PresentationSource may not be attached yet at that point.
        if (new WindowInteropHelper(this).Handle == 0)
        {
            return;
        }

        if (ViewModel is { IsCalibrationVisible: true } vm && _rawMouse is null)
        {
            _rawMouse = new RawMouseListener(this);
            _rawMouse.Report += vm.OnRawReport;
        }
        else if (ViewModel is not { IsCalibrationVisible: true } && _rawMouse is not null)
        {
            _rawMouse.Dispose();
            _rawMouse = null;
        }
    }
}

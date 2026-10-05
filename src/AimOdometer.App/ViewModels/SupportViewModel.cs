using System.Diagnostics;
using System.Windows.Media;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

/// <summary>One payment service in the support window; clicking it opens the author's page in the browser.</summary>
public sealed partial class SupportOptionViewModel(SupportService service) : ObservableObject
{
    public string Id => service.Id;

    public string Name => service.Name;

    public string Monogram => service.Monogram;

    public Brush Color { get; } = Frozen(service.Color);

    public string Note => Loc.Instance[$"Support.{service.Id}.Note"];

    [RelayCommand]
    private void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo(service.Url) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warning($"Could not open the browser: {ex.Message}");
        }
    }

    private static SolidColorBrush Frozen(string color)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

/// <summary>"Support the author": the payment services, grouped by where the money comes from.</summary>
public sealed partial class SupportViewModel(Action close) : ObservableObject
{
    public IReadOnlyList<SupportOptionViewModel> Russia { get; } =
        [.. SupportLinks.Available.Where(s => s.Region == SupportRegion.Russia).Select(s => new SupportOptionViewModel(s))];

    public IReadOnlyList<SupportOptionViewModel> World { get; } =
        [.. SupportLinks.Available.Where(s => s.Region == SupportRegion.World).Select(s => new SupportOptionViewModel(s))];

    public bool HasRussia => Russia.Count > 0;

    public bool HasWorld => World.Count > 0;

    [RelayCommand]
    private void Close() => close();
}

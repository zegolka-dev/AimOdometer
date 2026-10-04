using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Fun;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

/// <summary>Line icons for gear kinds on a 24 × 24 grid, drawn with a thin round stroke (Segoe Fluent has no sleeve or glides).</summary>
public static class GearIcons
{
    public static Geometry MousePad { get; } = Parse(
        "M5,6 H19 A2,2 0 0 1 21,8 V16 A2,2 0 0 1 19,18 H5 A2,2 0 0 1 3,16 V8 A2,2 0 0 1 5,6 Z " +
        "M15.5,9 A2,2 0 0 1 17.5,11 V13 A2,2 0 0 1 13.5,13 V11 A2,2 0 0 1 15.5,9 Z M15.5,9 V11");

    /// <summary>A forearm sleeve with elastic cuffs, reaching for a mouse.</summary>
    public static Geometry Sleeve { get; } = Parse(
        "M8.95,21.09 Q10.99,15.24 14.84,12.23 L12.16,7.77 Q6.01,10.76 2.05,17.91 Z M9.66,19.27 L3.36,15.75 M13.64,13.27 L10.26,8.83 " +
        "M18.5,2.5 A2.7,2.7 0 0 1 21.2,5.2 V7.3 A2.7,2.7 0 0 1 15.8,7.3 V5.2 A2.7,2.7 0 0 1 18.5,2.5 Z M18.5,2.5 V5");

    /// <summary>The underside of a mouse: two skates and the sensor.</summary>
    public static Geometry Glides { get; } = Parse(
        MouseBody + " M9.5,5.5 H14.5 A1,1 0 0 1 14.5,7.5 H9.5 A1,1 0 0 1 9.5,5.5 Z M9,17 H15 A1,1 0 0 1 15,19 H9 A1,1 0 0 1 9,17 Z " +
        "M13.5,12 A1.5,1.5 0 1 1 10.5,12 A1.5,1.5 0 1 1 13.5,12 Z");

    public static Geometry Mouse { get; } = Parse(
        MouseBody + " M12,3 V5.5 M12,8.5 V10.5 M6,10.5 H18 M11,6.5 A1,1 0 0 1 13,6.5 V7.5 A1,1 0 0 1 11,7.5 Z");

    private const string MouseBody = "M12,3 C16,3 18,6 18,10 V15 C18,19 15.5,21 12,21 C8.5,21 6,19 6,15 V10 C6,6 8,3 12,3 Z";

    private static Geometry Parse(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>A kind of gear in the "add" form.</summary>
public sealed record GearKindOption(GearKind Kind)
{
    public string Name => Loc.Instance[$"Gear.Kind.{Kind}"];
}

/// <summary>One pad, mouse or set of glides with its wear.</summary>
public sealed partial class GearItemRowViewModel(GearItemsViewModel owner, GearStatus status, string? deviceName)
{
    public GearStatus Status { get; } = status;

    public string Name => Status.Item.Name;

    public string KindText => Loc.Instance[$"Gear.Kind.{Status.Item.Kind}"];

    public string Details => deviceName is null
        ? Loc.Instance.Format("Gear.Since", Format.Date(Status.Item.StartedOn))
        : Loc.Instance.Format("Gear.SinceWithMouse", Format.Date(Status.Item.StartedOn), deviceName);

    public string UsedText => Loc.Instance.Format("Gear.Used",
        Format.Distance(Status.KilometersUsed * 100_000), Format.Distance(Status.Item.LifetimeKm * 100_000), Format.Percent(Status.Fraction));

    public double Fraction => Math.Min(1, Status.Fraction);

    public bool TimeToReplace => Status.TimeToReplace;

    public bool IsRetired => Status.Item.RetiredOn is not null;

    public string? RetiredText => Status.Item.RetiredOn is { } date ? Loc.Instance.Format("Gear.RetiredOn", Format.Date(date)) : null;

    public Geometry Icon => Status.Item.Kind switch
    {
        GearKind.MousePad => GearIcons.MousePad,
        GearKind.Mouse => GearIcons.Mouse,
        GearKind.Sleeve => GearIcons.Sleeve,
        _ => GearIcons.Glides,
    };

    [RelayCommand]
    private void Retire() => owner.Retire(this);

    [RelayCommand]
    private void Delete() => owner.Delete(this);
}

/// <summary>Mouse pads, mice and glides: wear by distance since they were put into use.</summary>
public sealed partial class GearItemsViewModel(AppData data) : ObservableObject
{
    public ObservableCollection<GearItemRowViewModel> Items { get; } = [];

    public IReadOnlyList<GearKindOption> Kinds { get; } =
        [new(GearKind.MousePad), new(GearKind.Sleeve), new(GearKind.Glides), new(GearKind.Mouse)];

    public ObservableCollection<DeviceRecord> Mice { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsMouse))]
    public partial GearKindOption? NewKind { get; set; }

    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime? NewStarted { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string NewLifetime { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DeviceRecord? NewMouse { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool HasItems { get; set; }

    public bool NeedsMouse => NewKind is { } kind && !Core.Fun.GearWear.UsesAllMice(kind.Kind);

    partial void OnNewKindChanged(GearKindOption? value)
    {
        if (value is not null)
        {
            NewLifetime = GearWear.DefaultLifetimeKm(value.Kind).ToString("0", CultureInfo.InvariantCulture);
        }
    }

    public void Refresh()
    {
        var devices = data.Store.GetDevices().Where(d => d.Kind != Core.Input.DeviceKind.Software).ToList();
        Mice.Clear();
        foreach (var device in devices)
        {
            Mice.Add(device);
        }

        NewKind ??= Kinds[0];
        NewMouse ??= Mice.FirstOrDefault();

        Items.Clear();
        foreach (var item in data.Store.GetGear())
        {
            var device = item.DeviceId is { } id ? devices.FirstOrDefault(d => d.Id == id)?.Name : null;
            Items.Add(new GearItemRowViewModel(this, GearWear.Status(data.Store, item), device));
        }

        HasItems = Items.Count > 0;
    }

    [RelayCommand]
    private void Add()
    {
        if (NewKind is null || string.IsNullOrWhiteSpace(NewName))
        {
            Error = Loc.Instance["Gear.NameRequired"];
            return;
        }

        if (!double.TryParse(NewLifetime.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var lifetime) || lifetime <= 0)
        {
            Error = Loc.Instance["Gear.LifetimeInvalid"];
            return;
        }

        if (NeedsMouse && NewMouse is null)
        {
            Error = Loc.Instance["Gear.MouseRequired"];
            return;
        }

        Error = null;
        var started = DateOnly.FromDateTime(NewStarted ?? DateTime.Today);
        data.Store.SaveGear(new GearItem(0, NewKind.Kind, NewName.Trim(), NeedsMouse ? NewMouse!.Id : null, started, lifetime, null));
        NewName = string.Empty;
        Refresh();
    }

    internal void Retire(GearItemRowViewModel row)
    {
        data.Store.SaveGear(row.Status.Item with { RetiredOn = DateOnly.FromDateTime(DateTime.Today) });
        Refresh();
    }

    internal void Delete(GearItemRowViewModel row)
    {
        data.Store.DeleteGear(row.Status.Item.Id);
        Refresh();
    }
}

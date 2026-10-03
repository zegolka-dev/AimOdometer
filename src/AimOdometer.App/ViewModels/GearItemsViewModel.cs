using System.Collections.ObjectModel;
using System.Globalization;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Fun;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

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

    public string Glyph => Status.Item.Kind switch
    {
        GearKind.MousePad => "",
        GearKind.Mouse => "",
        GearKind.Sleeve => "",
        _ => "",
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

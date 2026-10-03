using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Map;
using AimOdometer.Core.Stats;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

public enum MapPeriod
{
    Today,
    Week,
    Month,
    AllTime,
}

/// <summary>A period segment on the map page.</summary>
public sealed record MapPeriodOption(MapPeriod Period)
{
    public string Name => Loc.Instance[$"Map.Period.{Period}"];

    public override string ToString() => Name;
}

/// <summary>A search hit the user can add to the journey.</summary>
public sealed record PlaceResult(Place Place)
{
    public string Name => Place.Name;

    public string Details => Place.DisplayName;
}

/// <summary>
/// "Where would you get": the mouse distance of a period as a walk along real roads from the user's city through the
/// cities they picked. Nothing here touches the network until the user allows it on this page.
/// </summary>
public sealed partial class MapViewModel(AppData data) : PageViewModel(data)
{
    private const double RecheckSeconds = 60;

    private MapClient? _client;
    private List<RouteLeg> _legs = [];
    private PeriodTotals? _periods;
    private int _generation;
    private DateTime _lastProgress;

    public override string TitleKey => "Nav.Map";

    public override string Icon => "";

    public override bool FillsViewport => true;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SearchMessage { get; set; }

    public ObservableCollection<PlaceResult> SearchResults { get; } = [];

    public ObservableCollection<Place> Places { get; } = [];

    [ObservableProperty]
    public partial bool HasPlaces { get; set; }

    public IReadOnlyList<MapPeriodOption> Periods { get; } =
        [new(MapPeriod.Today), new(MapPeriod.Week), new(MapPeriod.Month), new(MapPeriod.AllTime)];

    [ObservableProperty]
    public partial MapPeriod Period { get; set; } = MapPeriod.Month;

    [ObservableProperty]
    public partial string CaptionLead { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CaptionRoute { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? CaptionDetails { get; set; }

    [ObservableProperty]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Set when the map itself cannot be shown (no WebView2 runtime, no WebGL).</summary>
    [ObservableProperty]
    public partial string? MapError { get; set; }

    /// <summary>Latest "route" message for the map page (JSON).</summary>
    [ObservableProperty]
    public partial string? RouteMessage { get; set; }

    /// <summary>Latest "progress" message for the map page (JSON).</summary>
    [ObservableProperty]
    public partial string? ProgressMessage { get; set; }

    private static Loc L => Loc.Instance;

    public override void Refresh()
    {
        IsEnabled = Data.Setting(SettingKeys.MapEnabled) == "1";
        if (Enum.TryParse<MapPeriod>(Data.Setting(SettingKeys.MapPeriod), out var period))
        {
            Period = period;
        }

        Places.Clear();
        foreach (var place in PlaceList.Read(Data.Setting(SettingKeys.MapPlaces)))
        {
            Places.Add(place);
        }

        HasPlaces = Places.Count > 0;
        UpdateSearchHint();
        LoadPeriods();
        if (IsEnabled)
        {
            Forget(RebuildAsync());
        }
        else
        {
            ShowCaption();
        }
    }

    public override void OnLiveUpdate(TrackerConnection tracker)
    {
        // The walked distance grows while the tab is open; once a minute is plenty for a map.
        if (IsEnabled && (DateTime.UtcNow - _lastProgress).TotalSeconds >= RecheckSeconds)
        {
            LoadPeriods();
            Forget(UpdateProgressAsync(_generation));
        }
    }

    /// <summary>The first message for the map page: style, attribution and palette.</summary>
    public string BuildInitMessage(IReadOnlyDictionary<string, string> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var services = _client?.Services ?? ServicesConfig.BuiltIn;
        return Json(w =>
        {
            w.WriteString("type", "init");
            w.WriteString("style", services.MapStyle.ToString());
            w.WriteString("attribution", services.Attribution);
            w.WriteStartObject("colors");
            foreach (var (name, value) in colors)
            {
                w.WriteString(name, value);
            }

            w.WriteEndObject();
        });
    }

    [RelayCommand]
    private void Enable()
    {
        // Map settings are the window's own: the tracker does not need a reload for them.
        Data.Store.SetSetting(SettingKeys.MapEnabled, "1");
        IsEnabled = true;
        Forget(RebuildAsync());
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var text = Query.Trim();
        if (text.Length == 0 || IsBusy)
        {
            return;
        }

        SearchResults.Clear();
        SearchMessage = L["Map.Searching"];
        IsBusy = true;
        try
        {
            var places = await Client.SearchAsync(text, L.Code, CancellationToken.None);
            foreach (var place in places)
            {
                SearchResults.Add(new PlaceResult(place));
            }

            SearchMessage = places.Count == 0 ? L["Map.NoResults"] : null;
        }
        catch (MapServiceException ex)
        {
            Log.Warning($"Map search failed: {ex.Message}");
            SearchMessage = L["Map.SearchFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddPlaceAsync(PlaceResult? result)
    {
        if (result is null)
        {
            return;
        }

        Places.Add(result.Place);
        SearchResults.Clear();
        SearchMessage = null;
        Query = string.Empty;
        await PlacesChangedAsync();
    }

    [RelayCommand]
    private async Task RemoveLastAsync()
    {
        if (Places.Count > 0)
        {
            Places.RemoveAt(Places.Count - 1);
            await PlacesChangedAsync();
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        Places.Clear();
        await PlacesChangedAsync();
    }

    partial void OnPeriodChanged(MapPeriod value)
    {
        Data.Store.SetSetting(SettingKeys.MapPeriod, value.ToString());
        Forget(UpdateProgressAsync(_generation));
    }

    private MapClient Client => _client ??= new MapClient(Data.Store);

    private async Task PlacesChangedAsync()
    {
        Data.Store.SetSetting(SettingKeys.MapPlaces, PlaceList.Write(Places));
        HasPlaces = Places.Count > 0;
        UpdateSearchHint();
        await RebuildAsync();
    }

    private void UpdateSearchHint() => SearchHint = Places.Count switch
    {
        0 => L["Map.SearchStart"],
        1 => L["Map.SearchNext"],
        _ => L["Map.SearchMore"],
    };

    private void LoadPeriods()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        _periods = StatsSummary.Periods(Data.Store.GetDailyTotals(), today, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
    }

    private double MetersFor(MapPeriod period) => _periods is not { } p ? 0 : period switch
    {
        MapPeriod.Today => p.Today.Centimeters,
        MapPeriod.Week => p.Week.Centimeters,
        MapPeriod.Month => p.Month.Centimeters,
        _ => p.AllTime.Centimeters,
    } / 100;

    /// <summary>Routes between consecutive places (cached in the database), then the map and the caption.</summary>
    private async Task RebuildAsync()
    {
        var generation = ++_generation;
        IsBusy = true;
        try
        {
            await Client.LoadServicesAsync(CancellationToken.None);
            var places = Places.ToList();
            var legs = new List<RouteLeg>();
            for (var i = 1; i < places.Count; i++)
            {
                legs.Add(await Client.RouteAsync(places[i - 1], places[i], CancellationToken.None));
                if (generation != _generation)
                {
                    return; // the places changed meanwhile; a newer rebuild is running
                }
            }

            _legs = legs;
            RouteMessage = BuildRouteMessage(places, legs);
            await UpdateProgressAsync(generation);
        }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false;
            }
        }
    }

    private async Task UpdateProgressAsync(int generation)
    {
        _lastProgress = DateTime.UtcNow;
        var legs = _legs;
        ShowCaption();
        if (legs.Count == 0)
        {
            ProgressMessage = Json(w =>
            {
                w.WriteString("type", "progress");
                w.WriteStartArray("walked");
                w.WriteEndArray();
                w.WriteStartArray("markers");
                w.WriteEndArray();
            });
            return;
        }

        ProgressMessage = BuildProgressMessage(legs);
        var position = RoutePlan.Locate(legs, MetersFor(Period));
        if (position is null || position.Finished || !IsEnabled)
        {
            return;
        }

        // Name the place the walk reached: one cached lookup per ~1 km cell.
        try
        {
            var name = await Client.PlaceNameAtAsync(position.Point, L.Code, CancellationToken.None);
            if (generation == _generation && name is not null && position == RoutePlan.Locate(_legs, MetersFor(Period)))
            {
                if (string.Equals(name, Places[0].Name, StringComparison.OrdinalIgnoreCase))
                {
                    CaptionLead = L["Map.StillInStart"];
                    CaptionRoute = Places[0].Name;
                }
                else
                {
                    CaptionRoute = $"{Places[0].Name} → {name}";
                }
            }
        }
        catch (MapServiceException ex)
        {
            Log.Info($"Reverse geocoding failed: {ex.Message}");
        }
    }

    private void ShowCaption()
    {
        Notice = _legs.Any(l => l.IsStraight) ? L["Map.Straight"] : null;
        CaptionLead = L[$"Map.Lead.{Period}"];
        if (Places.Count == 0)
        {
            CaptionLead = L["Map.ChooseStart"];
            CaptionRoute = string.Empty;
            CaptionDetails = null;
            return;
        }

        if (Places.Count == 1 || _legs.Count == 0)
        {
            CaptionLead = L["Map.ChooseDestination"];
            CaptionRoute = Places[0].Name;
            CaptionDetails = null;
            return;
        }

        var meters = MetersFor(Period);
        var total = RoutePlan.TotalMeters(_legs);
        var position = RoutePlan.Locate(_legs, meters)!;
        if (position.Finished)
        {
            CaptionRoute = $"{Places[0].Name} → {Places[^1].Name}";
            CaptionDetails = L.Format("Map.Reached", Format.Distance(position.ExtraMeters * 100));
        }
        else
        {
            // Until the reverse lookup answers: the leg being walked.
            CaptionRoute = $"{Places[0].Name} → … → {_legs[position.LegIndex].To.Name}";
            var percent = (meters / total).ToString("P0", L.Culture);
            CaptionDetails = L.Format("Map.Progress", Format.Distance(meters * 100), Format.Distance(total * 100), Format.Distance((total - meters) * 100), percent);
        }
    }

    private static string BuildRouteMessage(IReadOnlyList<Place> places, IReadOnlyList<RouteLeg> legs) => Json(w =>
    {
        w.WriteString("type", "route");
        w.WriteStartArray("legs");
        foreach (var leg in legs)
        {
            w.WriteStartObject();
            w.WriteBoolean("straight", leg.IsStraight);
            WriteLine(w, "line", leg.Line);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("places");
        foreach (var place in places)
        {
            w.WriteStartObject();
            w.WriteString("name", place.Name);
            w.WriteNumber("lat", place.Point.Lat);
            w.WriteNumber("lon", place.Point.Lon);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });

    private string BuildProgressMessage(IReadOnlyList<RouteLeg> legs) => Json(w =>
    {
        w.WriteString("type", "progress");
        WriteLine(w, "walked", RoutePlan.Walked(legs, MetersFor(Period)));
        w.WriteStartArray("markers");
        foreach (var option in Periods)
        {
            var meters = MetersFor(option.Period);
            if (RoutePlan.Locate(legs, meters) is not { } position)
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteString("label", $"{option.Name} · {Format.Distance(meters * 100)}");
            w.WriteNumber("lat", position.Point.Lat);
            w.WriteNumber("lon", position.Point.Lon);
            w.WriteBoolean("selected", option.Period == Period);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });

    // Background updates must never surface as an unhandled-exception dialog; the log has the details.
    private static async void Forget(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Map update failed", ex);
        }
    }

    private static void WriteLine(Utf8JsonWriter w, string name, IReadOnlyList<GeoPoint> line)
    {
        w.WriteStartArray(name);
        foreach (var p in line)
        {
            w.WriteStartArray();
            w.WriteNumberValue(Math.Round(p.Lon, 6));
            w.WriteNumberValue(Math.Round(p.Lat, 6));
            w.WriteEndArray();
        }

        w.WriteEndArray();
    }

    private static string Json(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

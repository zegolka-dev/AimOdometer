using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AimOdometer.App.Localization;
using AimOdometer.App.ViewModels;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AimOdometer.App.Views;

/// <summary>
/// Hosts the map. The WebView2 (and its msedgewebview2 processes) exists only while this page is on screen and the
/// user has allowed the map: it is created on Loaded and disposed on Unloaded.
/// </summary>
public partial class MapView : UserControl
{
    /// <summary>Virtual host for the bundled map page (the reserved .example TLD can never be a real site).</summary>
    public const string Host = "map.aimodometer.example";

    private WebView2? _web;
    private MapViewModel? _viewModel;
    private bool _isLoaded;
    private bool _pageReady;

    public MapView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _isLoaded = true;
            Attach(DataContext as MapViewModel);
        };
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            Attach(null);
            CloseMap();
        };
        DataContextChanged += (_, e) =>
        {
            if (_isLoaded)
            {
                Attach(e.NewValue as MapViewModel);
            }
        };
    }

    /// <summary>Folder with the bundled page and MapLibre, next to the exe.</summary>
    public static string ContentFolder => Path.Combine(AppContext.BaseDirectory, "map");

    /// <summary>WebView2's profile (cache, cookies) lives with the user's data, not next to the exe.</summary>
    public static string ProfileFolder => Path.Combine(AppIdentity.DataDirectory, "WebView2");

    private void Attach(MapViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            OpenMapIfAllowed();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MapViewModel.IsEnabled):
                OpenMapIfAllowed();
                break;
            case nameof(MapViewModel.RouteMessage):
                Post(_viewModel?.RouteMessage);
                break;
            case nameof(MapViewModel.ProgressMessage):
                Post(_viewModel?.ProgressMessage);
                break;
        }
    }

    private async void OpenMapIfAllowed()
    {
        if (_web is not null || _viewModel is not { IsEnabled: true } viewModel)
        {
            return;
        }

        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            viewModel.MapError = Loc.Instance["Map.NoRuntime"];
            return;
        }

        var web = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.ColorTranslator.FromHtml(Hex("BackgroundBrush")),
            Focusable = true,
        };
        _web = web;
        _pageReady = false;
        MapHost.Child = web;
        try
        {
            // Tiles are cached by the browser; keep that cache small (64 MB).
            var options = new CoreWebView2EnvironmentOptions("--disk-cache-size=67108864");
            var environment = await CoreWebView2Environment.CreateAsync(null, ProfileFolder, options);
            await web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or ObjectDisposedException)
        {
            Log.Error("WebView2 could not start", ex);
            if (_web == web)
            {
                viewModel.MapError = Loc.Instance["Map.NoRuntime"];
                CloseMap();
            }

            return;
        }

        if (_web != web)
        {
            return; // the page was left while WebView2 was starting
        }

        var core = web.CoreWebView2;
        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = Debugger.IsAttached;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        core.SetVirtualHostNameToFolderMapping(Host, ContentFolder, CoreWebView2HostResourceAccessKind.Deny);
        core.NavigationStarting += (_, e) =>
        {
            // Only the bundled page; attribution links open in the user's browser instead.
            if (!e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                OpenExternal(e.Uri);
            }
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        core.WebMessageReceived += OnWebMessage;
        core.ProcessFailed += (_, e) => Log.Warning($"Map browser process failed: {e.ProcessFailedKind}");
        core.Navigate($"https://{Host}/index.html");
    }

    private void CloseMap()
    {
        if (_web is null)
        {
            return;
        }

        var web = _web;
        _web = null;
        _pageReady = false;
        MapHost.Child = null;
        web.Dispose(); // ends the msedgewebview2 processes of this profile
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        switch (root.GetProperty("type").GetString())
        {
            case "ready":
                _pageReady = true;
                viewModel.MapError = null;
                Post(viewModel.BuildInitMessage(Palette()));
                Post(viewModel.RouteMessage);
                Post(viewModel.ProgressMessage);
                break;
            case "error":
                var code = root.GetProperty("code").GetString();
                Log.Warning($"Map page error: {code} {(root.TryGetProperty("message", out var m) ? m.GetString() : null)}");
                if (code == "webgl")
                {
                    viewModel.MapError = Loc.Instance["Map.NoWebGL"];
                }
                else
                {
                    viewModel.Notice = Loc.Instance["Map.TilesFailed"];
                }

                break;
        }
    }

    private void Post(string? json)
    {
        if (json is not null && _pageReady && _web?.CoreWebView2 is { } core)
        {
            core.PostWebMessageAsJson(json);
        }
    }

    private Dictionary<string, string> Palette() => new()
    {
        ["bg"] = Hex("BackgroundBrush"),
        ["surface"] = Hex("SurfaceBrush"),
        ["border"] = Hex("BorderStrongBrush"),
        ["text"] = Hex("TextPrimaryBrush"),
        ["muted"] = Hex("TextMutedBrush"),
        ["accent"] = Hex("AccentBrush"),
        ["accentText"] = Hex("AccentTextBrush"),
        ["accent2"] = Hex("Accent2Brush"),
    };

    private string Hex(string brushKey)
    {
        var color = ((SolidColorBrush)FindResource(brushKey)).Color;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }
}

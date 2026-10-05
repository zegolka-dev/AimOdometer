namespace AimOdometer.App.Services;

/// <summary>A way to send the author money. <see cref="Url"/> is the author's public page on that service.</summary>
public sealed record SupportService(string Id, string Name, string Url, string Monogram, string Color);

/// <summary>
/// The "Support the author" services, in the order shown: Boosty takes Russian and foreign cards, PayPal is for
/// people abroad. A service without an https link is not shown; with none at all, the sidebar
/// link is hidden. The links are public pages, not secrets.
/// </summary>
public static class SupportLinks
{
    public static IReadOnlyList<SupportService> All { get; } =
    [
        new("boosty", "Boosty", "https://boosty.to/zegolka", "B", "#F15F2C"),
        new("paypal", "PayPal", "https://paypal.me/zegolka", "P", "#0070E0"),
    ];

    public static IReadOnlyList<SupportService> Available => [.. All.Where(s => IsLink(s.Url))];

    public static bool IsLink(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

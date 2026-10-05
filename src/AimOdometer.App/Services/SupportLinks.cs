namespace AimOdometer.App.Services;

/// <summary>Who a payment service suits best: people paying with Russian cards, or everyone else.</summary>
public enum SupportRegion
{
    Russia,
    World,
}

/// <summary>A way to send the author money. <see cref="Url"/> is the author's public page on that service.</summary>
public sealed record SupportService(string Id, string Name, string Url, SupportRegion Region, string Monogram, string Color);

/// <summary>
/// The "Support the author" services. A service without an https link is not shown; with none at all, the sidebar
/// link is hidden. The links are public pages, not secrets.
/// </summary>
public static class SupportLinks
{
    public static IReadOnlyList<SupportService> All { get; } =
    [
        new("boosty", "Boosty", "", SupportRegion.Russia, "B", "#F15F2C"),
        new("donationalerts", "DonationAlerts", "", SupportRegion.Russia, "DA", "#F59E0B"),
        new("paypal", "PayPal", "", SupportRegion.World, "P", "#0070E0"),
    ];

    public static IReadOnlyList<SupportService> Available => [.. All.Where(s => IsLink(s.Url))];

    public static bool IsLink(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

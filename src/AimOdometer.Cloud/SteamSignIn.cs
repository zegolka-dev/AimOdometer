using System.Security.Cryptography;
using System.Text;

namespace AimOdometer.Cloud;

/// <summary>Pages shown in the browser tab after signing in (localized by the window).</summary>
public sealed record SignInPages(string SuccessTitle, string SuccessText, string FailureTitle, string FailureText);

/// <summary>
/// Sign in with Steam from a native app, the RFC 8252 way: the system browser (where the user is already signed in to
/// Steam, and where AimOdometer never sees the password) and a loopback redirect to this PC, protected by a PKCE-style
/// verifier and a state value.
/// </summary>
public static class SteamSignIn
{
    /// <summary>How long the browser part may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public static async Task<CloudSession> SignInAsync(
        CloudClient client, Func<Uri, bool> openBrowser, SignInPages pages, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(openBrowser);
        ArgumentNullException.ThrowIfNull(pages);

        var verifier = RandomToken(32);
        var challenge = Challenge(verifier);
        var state = RandomToken(24);
        using var loopback = new LoopbackCallback();
        var start = StartUrl(challenge, loopback.Port, state);
        if (!openBrowser(start))
        {
            throw new CloudException(CloudError.BrowserFailed, "The browser could not be opened.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(Timeout);
        IReadOnlyDictionary<string, string> query;
        try
        {
            query = await loopback.WaitAsync("/callback", q => Page(IsSuccess(q, state) ? (pages.SuccessTitle, pages.SuccessText) : (pages.FailureTitle, pages.FailureText)), timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new CloudException(CloudError.SignInTimedOut, "Sign-in was not finished in time.");
        }

        if (!query.TryGetValue("state", out var returnedState) || !FixedEquals(returnedState, state))
        {
            throw new CloudException(CloudError.SignInDenied, "The sign-in answer does not belong to this attempt.");
        }

        if (query.TryGetValue("error", out var error))
        {
            throw new CloudException(error == "cancelled" ? CloudError.SignInCancelled : CloudError.SignInDenied, $"Steam sign-in: {error}");
        }

        if (!query.TryGetValue("code", out var code))
        {
            throw new CloudException(CloudError.SignInDenied, "No sign-in code.");
        }

        return await client.ExchangeAsync(code, verifier, cancellation).ConfigureAwait(false);
    }

    internal static Uri StartUrl(string challenge, int port, string state) =>
        new($"{CloudConfig.Functions("auth-steam/start")}?challenge={challenge}&port={port}&state={state}");

    /// <summary>base64url(SHA-256(verifier)), as the server checks it.</summary>
    internal static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    internal static string RandomToken(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsSuccess(IReadOnlyDictionary<string, string> query, string state) =>
        query.TryGetValue("state", out var s) && FixedEquals(s, state) && query.ContainsKey("code") && !query.ContainsKey("error");

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    /// <summary>A small dark page in the app's style; the texts are HTML-encoded.</summary>
    internal static string Page((string Title, string Text) content)
    {
        var title = System.Net.WebUtility.HtmlEncode(content.Title);
        var text = System.Net.WebUtility.HtmlEncode(content.Text);
        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>AimOdometer</title>
            <style>body{margin:0;height:100vh;display:flex;align-items:center;justify-content:center;background:#0F0F23;
            color:#E2E8F0;font-family:"Segoe UI Variable Text","Segoe UI",sans-serif}main{max-width:460px;padding:32px;
            border:1px solid #3B2A6B;border-radius:16px;background:#1A1830}h1{font-size:22px;margin:0 0 10px;color:#B9A4FC}
            p{margin:0;color:#94A3B8;line-height:1.5}</style></head>
            <body><main><h1>{{title}}</h1><p>{{text}}</p></main></body></html>
            """;
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AimOdometer.Cloud;

/// <summary>
/// A one-shot HTTP endpoint on 127.0.0.1 and a random port, where the browser lands after signing in (RFC 8252
/// loopback redirect). Plain sockets: HttpListener would need a URL reservation. Only loopback is bound, so nothing
/// outside this PC can reach it.
/// </summary>
public sealed class LoopbackCallback : IDisposable
{
    private const int MaxRequestBytes = 16 * 1024;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public LoopbackCallback()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    /// <summary>
    /// Waits for <c>GET {path}?…</c>, answers it with the page from <paramref name="respond"/> and returns the query.
    /// Other requests (a favicon) get 404 and the wait goes on.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> WaitAsync(
        string path, Func<IReadOnlyDictionary<string, string>, string> respond, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(respond);
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellation).ConfigureAwait(false);
            var stream = client.GetStream();
            var target = await ReadRequestTargetAsync(stream, cancellation).ConfigureAwait(false);
            if (target is null || !target.StartsWith(path, StringComparison.Ordinal) ||
                (target.Length > path.Length && target[path.Length] != '?'))
            {
                await WriteAsync(stream, "404 Not Found", string.Empty, cancellation).ConfigureAwait(false);
                continue;
            }

            var query = ParseQuery(target.Length > path.Length ? target[(path.Length + 1)..] : string.Empty);
            await WriteAsync(stream, "200 OK", respond(query), cancellation).ConfigureAwait(false);
            return query;
        }
    }

    public void Dispose() => _listener.Stop();

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result.TryAdd(key, value);
        }

        return result;
    }

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellation)
    {
        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
                if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return null; // a connection that never sent a request
        }

        var head = Encoding.ASCII.GetString(buffer, 0, length);
        var firstLine = head.Split("\r\n", 2)[0].Split(' ');
        return firstLine.Length == 3 && firstLine[0] == "GET" ? firstLine[1] : null;
    }

    private static async Task WriteAsync(NetworkStream stream, string status, string html, CancellationToken cancellation)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\nReferrer-Policy: no-referrer\r\n\r\n");
        try
        {
            await stream.WriteAsync(head, cancellation).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellation).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The browser closed the connection early; the query was received anyway.
        }
    }
}

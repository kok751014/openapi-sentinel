using System.Net;
using System.Net.Sockets;

namespace OpenApiSentinel.OpenApi;

public sealed partial class SpecLoader
{
    private async Task<LoadedSpec> LoadFromUrlAsync(Uri uri, CancellationToken ct)
    {
        if (!_remotePolicy.Enabled)
            throw new SpecFetchException(
                $"Refusing to load '{uri}': remote document loading is disabled. " +
                "Enable it in the configuration file under 'remoteReferences.enabled: true' or pass --allow-remote.");

        ValidateRemoteUri(uri);

        using var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(_remotePolicy.TimeoutSeconds),
        })
        {
            Timeout = TimeSpan.FromSeconds(_remotePolicy.TimeoutSeconds),
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("openapi-sentinel");
        foreach (var h in _envHeaders)
        {
            if (h.Resolve() is { } resolved)
                request.Headers.TryAddWithoutValidation(resolved.Name, resolved.Value);
        }

        HttpResponseMessage? response = null;
        try
        {
            // Follow redirects manually so every hop is revalidated against the SSRF policy.
            var current = uri;
            for (var hop = 0; hop <= MaxRedirectHops; hop++)
            {
                using var hopRequest = new HttpRequestMessage(HttpMethod.Get, current);
                hopRequest.Headers.UserAgent.ParseAdd("openapi-sentinel");
                foreach (var h in _envHeaders)
                {
                    if (h.Resolve() is { } resolved)
                        hopRequest.Headers.TryAddWithoutValidation(resolved.Name, resolved.Value);
                }

                response?.Dispose();
                response = await client.SendAsync(hopRequest, HttpCompletionOption.ResponseHeadersRead, ct);

                if ((int)response.StatusCode is >= 300 and < 400 &&
                    response.Headers.Location is { } location)
                {
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    current = next;
                    ValidateRemoteUri(current);
                    continue;
                }
                break;
            }
        }
        catch (HttpRequestException ex)
        {
            response?.Dispose();
            throw new SpecFetchException($"Failed to fetch '{uri}': {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            response?.Dispose();
            throw new SpecFetchException(
                $"Timed out fetching '{uri}' after {_remotePolicy.TimeoutSeconds}s.", ex);
        }
        catch (SpecFetchException)
        {
            response?.Dispose();
            throw;
        }

        if (response is null)
            throw new SpecFetchException($"Failed to fetch '{uri}': no response received.");

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new SpecFetchException(
                    $"Failed to fetch '{uri}': HTTP {(int)response.StatusCode} {response.StatusCode}.");

            if (response.Content.Headers.ContentLength is { } len && len > _remotePolicy.MaxResponseBytes)
                throw new SpecFetchException(
                    $"Document at '{uri}' is {len / (1024 * 1024)} MB which exceeds the maximum response size of {_remotePolicy.MaxResponseBytes / (1024 * 1024)} MB.");

            var bytes = await ReadBoundedAsync(response, uri, ct);
            var content = System.Text.Encoding.UTF8.GetString(bytes);
            return new LoadedSpec(uri.ToString(), content, null);
        }
    }

    private const int MaxRedirectHops = 5;

    private async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, Uri uri, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new System.IO.MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        long total = 0;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > _remotePolicy.MaxResponseBytes)
                throw new SpecFetchException(
                    $"Document at '{uri}' exceeds the maximum response size of {_remotePolicy.MaxResponseBytes / (1024 * 1024)} MB.");
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Validates a remote URI against the fetch policy: scheme, host allowlist and SSRF protection.
    /// </summary>
    public void ValidateRemoteUri(Uri uri)
    {
        if (!IsSchemePermitted(uri))
            throw new SpecFetchException(
                $"Refusing to load '{uri}': scheme '{uri.Scheme}' is not permitted. " +
                (_remotePolicy.AllowHttp ? "Only http(s) URLs are supported." : "Use HTTPS or enable 'allowHttp' in remoteReferences settings."));

        if (_remotePolicy.AllowedHosts is { Count: > 0 } allow &&
            !allow.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) &&
            !allow.Contains("*", StringComparer.Ordinal))
            throw new SpecFetchException(
                $"Refusing to load '{uri}': host '{uri.Host}' is not in the configured host allowlist.");

        if (!_remotePolicy.AllowPrivateNetwork)
            GuardAgainstPrivateNetwork(uri);
    }

    private bool IsSchemePermitted(Uri uri) => uri.Scheme switch
    {
        _ when uri.Scheme == Uri.UriSchemeHttps => true,
        _ when uri.Scheme == Uri.UriSchemeHttp => _remotePolicy.AllowHttp,
        _ => false,
    };

    /// <summary>
    /// Resolves the host and rejects loopback, link-local, private and unique-local addresses
    /// unless explicitly permitted. This is the primary SSRF defense.
    /// </summary>
    private static void GuardAgainstPrivateNetwork(Uri uri)
    {
        if (uri.IsLoopback)
            throw new SpecFetchException(
                $"Refusing to load '{uri}': localhost targets are blocked for remote documents. Enable 'allowPrivateNetwork' only for explicitly trusted internal use.");

        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddressesAsync(uri.DnsSafeHost).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new SpecFetchException($"Cannot resolve host '{uri.DnsSafeHost}': {ex.Message}", ex);
        }

        foreach (var ip in addresses)
        {
            if (IsPrivateOrReserved(ip))
                throw new SpecFetchException(
                    $"Refusing to load '{uri}': host resolves to a private/reserved address ({ip}). " +
                    "Enable 'allowPrivateNetwork' in remoteReferences settings only for trusted internal use.");
        }
    }

    internal static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || b[0] == 127
                || b[0] == 0
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)      // link-local
                || (b[0] == 100 && b[1] is >= 64 and <= 127); // CGNAT
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80) return true; // link-local fe80::/10
            if ((b[0] & 0xfe) == 0xfc) return true;                 // unique local fc00::/7
            if (b.All(x => x == 0)) return true;                    // unspecified ::
        }
        return false;
    }
}

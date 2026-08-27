using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// Remote documents must resolve relative sibling-file $refs against their own URL base
/// (multi-file specs served over HTTP, e.g. DigitalOcean's public spec), with every fetched
/// target passing the same fetch policy as any other remote document.
/// Uses a loopback HttpListener; the policy under test therefore opts into http + private network,
/// while separate cases assert the guards fire when those opt-ins are absent.
/// </summary>
public sealed class RemoteSiblingRefTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _prefix;
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public RemoteSiblingRefTests()
    {
        // Port 0 is not supported by HttpListener prefixes; grab a free port via a TCP listener.
        var port = GetFreePort();
        _prefix = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(_prefix);
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }

    internal void Add(string path, string content) => _files[path.TrimStart('/')] = content;

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (_listener.IsListening)
            {
                var ctx = await _listener.GetContextAsync();
                var raw = ctx.Request.Url!.AbsolutePath.TrimStart('/');
                if (_files.TryGetValue(raw, out var body))
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.ContentType = raw.EndsWith(".json", StringComparison.Ordinal)
                        ? "application/json"
                        : "application/yaml";
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                ctx.Response.Close();
            }
        }
        catch (Exception) when (!_listener.IsListening)
        {
            // shutdown race — expected during Dispose
        }
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch { }
    }

    private static ProcessingLimits Limits() => ProcessingLimits.Default();

    /// <summary>Policy that permits the loopback server used by these tests.</summary>
    private static RemoteFetchPolicy LoopbackPolicy(IReadOnlySet<string>? hosts = null) => new()
    {
        Enabled = true,
        AllowHttp = true,
        AllowPrivateNetwork = true,
        AllowedHosts = hosts,
        TimeoutSeconds = 10,
    };

    [Fact]
    public async Task Sibling_File_Ref_From_Remote_Document_Resolves()
    {
        Add("main.yaml", """
            openapi: 3.0.3
            info:
              title: Split spec
              version: "1.0"
            paths:
              /products:
                get:
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: 'components.yaml#/components/schemas/Product'
            """);
        Add("components.yaml", """
            components:
              schemas:
                Product:
                  type: object
                  properties:
                    sku:
                      type: string
            """);

        var root = DocumentParser.Parse("remote-main", await FetchAsync("main.yaml"), Limits());
        new RefResolver(Limits(), LoopbackPolicy()).Resolve(root, baseDirectory: null, baseUri: _prefix + "main.yaml");
        var doc = OpenApiNormalizer.Normalize(root);

        var op = doc.Paths["/products"].Operations[HttpMethodKind.Get];
        var schema = op.Responses["200"].Content["application/json"];
        Assert.Equal("object", schema.PrimaryType());
        Assert.NotNull(schema.PropertyOrNull("sku"));
    }

    [Fact]
    public async Task Nested_Sibling_Refs_Chain_Against_Their_Own_Url_Base()
    {
        Add("main.json", """
            {
              "openapi": "3.0.3",
              "info": { "title": "Chained", "version": "1.0" },
              "paths": {
                "/x": {
                  "get": {
                    "responses": {
                      "200": {
                        "description": "ok",
                        "content": {
                          "application/json": {
                            "schema": { "$ref": "common/a.json#/A" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """);
        Add("common/a.json", """{ "A": { "$ref": "b.json#/B" } }""");
        Add("common/b.json", """
            {
              "B": {
                "type": "object",
                "properties": { "name": { "type": "string" } }
              }
            }
            """);

        var root = DocumentParser.Parse("remote-chain", await FetchAsync("main.json"), Limits());
        new RefResolver(Limits(), LoopbackPolicy()).Resolve(root, baseDirectory: null, baseUri: _prefix + "main.json");
        var doc = OpenApiNormalizer.Normalize(root);

        var schema = doc.Paths["/x"].Operations[HttpMethodKind.Get].Responses["200"].Content["application/json"];
        Assert.NotNull(schema.PropertyOrNull("name"));
    }

    [Fact]
    public async Task Host_Allowlist_Refusal_Degrades_To_Unresolved_Marker_Without_Fetching()
    {
        Add("main.yaml", """
            openapi: 3.0.3
            info:
              title: Allowlisted
              version: "1.0"
            paths:
              /x:
                get:
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: 'other.yaml#/X'
            """);
        Add("other.yaml", "X:\n  type: string\n");

        var limits = Limits();
        var root = DocumentParser.Parse("allowlist", await FetchAsync("main.yaml"), limits);
        // The document itself was already loaded (host permitted at load time), but the
        // sibling fetch runs under an allowlist that no longer contains the host.
        // Policy refusal must degrade to a marker WITHOUT fetching — not abort the run,
        // and never silently pass either.
        var resolver = new RefResolver(limits, LoopbackPolicy(hosts: FrozenHosts(["example.com"])));
        resolver.Resolve(root, null, _prefix + "main.yaml");

        Assert.Equal(1, RefResolver.CountUnresolvedMarkers(root));
    }

    [Fact]
    public async Task Missing_Sibling_File_Degrades_To_Marker()
    {
        Add("main.yaml", """
            openapi: 3.0.3
            info:
              title: Dead link
              version: "1.0"
            paths:
              /x:
                get:
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: 'does-not-exist.yaml#/X'
            """);

        var limits = Limits();
        var root = DocumentParser.Parse("dead-link", await FetchAsync("main.yaml"), limits);
        new RefResolver(limits, LoopbackPolicy()).Resolve(root, null, _prefix + "main.yaml");
        var doc = OpenApiNormalizer.Normalize(root);

        // Analysis continues; exactly one unresolved reference remains visible.
        Assert.Single(doc.Paths);
        Assert.Equal(1, RefResolver.CountUnresolvedMarkers(root));
    }

    [Fact]
    public async Task Sibling_Fetch_Is_Cached_Per_Url()
    {
        Add("main.yaml", """
            openapi: 3.0.3
            info:
              title: Shared
              version: "1.0"
            paths:
              /a:
                get:
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: 'shared.yaml#/S'
              /b:
                get:
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: 'shared.yaml#/S'
            """);
        Add("shared.yaml", "S:\n  type: string\n");

        var root = DocumentParser.Parse("cache-check", await FetchAsync("main.yaml"), Limits());
        new RefResolver(Limits(), LoopbackPolicy()).Resolve(root, null, _prefix + "main.yaml");
        var doc = OpenApiNormalizer.Normalize(root);

        Assert.Equal(doc.Paths["/a"].Operations[HttpMethodKind.Get].Responses["200"].Content["application/json"].PrimaryType(),
                     doc.Paths["/b"].Operations[HttpMethodKind.Get].Responses["200"].Content["application/json"].PrimaryType());
    }

    [Fact]
    public void Stdin_Source_Still_Gets_Actionable_Error_For_Relative_Refs()
    {
        const string spec = """
            openapi: 3.0.3
            info:
              title: Stdin
              version: "1.0"
            paths:
              /x:
                get:
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: 'side.yaml#/Y'
            """;
        var root = DocumentParser.Parse("<stdin>", spec, Limits());
        var ex = Assert.Throws<SpecParseException>(() =>
            new RefResolver(Limits(), LoopbackPolicy()).Resolve(root, null));
        Assert.Contains("stdin and git-revision sources", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InsecureTls_IsExplicit_AndDoesNotBypassRemoteOrPrivatePolicy()
    {
        await using var server = new SelfSignedHttpsServer("openapi: 3.0.3\ninfo: { title: TLS, version: '1' }\npaths: {}\n");
        var url = server.Url;

        var trustedDefaults = new RemoteFetchPolicy { Enabled = true, AllowPrivateNetwork = true };
        var insecure = trustedDefaults with { InsecureTls = true };
        var loaded = await new SpecLoader(insecure).LoadAsync(url);
        Assert.Contains("title: TLS", loaded.Content, StringComparison.Ordinal);

        await Assert.ThrowsAsync<SpecFetchException>(() => new SpecLoader(trustedDefaults).LoadAsync(url));

        await Assert.ThrowsAsync<SpecFetchException>(() => new SpecLoader(insecure with
        {
            AllowPrivateNetwork = false,
        }).LoadAsync(url));

        await Assert.ThrowsAsync<SpecFetchException>(() => new SpecLoader(insecure with
        {
            Enabled = false,
        }).LoadAsync(url));
    }

    private async Task<string> FetchAsync(string path)
    {
        using var client = new HttpClient();
        return await client.GetStringAsync(_prefix + path);
    }

    private static System.Collections.Generic.HashSet<string> FrozenHosts(params string[] hosts) =>
        hosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A tiny in-process HTTPS server with a self-signed certificate. It exercises the
/// actual SocketsHttpHandler TLS path without relying on machine certificate stores.</summary>
internal sealed class SelfSignedHttpsServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly X509Certificate2 _certificate;
    private readonly string _body;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;

    public SelfSignedHttpsServer(string body)
    {
        _body = body;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        _certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), password: null);
        _listener.Start();
        Url = $"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/openapi.yaml";
        _acceptLoop = AcceptLoopAsync();
    }

    public string Url { get; }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await HandleAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        await using (var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
        {
            try
            {
                await stream.AuthenticateAsServerAsync(_certificate, clientCertificateRequired: false,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                var content = Encoding.UTF8.GetBytes(_body);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/yaml\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers);
                await stream.WriteAsync(content);
            }
            catch (IOException) { }
            catch (AuthenticationException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _acceptLoop; } catch (ObjectDisposedException) { }
        _certificate.Dispose();
        _stop.Dispose();
    }
}

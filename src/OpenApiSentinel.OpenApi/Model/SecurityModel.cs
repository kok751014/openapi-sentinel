using OpenApiSentinel.Core;

namespace OpenApiSentinel.OpenApi.Model;

/// <summary>A security requirement: scheme name plus required OAuth/OIDC scopes.</summary>
public sealed record SecurityRequirementInfo
{
    public required string Scheme { get; init; }

    /// <summary>Empty for non-OAuth schemes.</summary>
    public IReadOnlySet<string> Scopes { get; init; } = new HashSet<string>();
}

public enum SecuritySchemeType
{
    ApiKey,
    Http,
    OAuth2,
    OpenIdConnect,
    Basic, // Swagger 2.0
}

public sealed class SecuritySchemeInfo
{
    public required string Name { get; init; }

    /// <summary>For apiKey schemes: the documented parameter name (e.g. "X-API-Key").</summary>
    public string? KeyName { get; init; }

    public required SecuritySchemeType Type { get; init; }

    /// <summary>Raw type string from the document (e.g. "apiKey", "http").</summary>
    public required string RawType { get; init; }

    /// <summary>For apiKey: "query" | "header" | "cookie".</summary>
    public string? In { get; init; }

    public string? Scheme { get; init; } // http: basic/bearer/...

    public string? BearerFormat { get; init; }

    public string? OpenIdConnectUrl { get; init; }

    public IReadOnlyDictionary<string, OAuthFlow> Flows { get; init; } =
        new Dictionary<string, OAuthFlow>();

    public bool Deprecated { get; init; }
}

public sealed record OAuthFlow(
    string? AuthorizationUrl,
    string? TokenUrl,
    string? RefreshUrl,
    IReadOnlySet<string> Scopes);

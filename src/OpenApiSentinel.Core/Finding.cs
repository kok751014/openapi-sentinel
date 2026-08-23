using System.Text.Json.Serialization;

namespace OpenApiSentinel.Core;

/// <summary>
/// A single user-facing compatibility finding produced by the rule engine.
/// </summary>
public sealed record Finding
{
    /// <summary>Stable rule identifier, e.g. "CG1001".</summary>
    public required string RuleId { get; init; }

    public required string Title { get; init; }

    public required Severity Severity { get; init; }

    public required FindingCategory Category { get; init; }

    public required ChangeType ChangeType { get; init; }

    public required Compatibility Compatibility { get; init; }

    public Confidence Confidence { get; init; } = Confidence.Certain;

    public ApiDirection Direction { get; init; } = ApiDirection.Neutral;

    /// <summary>API path, e.g. "/orders/{orderId}". Empty for document-level findings.</summary>
    public string Path { get; init; } = "";

    public HttpMethodKind HttpMethod { get; init; } = HttpMethodKind.None;

    /// <summary>Precise location within the contract, e.g. "GET /orders parameters.customerId".</summary>
    public string Location { get; init; } = "";

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    /// <summary>What changed?</summary>
    public required string Explanation { get; init; }

    /// <summary>Why does it matter and who is affected?</summary>
    public required string Impact { get; init; }

    /// <summary>What should the developer do?</summary>
    public required string SuggestedAction { get; init; }

    /// <summary>Deterministic fingerprint used for baseline matching.</summary>
    [JsonIgnore]
    public string Fingerprint => Fingerprinting.Compute(this);
}

public static class Fingerprinting
{
    /// <summary>
    /// Computes a stable SHA-256 based fingerprint from the identifying parts of a finding.
    /// Explanatory text is deliberately excluded so that wording improvements do not invalidate baselines.
    /// </summary>
    public static string Compute(Finding finding)
    {
        var payload = string.Join('\u001f',
            finding.RuleId,
            finding.Path,
            finding.HttpMethod.ToHttpString(),
            finding.Location,
            finding.OldValue ?? "",
            finding.NewValue ?? "");
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

namespace OpenApiSentinel.OpenApi;

/// <summary>Base exception for all specification loading/parsing failures. Mapped to exit code 2.</summary>
public class SpecParseException : Exception
{
    public SpecParseException(string message) : base(message) { }
    public SpecParseException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Network/reference retrieval failure. Mapped to exit code 4.</summary>
public class SpecFetchException : Exception
{
    public SpecFetchException(string message) : base(message) { }
    public SpecFetchException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Hard processing bounds applied to every loaded document. Protect against
/// decompression/resource abuse, deep nesting, reference bombs and DoS-style inputs.
/// </summary>
public sealed record ProcessingLimits
{
    /// <summary>Maximum serialized document size in bytes.</summary>
    public int MaxDocumentBytes { get; init; } = 20 * 1024 * 1024;

    /// <summary>Maximum JSON/YAML nesting depth.</summary>
    public int MaxDepth { get; init; } = 128;

    /// <summary>Maximum number of nodes after conversion to the document tree.</summary>
    public long MaxNodes { get; init; } = 2_000_000;

    /// <summary>Maximum YAML anchors allowed (billion-laughs mitigation).</summary>
    public int MaxYamlAnchors { get; init; } = 256;

    /// <summary>Maximum YAML aliases per anchor expansion heuristic (total alias uses).</summary>
    public int MaxYamlAliasUses { get; init; } = 100_000;

    /// <summary>Maximum total resolved references per document.</summary>
    public int MaxResolvedRefs { get; init; } = 200_000;

    /// <summary>Maximum $ref chain depth before a cycle/chain error is raised.</summary>
    public int MaxRefChainDepth { get; init; } = 64;

    public static ProcessingLimits Default() => new();
}

namespace OpenApiSentinel.Core;

/// <summary>
/// Static description of a built-in rule used by `openapi-sentinel rules` and `openapi-sentinel explain`.
/// This is the single source of truth for rule metadata; documentation is generated from it.
/// </summary>
public sealed record RuleDescriptor
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required FindingCategory Category { get; init; }

    public required Severity DefaultSeverity { get; init; }

    public required Compatibility DefaultCompatibility { get; init; }

    /// <summary>The structural change kinds this rule evaluates.</summary>
    public required IReadOnlyList<string> ChangeKinds { get; init; }

    public required string Description { get; init; }

    /// <summary>Why the change matters / who is affected.</summary>
    public required string Impact { get; init; }

    public required string Remediation { get; init; }

    /// <summary>Short YAML/JSON snippet showing a breaking example.</summary>
    public string? BreakingExample { get; init; }

    /// <summary>Short YAML/JSON snippet showing a non-breaking variant where relevant.</summary>
    public string? NonBreakingExample { get; init; }

    /// <summary>Known exceptions or limitations of the rule.</summary>
    public string? Limitations { get; init; }
}

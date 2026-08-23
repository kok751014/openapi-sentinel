using OpenApiSentinel.Core;

namespace OpenApiSentinel.Rules;

/// <summary>A built-in rule: static documentation metadata plus a change-to-finding handler.</summary>
public sealed record RuleDefinition
{
    public required RuleDescriptor Descriptor { get; init; }

    /// <summary>Maps a semantic change handled by this rule into a user-facing finding.</summary>
    public required Func<SemanticChange, Finding> Build { get; init; }

    /// <summary>
    /// Optional guard for kinds shared by several directional rules (e.g. became-required vs
    /// became-optional). When set, the engine only routes changes where this returns true;
    /// unset means the rule accepts every change of its kinds.
    /// </summary>
    public Func<SemanticChange, bool>? AppliesTo { get; init; }
}

/// <summary>Central registry of all built-in rules, keyed by rule ID.</summary>
public sealed class RuleRegistry
{
    private readonly Dictionary<string, RuleDefinition> _rules;

    public RuleRegistry(IEnumerable<RuleDefinition> definitions)
    {
        var byId = new Dictionary<string, RuleDefinition>(StringComparer.Ordinal);
        foreach (var def in definitions)
        {
            if (!byId.TryAdd(def.Descriptor.Id, def))
                throw new InvalidOperationException($"Duplicate rule id '{def.Descriptor.Id}'.");
        }
        _rules = byId;
    }

    public static RuleRegistry CreateDefault() => new(DefaultRules.All());

    public IReadOnlyCollection<RuleDefinition> All => _rules.Values;

    public bool TryGet(string ruleId, out RuleDefinition definition) =>
        _rules.TryGetValue(ruleId, out definition!);

    /// <summary>All change kinds that at least one registered rule can evaluate.</summary>
    public IReadOnlySet<string> SupportedChangeKinds =>
        _rules.Values.SelectMany(r => r.Descriptor.ChangeKinds).ToHashSet(StringComparer.Ordinal);
}

/// <summary>Shared text/format helpers for rule implementations.</summary>
internal static class RuleText
{
    public static string DescribeChange(SemanticChange c)
    {
        var where = c.HttpMethod == HttpMethodKind.None
            ? c.Path
            : $"{c.HttpMethod.ToHttpString()} {c.Path}";
        return string.IsNullOrEmpty(c.Location) ? where : $"{where} → {c.Location}";
    }

    public static string Values(SemanticChange c)
    {
        var oldV = c.OldValue is null ? "(none)" : c.OldValue;
        var newV = c.NewValue is null ? "(removed)" : c.NewValue;
        return $"'{oldV}' → '{newV}'";
    }

    /// <summary>Parses a numeric JSON-ish value string for constraint comparisons.</summary>
    public static bool TryGetNumber(string? value, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return double.TryParse(value.Trim().Trim('"'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out number);
    }

    public static ChangeType ChangeTypeOf(SemanticChange c) => c.Kind switch
    {
        var k when k.EndsWith(".added") => ChangeType.Added,
        var k when k.EndsWith(".removed") => ChangeType.Removed,
        var k when k.Contains("renamed") => ChangeType.Renamed,
        _ => ChangeType.Changed,
    };
}

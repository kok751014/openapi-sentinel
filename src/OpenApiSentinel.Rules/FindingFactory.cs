using OpenApiSentinel.Core;

namespace OpenApiSentinel.Rules;

/// <summary>Shared factory for building findings inside rule handlers.</summary>
internal static class FindingFactory
{
    public static Finding Create(
        string ruleId, string title, Severity severity, Compatibility compatibility,
        SemanticChange change, string explanation, string impact, string action,
        Confidence confidence = Confidence.Certain)
    {
        var category = change.Category;
        // Schema-level changes carry the direction in the change; category already reflects it.
        return new Finding
        {
            RuleId = ruleId,
            Title = title,
            Severity = severity,
            Category = category,
            ChangeType = RuleText.ChangeTypeOf(change),
            Compatibility = compatibility,
            Confidence = confidence,
            Direction = change.Direction,
            Path = change.Path,
            HttpMethod = change.HttpMethod,
            Location = change.Location,
            OldValue = change.OldValue,
            NewValue = change.NewValue,
            Explanation = explanation,
            Impact = impact,
            SuggestedAction = action,
        };
    }
}

/// <summary>Direction-aware helpers shared by schema/parameter constraint rules.</summary>
internal static class DirectionLogic
{
    public enum Impact
    {
        Tightened,
        Relaxed,
        Uncertain,
    }

    /// <summary>
    /// Classifies a numeric constraint transition. In the request direction a stricter
    /// server-side contract breaks clients; in the response direction a wider output
    /// contract breaks clients instead.
    /// </summary>
    public static Impact ClassifyNumeric(string keyword, double oldV, double newV) => keyword switch
    {
        "minimum" or "exclusiveMinimum" or "minLength" or "minItems" or "minProperties" =>
            newV > oldV ? Impact.Tightened : newV < oldV ? Impact.Relaxed : Impact.Uncertain,
        "maximum" or "exclusiveMaximum" or "maxLength" or "maxItems" or "maxProperties" =>
            newV < oldV ? Impact.Tightened : newV > oldV ? Impact.Relaxed : Impact.Uncertain,
        _ => Impact.Uncertain, // multipleOf etc.
    };

    /// <summary>Maps request-direction impact to finding values.</summary>
    public static (Severity Severity, Compatibility Compatibility) ForRequest(Impact impact, bool patternLike = false)
    {
        if (patternLike || impact == Impact.Uncertain)
            return (Severity.Verify, Compatibility.Unknown);
        return impact == Impact.Tightened
            ? (Severity.High, Compatibility.Breaking)
            : (Severity.Info, Compatibility.NonBreaking);
    }

    public static (Severity Severity, Compatibility Compatibility) ForResponse(Impact impact, bool patternLike = false)
    {
        if (patternLike || impact == Impact.Uncertain)
            return (Severity.Verify, Compatibility.Unknown);
        return impact == Impact.Relaxed
            ? (Severity.Medium, Compatibility.PotentiallyBreaking)
            : (Severity.Info, Compatibility.NonBreaking);
    }

    /// <summary>Format pairs where the second accepts a superset of the first's values.</summary>
    private static readonly HashSet<(string From, string To)> WideningFormats = new()
    {
        ("int32", "int64"),
        ("float", "double"),
        ("date", "date-time"),
    };

    /// <summary>
    /// Direction-aware format-change classification for known precision pairs.
    /// Unknown pairs keep the conservative potentially-breaking classification.
    /// </summary>
    public static (Severity Severity, Compatibility Compatibility, Confidence Confidence) ForFormat(
        string? oldFormat, string? newFormat, ApiDirection direction)
    {
        if (oldFormat is null || newFormat is null || oldFormat == newFormat)
            return (Severity.Medium, Compatibility.PotentiallyBreaking, Confidence.Likely);

        if (WideningFormats.Contains((oldFormat, newFormat)))
        {
            // Server now accepts/produces a superset of values.
            return direction == ApiDirection.Request
                ? (Severity.Info, Compatibility.NonBreaking, Confidence.Certain)
                : (Severity.Medium, Compatibility.PotentiallyBreaking, Confidence.Likely);
        }

        if (WideningFormats.Contains((newFormat, oldFormat)))
        {
            // Server narrowed: request payloads may be rejected; response values now fit smaller ranges.
            return direction == ApiDirection.Request
                ? (Severity.High, Compatibility.Breaking, Confidence.Certain)
                : (Severity.Low, Compatibility.NonBreaking, Confidence.Likely);
        }

        return (Severity.Medium, Compatibility.PotentiallyBreaking, Confidence.Likely);
    }

    /// <summary>Numeric JSON types where widening/narrowing has well-defined semantics.</summary>
    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal) { "integer", "number" };

    /// <summary>
    /// Direction-aware classification for type changes within the numeric family
    /// (integer ↔ number). Cross-family changes fall back to the caller's defaults.
    /// </summary>
    public static (Severity Severity, Compatibility Compatibility, Confidence Confidence)? ForNumericTypeChange(
        string? oldType, string? newType, ApiDirection direction)
    {
        // Comparer emits type sets JSON-encoded and pipe-joined ("integer" or "string|number").
        static HashSet<string>? SplitTypes(string? raw)
        {
            if (raw is null) return null;
            var set = raw.Trim('"').Split('|').Select(t => t.Trim()).ToHashSet(StringComparer.Ordinal);
            return set.Count == 0 || set.Any(t => !NumericTypes.Contains(t)) ? null : set;
        }

        var oldSet = SplitTypes(oldType);
        var newSet = SplitTypes(newType);
        if (oldSet is null || newSet is null) return null;

        // integer → number widens accepted/produced values; number → integer narrows them.
        // Unions widen when the new set is a proper superset, narrow on the reverse.
        var widened =
            (oldSet.Count == 1 && newSet.Count == 1 && oldSet.First() == "integer" && newSet.First() == "number") ||
            (!newSet.SetEquals(oldSet) && newSet.IsSupersetOf(oldSet));
        if (newSet.SetEquals(oldSet)) return null;

        return direction == ApiDirection.Request
            ? (widened ? Severity.Low : Severity.Critical,
               widened ? Compatibility.NonBreaking : Compatibility.Breaking,
               Confidence.Certain)
            : (widened ? Severity.Medium : Severity.Low,
               widened ? Compatibility.PotentiallyBreaking : Compatibility.NonBreaking,
               widened ? Confidence.Likely : Confidence.Certain);
    }
}

using OpenApiSentinel.OpenApi;
using OpenApiSentinel.Core;

namespace OpenApiSentinel.Rules;

/// <summary>Rules for parameter-level changes (CG20xx). All parameter changes are request-direction.</summary>
internal static class ParameterRules
{
    public static IEnumerable<RuleDefinition> All()
    {
        yield return Required();
        yield return Added();
        yield return BecameRequired();
        yield return BecameOptional();
        yield return TypeChanged();
        yield return FormatChanged();
        yield return EnumValueRemoved();
        yield return EnumValueAdded();
        yield return DefaultChanged();
        yield return NullableChanged();
        yield return ConstraintChanged();
        yield return ItemsChanged();
        yield return StyleChanged();
        yield return ExplodeChanged();
        yield return LocationChanged();
        yield return DeprecatedFlag();
    }

    private static string Where(SemanticChange c) =>
        c.HttpMethod == HttpMethodKind.None ? c.Path : $"{c.HttpMethod.ToHttpString()} {c.Path}";

    private static string ParamName(SemanticChange c)
    {
        if (!string.IsNullOrEmpty(c.ParameterName)) return c.ParameterName;
        return c.Details.TryGetValue("name", out var n) && !string.IsNullOrEmpty(n) ? n : "(unknown)";
    }

    private static RuleDefinition Required() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2001", Title = "Parameter removed",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.High,
            DefaultCompatibility = Compatibility.Breaking,
            ChangeKinds = [ChangeKinds.ParameterRemoved],
            Description = "A documented input parameter disappeared. The server no longer accepts or documents it.",
            Impact = "Clients that send this parameter may receive 400 responses (strict servers) or silently lose intended behaviour (tolerant servers).",
            Remediation = "Keep accepting the parameter during a deprecation window and document its removal.",
            BreakingExample = "parameters:\n  - name: expand\n    in: query     # removed in v2",
            NonBreakingExample = "# keep the parameter but mark it deprecated",
            Limitations = "Impact assumes servers validate unknown parameters or the parameter influenced behaviour.",
        },
        Build = c =>
        {
            c.Details.TryGetValue("required", out var required);
            var wasRequired = required == "true";
            var sev = wasRequired ? Severity.High : Severity.Medium;
            return FindingFactory.Create("CG2001", "Parameter removed", sev,
                Compatibility.Breaking, c,
                $"Parameter '{ParamName(c)}' was removed from {Where(c)}.",
                wasRequired
                    ? "Clients must send this parameter today; after the change they will receive validation errors."
                    : "Clients sending this optional parameter may trigger validation errors or lose behaviour they depended on.",
                "Re-add the parameter as deprecated, or migrate clients and document the removal.");
        },
    };

    private static RuleDefinition Added() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2002", Title = "Required parameter added",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.High,
            DefaultCompatibility = Compatibility.Breaking,
            ChangeKinds = [ChangeKinds.ParameterAdded],
            Description = "A new parameter was added. When required, existing calls fail immediately.",
            Impact = "Required additions break every existing caller that does not send the new parameter.",
            Remediation = "Introduce the parameter as optional, migrate consumers, then enforce the requirement in a later version.",
            BreakingExample = "parameters:\n  - name: customerId\n    in: query\n    required: true   # newly added",
            NonBreakingExample = "parameters:\n  - name: customerId\n    in: query\n    required: false",
        },
        Build = c =>
        {
            c.Details.TryGetValue("required", out var required);
            var isRequired = required == "true";
            var sev = isRequired ? Severity.High : Severity.Info;
            var compat = isRequired ? Compatibility.Breaking : Compatibility.NonBreaking;
            return FindingFactory.Create("CG2002",
                isRequired ? "Required parameter added" : "Optional parameter added",
                sev, compat, c,
                $"Parameter '{ParamName(c)}' was added to {Where(c)} as {(isRequired ? "required" : "optional")}.",
                isRequired
                    ? "Existing clients that do not send this parameter may begin receiving validation errors."
                    : "Additive change: clients that ignore the parameter keep working.",
                isRequired
                    ? "Introduce the field as optional first, migrate consumers, then enforce the requirement in a later API version."
                    : "No action required.");
        },
    };

    private static RuleDefinition BecameRequired() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2003", Title = "Optional parameter became required",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.High,
            DefaultCompatibility = Compatibility.Breaking,
            ChangeKinds = [ChangeKinds.ParameterRequiredChanged],
            Description = "A parameter that clients could previously omit is now mandatory.",
            Impact = "Existing clients that omit the parameter may begin receiving validation errors (HTTP 400).",
            Remediation = "Keep the parameter optional, or enforce the requirement only in a new major version after migrating consumers.",
            BreakingExample = "- name: customerId\n  in: query\n  required: false   # -> required: true",
        },
        AppliesTo = c => c.NewValue == "required",
        Build = c => FindingFactory.Create("CG2003", "Optional parameter became required", Severity.High,
            Compatibility.Breaking, c,
            $"Parameter '{ParamName(c)}' on {Where(c)} changed from optional to required.",
            "Existing clients that do not send this parameter may begin receiving validation errors.",
            "Introduce the field as optional first, migrate consumers, then enforce the requirement in a later API version."),
    };

    private static RuleDefinition BecameOptional() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2004", Title = "Required parameter became optional",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.Low,
            DefaultCompatibility = Compatibility.NonBreaking,
            ChangeKinds = [ChangeKinds.ParameterRequiredChanged],
            Description = "A previously mandatory parameter can now be omitted.",
            Impact = "Non-breaking: all previously valid requests remain valid.",
            Remediation = "No action required. Verify business logic handles missing values as intended.",
        },
        AppliesTo = c => c.NewValue != "required",
        Build = c => FindingFactory.Create("CG2004", "Required parameter became optional", Severity.Low,
            Compatibility.NonBreaking, c,
            $"Parameter '{ParamName(c)}' on {Where(c)} changed from required to optional.",
            "Non-breaking: previously valid requests stay valid.",
            "No action required; confirm default behaviour when omitted is well-defined."),
    };

    private static RuleDefinition TypeChanged() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2005", Title = "Parameter type changed",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.High,
            DefaultCompatibility = Compatibility.Breaking,
            ChangeKinds = [ChangeKinds.ParameterTypeChanged],
            Description = "The data type of a parameter changed (e.g. string → integer).",
            Impact = "Clients sending the old representation will fail deserialization or validation.",
            Remediation = "Avoid changing parameter types; add a new parameter and deprecate the old one.",
            BreakingExample = "- name: count\n  in: query\n  schema: {type: string}   # -> {type: integer}",
        },
        Build = c => FindingFactory.Create("CG2005", "Parameter type changed", Severity.High,
            Compatibility.Breaking, c,
            $"Parameter '{ParamName(c)}' type changed: {RuleText.Values(c)}.",
            "Requests using the old type will be rejected or misinterpreted.",
            "Keep the original type; if unavoidable, add a new parameter and deprecate this one."),
    };

    private static RuleDefinition FormatChanged() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2006", Title = "Parameter format changed",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.Medium,
            DefaultCompatibility = Compatibility.PotentiallyBreaking,
            ChangeKinds = [ChangeKinds.ParameterFormatChanged],
            Description = "The format of a parameter changed (e.g. int32 → int64, date-time → date).",
            Impact = "Depending on direction this may reject previously valid values or alter precision expectations.",
            Remediation = "Widen formats only when safe (e.g. int32 → int64); document any precision changes.",
            BreakingExample = "schema: {type: integer, format: int32}   # -> format: int64",
        },
        Build = c =>
        {
            var (severity, compatibility, confidence) =
                DirectionLogic.ForFormat(c.OldValue, c.NewValue, ApiDirection.Request);
            return FindingFactory.Create("CG2006", "Parameter format changed",
                severity, compatibility, c,
                $"Parameter '{ParamName(c)}' format changed: {RuleText.Values(c)}.",
                compatibility == Compatibility.Breaking
                    ? "Clients may send values the server can no longer represent (e.g. int64 values over int32)."
                    : "Clients may send values that no longer match the expected format or lose precision expectations.",
                "Verify client compatibility; prefer widening formats and documenting precision changes.",
                confidence);
        },
    };

    private static RuleDefinition EnumValueRemoved() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2007", Title = "Parameter enum value removed",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.High,
            DefaultCompatibility = Compatibility.Breaking,
            ChangeKinds = [ChangeKinds.ParameterEnumValueRemoved],
            Description = "A value the server previously accepted for this parameter is no longer allowed.",
            Impact = "Clients still sending the removed value will receive validation errors.",
            Remediation = "Keep accepting the value during a deprecation window, or reject it only in a new major version.",
            BreakingExample = "schema:\n  type: string\n  enum: [open, closed, pending]   # pending removed",
            NonBreakingExample = "enum: [open, closed]   # adding values is non-breaking for requests",
        },
        Build = c => FindingFactory.Create("CG2007", "Parameter enum value removed", Severity.High,
            Compatibility.Breaking, c,
            $"Enum value {c.OldValue} was removed from parameter '{ParamName(c)}'.",
            "Clients sending this value will now be rejected.",
            "Deprecate the value first; remove acceptance only after consumers migrated."),
    };

    private static RuleDefinition EnumValueAdded() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2008", Title = "Parameter enum value added",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.Info,
            DefaultCompatibility = Compatibility.NonBreaking,
            ChangeKinds = [ChangeKinds.ParameterEnumValueAdded],
            Description = "A new value is accepted for this parameter.",
            Impact = "Non-breaking for senders: the server accepts strictly more inputs.",
            Remediation = "No action required for compatibility.",
        },
        Build = c => FindingFactory.Create("CG2008", "Parameter enum value added", Severity.Info,
            Compatibility.NonBreaking, c,
            $"New enum value {c.NewValue} is accepted by parameter '{ParamName(c)}'.",
            "Additive: existing requests remain valid.",
            "No action required."),
    };

    private static RuleDefinition DefaultChanged() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2009", Title = "Parameter default value changed",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.Verify,
            DefaultCompatibility = Compatibility.Unknown,
            ChangeKinds = [ChangeKinds.ParameterDefaultChanged],
            Description = "The default applied when the parameter is omitted changed.",
            Impact = "Clients omitting the parameter will observe different server behaviour. Impact depends on business semantics.",
            Remediation = "Confirm the new default is safe for all existing consumers and announce the behaviour change.",
            BreakingExample = "schema:\n  type: integer\n  default: 10   # was 25",
        },
        Build = c => FindingFactory.Create("CG2009", "Parameter default value changed", Severity.Verify,
            Compatibility.Unknown, c,
            $"Default of parameter '{ParamName(c)}' changed: {RuleText.Values(c)}.",
            "Clients omitting this parameter will experience different behaviour; impact is business-dependent.",
            "Verify the new default against consumer expectations and communicate the change."),
    };

    private static RuleDefinition NullableChanged() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2010", Title = "Parameter nullability changed",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.High,
            DefaultCompatibility = Compatibility.Breaking,
            ChangeKinds = [ChangeKinds.ParameterNullableChanged],
            Description = "Whether null is an accepted value for this parameter changed.",
            Impact = "Disallowing null rejects requests that previously succeeded. Allowing null is safe for senders.",
            Remediation = "Keep accepting null during a transition; enforce non-null in a future major version.",
            BreakingExample = "schema: {type: string, nullable: true}   # -> nullable: false",
        },
        Build = c =>
        {
            var nowNullable = c.NewValue == "nullable";
            return FindingFactory.Create("CG2010", "Parameter nullability changed",
                nowNullable ? Severity.Info : Severity.High,
                nowNullable ? Compatibility.NonBreaking : Compatibility.Breaking, c,
                $"Nullability of parameter '{ParamName(c)}' changed: {RuleText.Values(c)}.",
                nowNullable
                    ? "Non-breaking: the server now accepts strictly more inputs."
                    : "Clients sending null will now be rejected.",
                nowNullable ? "No action required." : "Keep accepting null during a transition period.");
        },
    };

    private static RuleDefinition ConstraintChanged() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2011", Title = "Parameter constraint changed",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.High,
            DefaultCompatibility = Compatibility.Breaking,
            ChangeKinds = [ChangeKinds.ParameterConstraintChanged],
            Description = "A validation constraint (minimum, maximum, minLength, maxLength, pattern, minItems, maxItems, uniqueItems, multipleOf, minProperties, maxProperties) changed on a request parameter.",
            Impact = "Tightened constraints reject previously valid client inputs; relaxed constraints are usually safe.",
            Remediation = "Avoid tightening validation on existing inputs; relax constraints freely and tighten only in a major version.",
            BreakingExample = "schema:\n  type: integer\n  minimum: 0   # -> minimum: 10",
            NonBreakingExample = "schema:\n  type: integer\n  minimum: 10  # -> minimum: 0 (relaxed)",
            Limitations = "Pattern changes cannot be classified automatically and are reported as Verify.",
        },
        Build = c =>
        {
            c.Details.TryGetValue("constraint", out var constraint);
            var patternLike = constraint is "pattern" or "multipleOf";
            var impact = DirectionLogic.Impact.Uncertain;

            if (!patternLike &&
                RuleText.TryGetNumber(c.OldValue, out var oldN) &&
                RuleText.TryGetNumber(c.NewValue, out var newN))
            {
                impact = DirectionLogic.ClassifyNumeric(constraint ?? "", oldN, newN);
            }
            else if (!patternLike && c.OldValue is null)
            {
                // constraint introduced from nothing: treat as tightening
                impact = DirectionLogic.Impact.Tightened;
            }
            else if (!patternLike && c.NewValue is null)
            {
                // constraint dropped: relaxation
                impact = DirectionLogic.Impact.Relaxed;
            }

            var (sev, compat) = DirectionLogic.ForRequest(impact, patternLike);
            return FindingFactory.Create("CG2011", "Parameter constraint changed", sev, compat, c,
                $"Constraint '{constraint}' of parameter '{ParamName(c)}' changed: {RuleText.Values(c)}.",
                impact == DirectionLogic.Impact.Tightened
                    ? "Requests that were valid before may now be rejected by validation."
                    : impact == DirectionLogic.Impact.Relaxed
                        ? "Constraint relaxed: previously valid requests remain valid."
                        : "Impact cannot be determined automatically; validate representative client inputs.",
                impact == DirectionLogic.Impact.Tightened
                    ? "Relax the constraint, or tighten only in a new major version after verifying consumer data."
                    : "No immediate action; document the new constraint behaviour.");
        },
    };

    private static RuleDefinition ItemsChanged() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2012", Title = "Parameter array item schema changed",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.Medium,
            DefaultCompatibility = Compatibility.PotentiallyBreaking,
            ChangeKinds = [ChangeKinds.ParameterItemsChanged],
            Description = "The schema of array elements in a parameter changed.",
            Impact = "Array contents the server accepted before may now be rejected, or new shapes may be accepted.",
            Remediation = "Review the item schema change and validate representative client payloads.",
            BreakingExample = "items: {type: string}   # -> items: {type: integer}",
        },
        Build = c => FindingFactory.Create("CG2012", "Parameter array item schema changed",
            Severity.Medium, Compatibility.PotentiallyBreaking, c,
            $"Array items of parameter '{ParamName(c)}' changed: {RuleText.Values(c)}.",
            "Previously accepted array payloads may now be rejected.",
            "Review the item schema diff and test existing client payloads."),
    };

    private static RuleDefinition StyleChanged() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2013", Title = "Parameter serialization style changed",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.Medium,
            DefaultCompatibility = Compatibility.Breaking,
            ChangeKinds = [ChangeKinds.ParameterStyleChanged],
            Description = "The serialization style (form, spaceDelimited, pipeDelimited, deepObject, ...) of a parameter changed.",
            Impact = "Clients serialize values differently; the server may parse them incorrectly.",
            Remediation = "Keep the existing style; change only in a major version with client updates.",
            BreakingExample = "style: form   # -> style: pipeDelimited",
        },
        Build = c => FindingFactory.Create("CG2013", "Parameter serialization style changed",
            Severity.Medium, Compatibility.Breaking, c,
            $"Serialization style of parameter '{ParamName(c)}' changed: {RuleText.Values(c)}.",
            "Clients and servers may disagree on how values are encoded on the wire.",
            "Keep the current style; coordinate a wire-format change only with a major version."),
    };

    private static RuleDefinition ExplodeChanged() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2014", Title = "Parameter explode behaviour changed",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.Medium,
            DefaultCompatibility = Compatibility.Breaking,
            ChangeKinds = [ChangeKinds.ParameterExplodeChanged],
            Description = "The explode flag (how arrays/objects are serialized) changed.",
            Impact = "Array/object parameters are encoded differently, breaking parsers on either side.",
            Remediation = "Keep explode behaviour stable; change only with a coordinated major version.",
            BreakingExample = "explode: false   # -> explode: true",
        },
        Build = c => FindingFactory.Create("CG2014", "Parameter explode behaviour changed",
            Severity.Medium, Compatibility.Breaking, c,
            $"Explode behaviour of parameter '{ParamName(c)}' changed: {RuleText.Values(c)}.",
            "Wire format for arrays/objects changes; existing clients may send values the server cannot parse.",
            "Preserve the existing explode behaviour or migrate clients explicitly."),
    };

    private static RuleDefinition LocationChanged() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2015", Title = "Parameter moved to a different location",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.High,
            DefaultCompatibility = Compatibility.Breaking,
            ChangeKinds = [ChangeKinds.ParameterLocationChanged],
            Description = "A parameter moved between query, header, path or cookie.",
            Impact = "Clients send the value in the wrong place; the server will not find it.",
            Remediation = "Accept the parameter in both locations during migration, or introduce a new parameter name.",
            BreakingExample = "- name: traceId\n  in: query   # -> in: header",
        },
        Build = c => FindingFactory.Create("CG2015", "Parameter location changed", Severity.High,
            Compatibility.Breaking, c,
            $"Parameter '{ParamName(c)}' moved from {c.OldValue} to {c.NewValue}.",
            "Clients still sending the value in the old location will be treated as not providing it.",
            "Support both locations during a migration window."),
    };

    private static RuleDefinition DeprecatedFlag() => new()
    {
        Descriptor = new RuleDescriptor
        {
            Id = "CG2016", Title = "Parameter deprecated",
            Category = FindingCategory.Parameter,
            DefaultSeverity = Severity.Medium,
            DefaultCompatibility = Compatibility.PotentiallyBreaking,
            ChangeKinds = [ChangeKinds.ParameterDeprecatedChanged],
            Description = "A parameter was marked (or unmarked) deprecated.",
            Impact = "Signals planned removal; not immediately breaking.",
            Remediation = "Publish a migration plan for consumers using this parameter.",
        },
        Build = c =>
        {
            var nowDeprecated = c.NewValue == "true";
            return FindingFactory.Create("CG2016",
                nowDeprecated ? "Parameter deprecated" : "Parameter undeprecated",
                nowDeprecated ? Severity.Medium : Severity.Low,
                nowDeprecated ? Compatibility.PotentiallyBreaking : Compatibility.NonBreaking, c,
                $"Parameter '{ParamName(c)}' deprecated flag changed: {RuleText.Values(c)}.",
                nowDeprecated ? "Consumers should stop using this parameter before it is removed." : "No impact.",
                nowDeprecated ? "Document the deprecation and the replacement parameter." : "No action required.");
        },
    };
}

using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.Rules;

/// <summary>
/// Direction-aware schema rules (CG50xx). The same structural change can be breaking or safe
/// depending on whether it affects what clients SEND (request) or what the server RETURNS (response).
/// </summary>
internal static class SchemaRules
{
    private static string Where(SemanticChange c) =>
        c.HttpMethod == HttpMethodKind.None ? c.Path : $"{c.HttpMethod.ToHttpString()} {c.Path}";

    private static string DirectionWord(ApiDirection d) => d switch
    {
        ApiDirection.Request => "request",
        ApiDirection.Response => "response",
        _ => "",
    };

    public static IEnumerable<RuleDefinition> All()
    {
        // ---------- required property changes (the flagship direction-aware rule) ----------
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5015", Title = "Required property changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.RequiredPropertyChanged],
                Description = "A property entered or left the 'required' list. Direction decides impact: requiring input properties breaks senders; removing required output properties breaks readers.",
                Impact = "Request: clients omitting a now-required property get validation errors. Response: clients relying on an always-present property may crash when it disappears.",
                Remediation = "Request side: add properties as optional first, migrate, then enforce. Response side: keep returning previously guaranteed properties.",
                BreakingExample = "# request body\nrequired: [id]\nproperties:\n  id: {...}      # newly required\n\n# response\nrequired: [total] # removed from response required list",
                NonBreakingExample = "# request: property became optional\n# response: property became required",
            },
            Build = c =>
            {
                var becameRequired = c.Details.TryGetValue("becameRequired", out var br) && br == "true";
                var isRequest = c.Direction == ApiDirection.Request;

                return FindingFactory.Create(
                    "CG5015",
                    Title(isRequest, becameRequired),
                    Severity(isRequest, becameRequired),
                    Compatibility(isRequest, becameRequired),
                    c,
                    Explanation(c, isRequest, becameRequired),
                    Impact(isRequest, becameRequired),
                    Action(isRequest, becameRequired));

                static string Title(bool req, bool became) => (req, became) switch
                {
                    (true, true) => "Required property added to request",
                    (true, false) => "Required property no longer required in request",
                    (false, true) => "Response property made required",
                    (false, false) => "Required response property removed from required list",
                };

                static Severity Severity(bool req, bool became) => (req, became) switch
                {
                    (true, true) => Core.Severity.High,
                    (true, false) => Core.Severity.Low,
                    (false, true) => Core.Severity.Low,
                    (false, false) => Core.Severity.High,
                };

                static Compatibility Compatibility(bool req, bool became) => (req, became) switch
                {
                    (true, true) => Core.Compatibility.Breaking,
                    (true, false) => Core.Compatibility.NonBreaking,
                    (false, true) => Core.Compatibility.NonBreaking,
                    (false, false) => Core.Compatibility.Breaking,
                };

                static string Explanation(SemanticChange c, bool req, bool became)
                {
                    c.Details.TryGetValue("property", out var prop);
                    prop ??= "(unknown)";
                    return req
                        ? became
                            ? $"Property '{prop}' was added as REQUIRED to the {DirectionWord(c.Direction)} schema of {Where(c)}."
                            : $"Property '{prop}' of {Where(c)} changed from required to optional."
                        : became
                            ? $"Property '{prop}' in the response of {Where(c)} is now documented as always present."
                            : $"Property '{prop}' is no longer a required member of the {StatusOrEmpty(c)}response of {Where(c)}.";
                }

                static string Impact(bool req, bool became) => (req, became) switch
                {
                    (true, true) => "Existing clients that do not send this property may begin receiving validation errors.",
                    (true, false) => "Non-breaking: requests that omitted the property are now accepted.",
                    (false, true) => "Non-breaking for readers: the server commits to always include the property.",
                    _ => "Clients that rely on this property being present will break when it is missing.",
                };

                static string Action(bool req, bool became) => (req, became) switch
                {
                    (true, true) => "Introduce the field as optional first, migrate consumers, then enforce the requirement in a later API version.",
                    (true, false) => "No action required; confirm server behaviour when omitted.",
                    (false, true) => "No action required; document the guarantee.",
                    _ => "Keep returning the property, or coordinate its removal with all known consumers.",
                };
            },
        };

        // ---------- property presence ----------
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5016", Title = "Optional property added",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.PropertyAdded],
                Description = "A new optional property appeared in a schema.",
                Impact = "Additive. Request schemas accept more; response schemas may contain new fields that tolerant readers ignore.",
                Remediation = "No action required for compatibility. Document the addition and ship it in a minor release.",
            },
            Build = c =>
            {
                c.Details.TryGetValue("property", out var prop);
                var isResponse = c.Direction == ApiDirection.Response;
                return FindingFactory.Create("CG5016", isResponse ? "Response property added" : "Optional property added",
                    Severity.Info, Compatibility.NonBreaking, c,
                    $"New property '{prop}' added to {Where(c)} ({(isResponse ? "clients should tolerate unknown fields" : "optional on request")}).",
                    isResponse
                        ? "Clients with strict deserializers may need updates; tolerant readers are unaffected."
                        : "Purely additive for senders.",
                    isResponse
                        ? "Confirm client deserializers ignore unknown fields."
                        : "No action required.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5017", Title = "Property removed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.PropertyRemoved],
                Description = "A property disappeared from a schema.",
                Impact = "Response side: readers depending on the field break. Request side: clients still sending it depend on server tolerance for unknown fields.",
                Remediation = "Deprecate the property first; remove only after consumer migration.",
                BreakingExample = "properties:\n  legacyId: {...}   # removed while clients read it",
                NonBreakingExample = "# removing a writeOnly property never exposed in responses",
            },
            Build = c =>
            {
                c.Details.TryGetValue("property", out var prop);
                var isResponse = c.Direction == ApiDirection.Response;
                return FindingFactory.Create("CG5017", isResponse ? "Response property removed" : "Request property removed",
                    isResponse ? Severity.High : Severity.Medium,
                    isResponse ? Compatibility.Breaking : Compatibility.PotentiallyBreaking, c,
                    $"Property '{prop}' was removed from {Where(c)}.",
                    isResponse
                        ? "Consumers reading this property will receive undefined data and may fail."
                        : "Clients still sending this property rely on the server tolerating unknown fields.",
                    isResponse
                        ? "Keep returning the property during a deprecation window before removing it."
                        : "Tolerate and ignore the property during migration, then reject in a major version.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5018", Title = "Property renamed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.PropertyRenamed],
                Description = "A property appears to have been renamed (same type and highly similar structure).",
                Impact = "Renames behave like remove+add for every consumer.",
                Remediation = "Prefer adding the new name and deprecating the old one so both exist during migration.",
                Limitations = "Rename detection is heuristic (same type + >=80% child-property overlap). When uncertain, Sentinel reports removal and addition separately.",
            },
            Build = c => FindingFactory.Create("CG5018", "Property renamed",
                Severity.Medium, Compatibility.Breaking, c,
                $"'{c.OldValue}' appears to have been renamed to '{c.NewValue}' in {Where(c)}.",
                "Every consumer referencing the old name stops working, exactly as with removal.",
                "Ship both names for a transition period; deprecate the old one."),
        };

        // ---------- type & format ----------
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5001", Title = "Schema type changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Critical,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.SchemaTypeChanged],
                Description = "The JSON type of a schema changed (string → number, object → array, ...).",
                Impact = "Breaks serialization/deserialization for every consumer in both directions.",
                Remediation = "Never change types in place; introduce a new field and version the API.",
                BreakingExample = "schema:\n  type: string   # -> type: integer",
            },
            Build = c =>
            {
                var numeric = DirectionLogic.ForNumericTypeChange(c.OldValue, c.NewValue, c.Direction);
                if (numeric is { } n)
                {
                    var (sev, comp, conf) = n;
                    return FindingFactory.Create("CG5001", "Schema type changed",
                        sev, comp, c,
                        $"Type changed at {Where(c)}: {RuleText.Values(c)}.",
                        comp == Compatibility.PotentiallyBreaking
                            ? "Responses may now include decimal values where integers were guaranteed."
                            : comp == Compatibility.Breaking
                                ? "Decimal values sent by clients will be rejected."
                                : "Relaxation: previously valid values remain valid.",
                        comp == Compatibility.NonBreaking
                            ? "No action required; document the widened type."
                            : "Introduce a new field with the new type instead of mutating this one.",
                        conf);
                }

                return FindingFactory.Create("CG5001", "Schema type changed",
                    Severity.Critical, Compatibility.Breaking, c,
                    $"Type changed at {Where(c)}: {RuleText.Values(c)}.",
                    "All consumers exchanging values of this schema will fail to serialize or validate.",
                    "Introduce a new field with the new type instead of mutating this one.");
            },
        };


        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5002", Title = "Schema format changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.SchemaFormatChanged],
                Description = "The format of a schema changed (date ↔ date-time, int32 ↔ int64, ...).",
                Impact = "Precision and parsing expectations shift; some clients will fail.",
                Remediation = "Widen formats deliberately and document precision/behaviour changes.",
            },
            Build = c =>
            {
                var (severity, compatibility, confidence) =
                    DirectionLogic.ForFormat(c.OldValue, c.NewValue, c.Direction);
                return FindingFactory.Create("CG5002", "Schema format changed",
                    severity, compatibility, c,
                    $"Format changed at {Where(c)}: {RuleText.Values(c)}.",
                    compatibility == Compatibility.Breaking
                        ? "Clients may send values the server can no longer represent (e.g. int64 values over int32)."
                        : "Precision and parsing expectations shift; some clients will fail.",
                    "Verify client handling; prefer widening formats with documentation.",
                    confidence);
            },
        };

        // ---------- enum / const ----------
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5003", Title = "Enum value removed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.EnumValueRemoved],
                Description = "An enum value disappeared from a schema. Direction-aware: request enums contract acceptance; response enums narrow possible outputs.",
                Impact = "Request: clients sending the value are rejected. Response: usually safe (fewer outputs), but enum-driven UIs lose cases.",
                Remediation = "Deprecate the value before removing it from accepted inputs.",
                BreakingExample = "enum: [red, green, blue]   # blue removed",
            },
            Build = c =>
            {
                var isRequest = c.Direction == ApiDirection.Request;
                return FindingFactory.Create("CG5003", isRequest ? "Enum value no longer accepted" : "Response enum value removed",
                    isRequest ? Severity.High : Severity.Low,
                    isRequest ? Compatibility.Breaking : Compatibility.NonBreaking, c,
                    $"Enum value {c.OldValue} removed at {Where(c)}.",
                    isRequest
                        ? "Requests using this value will be rejected by validation."
                        : "Narrowing possible outputs; readers are unaffected unless they depended on exhaustive cases.",
                    isRequest
                        ? "Keep accepting the value during a deprecation window."
                        : "Update documentation/UI enumerations accordingly.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5004", Title = "Enum value added",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.EnumValueAdded],
                Description = "A new enum value appeared. Direction-aware: accepting more input is safe; producing new outputs can break strict readers.",
                Impact = "Response: clients switching over enum values may hit unhandled cases. Request: purely additive.",
                Remediation = "Announce new response enum values; ensure consumers use default branches.",
                NonBreakingExample = "# request enum gains a value -> safe",
            },
            Build = c =>
            {
                var isResponse = c.Direction == ApiDirection.Response;
                return FindingFactory.Create("CG5004", isResponse ? "New response enum value" : "Enum value added",
                    isResponse ? Severity.Medium : Severity.Info,
                    isResponse ? Compatibility.PotentiallyBreaking : Compatibility.NonBreaking, c,
                    $"Enum value {c.NewValue} added at {Where(c)}.",
                    isResponse
                        ? "Strict consumers without default handling may fail on the new value."
                        : "Additive: server accepts more inputs.",
                    isResponse
                        ? "Verify consumers handle unknown enum values gracefully."
                        : "No action required.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5005", Title = "Const value changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.ConstChanged],
                Description = "A const-constrained value changed.",
                Impact = "Every payload/response using the old constant fails validation or expectations.",
                Remediation = "Version the change; do not mutate const values silently.",
            },
            Build = c => FindingFactory.Create("CG5005", "Const value changed",
                Severity.High, Compatibility.Breaking, c,
                $"Const changed at {Where(c)}: {RuleText.Values(c)}.",
                "Consumers expecting the previous fixed value will fail.",
                "Coordinate the change via a new versioned field or endpoint."),
        };

        // ---------- constraints (direction aware) ----------
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5006", Title = "Validation constraint changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.ConstraintChanged],
                Description = "A numeric/string/array/object constraint changed (minimum, maximum, minLength, maxLength, pattern, minItems, maxItems, uniqueItems, multipleOf, minProperties, maxProperties). Tightening request constraints rejects old payloads; widening response constraints produces new outputs readers must handle.",
                Impact = "Request tightening → 400 responses for previously valid data. Response widening → unexpected shapes/values for readers.",
                Remediation = "Relax request constraints freely; tighten them only across versions. Widen response ranges only after confirming reader tolerance.",
                BreakingExample = "# request\ntype: integer\nminimum: 0    # -> minimum: 1",
                NonBreakingExample = "# response\ntype: string\nmaxLength: 64 # -> maxLength: 128 (server may return longer)",
                Limitations = "pattern/multipleOf transitions cannot be classified automatically and yield Verify findings.",
            },
            Build = c =>
            {
                c.Details.TryGetValue("constraint", out var constraint);
                constraint ??= "(unknown)";
                var patternLike = constraint is "pattern" or "multipleOf";

                DirectionLogic.Impact impact;
                if (!patternLike &&
                    RuleText.TryGetNumber(c.OldValue, out var o) &&
                    RuleText.TryGetNumber(c.NewValue, out var n))
                {
                    impact = DirectionLogic.ClassifyNumeric(constraint, o, n);
                }
                else if (!patternLike && c.OldValue is null) impact = DirectionLogic.Impact.Tightened;
                else if (!patternLike && c.NewValue is null) impact = DirectionLogic.Impact.Relaxed;
                else impact = DirectionLogic.Impact.Uncertain;

                var isRequest = c.Direction == ApiDirection.Request;
                var (sev, compat) = isRequest
                    ? DirectionLogic.ForRequest(impact, patternLike)
                    : DirectionLogic.ForResponse(impact, patternLike);

                var impactText = c.Direction switch
                {
                    ApiDirection.Request => impact == DirectionLogic.Impact.Tightened
                        ? "Previously valid requests may now be rejected by validation."
                        : impact == DirectionLogic.Impact.Relaxed
                            ? "Constraint relaxed; previously valid requests remain valid."
                            : "Cannot determine automatically whether existing inputs remain valid.",
                    _ => impact == DirectionLogic.Impact.Relaxed
                        ? "Possible outputs widened; readers must handle the new range."
                        : impact == DirectionLogic.Impact.Tightened
                            ? "Output range narrowed; generally safe for readers."
                            : "Cannot determine automatically how outputs changed.",
                };

                return FindingFactory.Create("CG5006", "Validation constraint changed", sev, compat, c,
                    $"Constraint '{constraint}' changed at {Where(c)}: {RuleText.Values(c)}.",
                    impactText,
                    isRequest
                        ? (impact == DirectionLogic.Impact.Tightened
                            ? "Relax the constraint or enforce it only in a major version."
                            : "No immediate action; document the new bounds.")
                        : (impact == DirectionLogic.Impact.Relaxed
                            ? "Verify consumers handle the wider output range."
                            : "No action required."));
            },
        };

        // ---------- nullability ----------
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5007", Title = "Nullability changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.NullableChanged],
                Description = "Whether null is allowed changed. Request: nullable→non-nullable rejects null-sending clients. Response: non-nullable→nullable forces readers to handle nulls.",
                Impact = "Both directions can break clients that were not prepared for the new null behaviour.",
                Remediation = "Introduce nullability gradually; document which fields may be null.",
                BreakingExample = "# request\nnullable: true  # -> false\n\n# response\nnullable: false # -> true",
            },
            Build = c =>
            {
                var nowNullable = c.NewValue == "true";
                var isRequest = c.Direction == ApiDirection.Request;

                var (sev, compat) = (isRequest, nowNullable) switch
                {
                    (true, false) => (Severity.High, Compatibility.Breaking),       // null no longer accepted
                    (true, true) => (Severity.Info, Compatibility.NonBreaking),     // null now accepted
                    (false, true) => (Severity.Medium, Compatibility.PotentiallyBreaking), // responses may contain null
                    (false, false) => (Severity.Info, Compatibility.NonBreaking),   // outputs narrowed
                };

                return FindingFactory.Create("CG5007", "Nullability changed", sev, compat, c,
                    $"Nullability at {Where(c)} changed: {(nowNullable ? "null is now allowed" : "null is no longer allowed")}.",
                    (isRequest, nowNullable) switch
                    {
                        (true, false) => "Clients sending explicit null will be rejected.",
                        (true, true) => "Non-breaking relaxation for senders.",
                        (false, true) => "Responses may now contain null where they previously never did.",
                        _ => "Outputs narrowed; readers unaffected.",
                    },
                    (isRequest, nowNullable) switch
                    {
                        (true, false) => "Keep accepting null during a transition period.",
                        (true, true) => "No action required.",
                        (false, true) => "Ensure readers null-check this value.",
                        _ => "No action required.",
                    });
            },
        };

        // ---------- array items ----------
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5008", Title = "Array item schema changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.ItemsChanged],
                Description = "The element schema of an array changed. Element-level details are reported by their own rules (CG50xx), so this is a summary marker.",
                Impact = "Informational: the concrete element-level findings above/below carry the actual compatibility impact.",
                Remediation = "Review the detailed element schema findings; avoid changing item types in place.",
                Limitations = "Fired alongside the specific element-level rules; it intentionally does not classify impact itself to avoid double-counting.",
            },
            Build = c => FindingFactory.Create("CG5008", "Array item schema changed",
                Severity.Info, Compatibility.NonBreaking, c,
                $"Array items changed at {Where(c)}; see the detailed element-level findings for specifics.",
                "Summary only — the individual element changes are reported by their own rules.",
                "Review the element schema diff carefully."),
        };

        // ---------- additionalProperties ----------
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5009", Title = "additionalProperties changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.AdditionalPropertiesChanged],
                Description = "Whether extra properties are allowed (or how they are validated) changed. Closing an object rejects unknown fields sent by clients.",
                Impact = "Request side: introducing `false` rejects clients that send extension fields. Response side: removing `false` may surface previously impossible fields to strict readers.",
                Remediation = "Only restrict unknown fields in a major version after auditing real traffic; keep objects open while consumers send extensions.",
                BreakingExample = "type: object\nadditionalProperties: false   # was absent/open — request payloads with extras now fail",
                NonBreakingExample = "additionalProperties: false  →  true   # relaxation",
                Limitations = "Schema-valued additionalProperties transitions are reported as Verify: static analysis cannot tell whether real payloads still validate.",
            },
            Build = c =>
            {
                var oldV = c.OldValue;
                var newV = c.NewValue;
                var oldClosed = oldV == "false";
                var newClosed = newV == "false";
                var schemaInvolved = oldV == "<schema>" || newV == "<schema>";

                if (!oldClosed && !newClosed && schemaInvolved)
                {
                    // Openness retained, but extras are now (differently) validated.
                    return FindingFactory.Create("CG5009", "additionalProperties validation changed",
                        Severity.Verify, Compatibility.Unknown, c,
                        $"additionalProperties changed at {Where(c)}: {RuleText.Values(c)}.",
                        "Extra properties are still allowed but must now satisfy a schema (or vice versa); payload compatibility depends on the data.",
                        "Validate representative payloads against both definitions.",
                        Confidence.Uncertain);
                }

                if (newClosed && !oldClosed)
                {
                    return c.Direction == ApiDirection.Request
                        ? FindingFactory.Create("CG5009", "Objects closed to extra properties",
                            Severity.High, Compatibility.Breaking, c,
                            $"additionalProperties became false at {Where(c)} (was {oldV ?? "absent"}).",
                            "Clients sending undocumented fields will fail validation.",
                            "Audit consumer payloads before restricting unknown fields.")
                        : FindingFactory.Create("CG5009", "Response objects closed to extra properties",
                            Severity.Low, Compatibility.NonBreaking, c,
                            $"additionalProperties became false at {Where(c)} (was {oldV ?? "absent"}).",
                            "Server outputs become narrower; readers unaffected.",
                            "No action required for consumers.");
                }

                if (oldClosed && !newClosed)
                {
                    return c.Direction == ApiDirection.Request
                        ? FindingFactory.Create("CG5009", "Objects opened to extra properties",
                            Severity.Low, Compatibility.NonBreaking, c,
                            $"additionalProperties restriction removed at {Where(c)} ({oldV} → {newV ?? "absent"}).",
                            "Relaxation: previously valid requests remain valid.",
                            "No action required.")
                        : FindingFactory.Create("CG5009", "Response objects opened to extra properties",
                            Severity.Medium, Compatibility.PotentiallyBreaking, c,
                            $"additionalProperties restriction removed at {Where(c)} ({oldV} → {newV ?? "absent"}).",
                            "Responses may now contain fields strict client deserializers do not expect.",
                            "Confirm client readers tolerate unknown fields.");
                }

                return FindingFactory.Create("CG5009", "additionalProperties changed",
                    Severity.Verify, Compatibility.Unknown, c,
                    $"additionalProperties changed at {Where(c)}: {RuleText.Values(c)}.",
                    "The openness of the object changed in a way that could affect either side.",
                    "Review the change manually.",
                    Confidence.Uncertain);
            },
        };

        // ---------- composition ----------
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5010", Title = "Schema composition changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Verify,
                DefaultCompatibility = Compatibility.Unknown,
                ChangeKinds = [ChangeKinds.CompositionChanged],
                Description = "allOf/oneOf/anyOf/not membership changed structurally.",
                Impact = "Composition semantics are too varied to classify automatically; validity of existing payloads may change.",
                Remediation = "Review the composition diff manually against representative payloads.",
                Limitations = "Reported as Verify by design; Sentinel does not guess JSON Schema boolean-algebra outcomes.",
            },
            Build = c =>
            {
                c.Details.TryGetValue("composition", out var comp);
                return FindingFactory.Create("CG5010", "Schema composition changed",
                    Severity.Verify, Compatibility.Unknown, c,
                    $"'{comp}' composition changed at {Where(c)}.",
                    "Existing payloads may become valid or invalid depending on the exact algebraic effect.",
                    "Manually verify representative payloads against the new composition.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5011", Title = "Discriminator changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.DiscriminatorChanged],
                Description = "The discriminator (propertyName or mapping) of a polymorphic schema changed. Property changes and mapping removals break payload routing; purely additive mapping extensions do not.",
                Impact = "Clients dispatching on the discriminator will route payloads incorrectly when the property or existing mappings change.",
                Remediation = "Keep discriminator property names stable; extend mappings additively.",
            },
            Build = c =>
            {
                var aspect = c.Details.TryGetValue("aspect", out var a) ? a : "";
                c.Details.TryGetValue("mappingRemoved", out var removed);
                c.Details.TryGetValue("mappingAdded", out var added);
                var where = Where(c);

                // Purely additive mapping extension: existing payload routing untouched.
                if (aspect == "mapping" && string.IsNullOrEmpty(removed) && !string.IsNullOrEmpty(added))
                    return FindingFactory.Create("CG5011", "Discriminator mapping extended",
                        Severity.Low, Compatibility.NonBreaking, c,
                        $"New discriminator mapping(s) [{added}] were added at {where}.",
                        "Additive: existing variants keep routing exactly as before.",
                        "No action required; document the new variants.");

                // Mapping keys removed or mapping targets changed: consumers can no longer
                // resolve affected variants confidently.
                if (aspect == "mapping")
                    return FindingFactory.Create("CG5011", "Discriminator mapping changed",
                        Severity.Medium, Compatibility.PotentiallyBreaking, c,
                        $"Discriminator mapping changed at {where}."
                        + (string.IsNullOrEmpty(removed) ? "" : $" Removed: [{removed}].")
                        + (string.IsNullOrEmpty(added) ? "" : $" Added: [{added}]."),
                        "Consumers dispatching removed or retargeted variants may fail to deserialize payloads.",
                        "Keep removed mappings resolvable during a migration window; retarget variants additively.");

                return FindingFactory.Create("CG5011", "Discriminator changed",
                    Severity.High, Compatibility.Breaking, c,
                    $"Discriminator definition changed at {where}.",
                    "Polymorphic deserialization in client SDKs may break.",
                    "Preserve the discriminator property; only ever append new mappings.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5012", Title = "readOnly/writeOnly flag changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Low,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.ReadOnlyWriteOnlyChanged],
                Description = "The readOnly or writeOnly classification of a property changed.",
                Impact = "readOnly:true means clients must not send the field; flipping it can invalidate existing payloads.",
                Remediation = "Verify clients do not send readOnly fields nor expect writeOnly fields in outputs.",
            },
            Build = c =>
            {
                c.Details.TryGetValue("flag", out var flag);
                var flagName = flag ?? "readOnly";
                var nowOn = string.Equals(c.NewValue, "true", StringComparison.OrdinalIgnoreCase);
                var isRequest = c.Direction == ApiDirection.Request;

                // OpenAPI semantics: readOnly fields must not be SENT (matters on request
                // schemas); writeOnly fields must not be RETURNED (matters on response
                // schemas). The mismatched combinations have no effect on the wire.
                var restrictsClientSending = flagName == "readOnly" && isRequest;
                var restrictsServerReturning = flagName == "writeOnly" && !isRequest;

                if (!restrictsClientSending && !restrictsServerReturning)
                {
                    return FindingFactory.Create("CG5012", $"{flagName} flag changed",
                        Severity.Info, Compatibility.Informational, c,
                        $"'{flagName}' flag changed at {Where(c)}: {RuleText.Values(c)}.",
                        "This combination has no effect on requests or responses in this position.",
                        "No action required.",
                        Confidence.Certain);
                }

                if (restrictsClientSending)
                {
                    return nowOn
                        ? FindingFactory.Create("CG5012", "Clients may no longer send this property",
                            Severity.High, Compatibility.Breaking, c,
                            $"'{flagName}' became true at {Where(c)}: payloads containing it will be rejected.",
                            "Existing clients that send this field start failing validation.",
                            "Remove reliance on sending the field before enforcing.",
                            Confidence.Certain)
                        : FindingFactory.Create("CG5012", "Send restriction lifted",
                            Severity.Low, Compatibility.NonBreaking, c,
                            $"'{flagName}' was removed at {Where(c)}; sending is permitted again.",
                            "Relaxation: previously valid payloads remain valid.",
                            "No action required.",
                            Confidence.Certain);
                }

                // restrictsServerReturning (writeOnly on a response schema)
                return nowOn
                    ? FindingFactory.Create("CG5012", "Responses stop returning this property",
                        Severity.Medium, Compatibility.PotentiallyBreaking, c,
                        $"'{flagName}' became true at {Where(c)}: responses no longer include it.",
                        "Clients reading this field receive undefined data.",
                        "Keep returning the field during a deprecation window.",
                        Confidence.Likely)
                    : FindingFactory.Create("CG5012", "Return restriction lifted",
                        Severity.Low, Compatibility.NonBreaking, c,
                        $"'{flagName}' was removed at {Where(c)}; responses may include it again.",
                        "Relaxation for readers; strict readers should tolerate the field.",
                        "No action required.",
                        Confidence.Likely);
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5013", Title = "Schema deprecated",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.SchemaDeprecatedChanged],
                Description = "A schema (or property) was marked deprecated.",
                Impact = "Signals planned removal; not immediately breaking.",
                Remediation = "Publish replacement guidance and a sunset timeline.",
            },
            Build = c =>
            {
                var dep = string.Equals(c.NewValue, "true", StringComparison.OrdinalIgnoreCase);
                return FindingFactory.Create("CG5013", dep ? "Schema deprecated" : "Deprecation lifted",
                    dep ? Severity.Medium : Severity.Info,
                    dep ? Compatibility.PotentiallyBreaking : Compatibility.Informational, c,
                    $"deprecated flag changed at {Where(c)}: {RuleText.Values(c)}.",
                    dep ? "Consumers should migrate off this schema." : "No impact.",
                    dep ? "Document the replacement." : "No action required.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5014", Title = "Default value changed",
                Category = FindingCategory.Schema,
                DefaultSeverity = Severity.Verify,
                DefaultCompatibility = Compatibility.Unknown,
                ChangeKinds = [ChangeKinds.DefaultChanged],
                Description = "The default value of a property changed.",
                Impact = "Behavioural: consumers omitting the property observe different effective values.",
                Remediation = "Confirm the new default against downstream business logic.",
            },
            Build = c => FindingFactory.Create("CG5014", "Default value changed",
                Severity.Verify, Compatibility.Unknown, c,
                $"Default changed at {Where(c)}: {RuleText.Values(c)}.",
                "Consumers omitting this value experience different behaviour.",
                "Validate the new default with stakeholders."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG5019", Title = "Example changed",
                Category = FindingCategory.Metadata,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.Informational,
                ChangeKinds = [ChangeKinds.ExampleChanged],
                Description = "Documentation examples changed. No wire-format impact.",
                Impact = "None.",
                Remediation = "No action required.",
            },
            Build = c => FindingFactory.Create("CG5019", "Example changed",
                Severity.Info, Compatibility.Informational, c,
                $"Examples changed at {Where(c)}.",
                "Documentation-only change.",
                "No action required."),
        };
    }

    private static string StatusOrEmpty(SemanticChange c) =>
        c.Details.TryGetValue("statusCode", out var s) && s.Length > 0 ? s + " " : "";
}

using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.Rules;

/// <summary>Rules for authentication/security contract changes (CG60xx).</summary>
internal static class SecurityRules
{
    private static string Where(SemanticChange c) =>
        c.HttpMethod == HttpMethodKind.None ? c.Path : $"{c.HttpMethod.ToHttpString()} {c.Path}";

    public static IEnumerable<RuleDefinition> All()
    {
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6001", Title = "Authentication requirement added",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.SecurityRequirementAdded],
                Description = "An operation now requires authentication where it previously was public (or required fewer credentials).",
                Impact = "Clients that called this endpoint without credentials will start receiving 401/403 responses.",
                Remediation = "Introduce authentication in a major version, or keep a documented unauthenticated variant during a migration window.",
                BreakingExample = "security:\n  - bearerAuth: []   # added; endpoint was public before",
                NonBreakingExample = "# documenting optional alternatives alongside an existing requirement is additive only when no new mandatory scheme appears",
                Limitations = "Multiple alternatives are unioned conservatively: any newly required scheme in every alternative triggers this finding.",
            },
            Build = c => FindingFactory.Create("CG6001", "Endpoint now requires authentication",
                Severity.High, Compatibility.Breaking, c,
                $"Authentication became mandatory for {Where(c)} ({c.NewValue}).",
                "Unauthenticated clients will receive 401/403 responses and stop working.",
                "Migrate clients to authenticated calls before enforcing, or document a transition period."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6002", Title = "Authentication requirement removed",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.SecurityRequirementRemoved],
                Description = "A previously required authentication scheme is no longer documented for this operation.",
                Impact = "Usually intentional relaxation, but may expose data that clients assumed was protected.",
                Remediation = "Confirm the removal is intended; verify authorization is still enforced server-side where needed.",
                NonBreakingExample = "security: []   # endpoint becomes public deliberately",
            },
            Build = c => FindingFactory.Create("CG6002", "Authentication no longer required",
                Severity.Medium, Compatibility.PotentiallyBreaking, c,
                $"The security requirement ({c.OldValue}) was removed from {Where(c)}.",
                "Existing authenticated clients continue to work, but the endpoint may now be publicly accessible.",
                "Verify the endpoint is intentionally public and that sensitive data remains protected."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6003", Title = "OAuth scope added to requirement",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.OAuthScopeAdded],
                Description = "An OAuth scope was added to an effective security requirement, so tokens must now carry it.",
                Impact = "Clients holding tokens without the new scope will receive 403 responses.",
                Remediation = "Grant the scope to existing clients before enforcing it.",
                BreakingExample = "security:\n  - oauth2: [orders:write]   # 'orders:write' newly required",
            },
            Build = c => FindingFactory.Create("CG6003", "OAuth scope now required",
                Severity.Medium, Compatibility.Breaking, c,
                $"Scope '{c.NewValue}' is now required on {Where(c)}.",
                "Access tokens without this scope will be rejected with 403.",
                "Provision the scope for existing integrations before deploying the contract change."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6004", Title = "OAuth scope removed from requirement",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.Low,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.OAuthScopeRemoved],
                Description = "An OAuth scope is no longer demanded by this operation's security requirement.",
                Impact = "Relaxation; previously valid tokens remain valid. Access may become broader than intended.",
                Remediation = "Confirm the relaxed authorization model is intended.",
            },
            Build = c => FindingFactory.Create("CG6004", "OAuth scope no longer required",
                Severity.Low, Compatibility.NonBreaking, c,
                $"Scope '{c.OldValue}' is no longer required on {Where(c)}.",
                "Clients with fewer scopes can now call the endpoint; review whether the wider audience is acceptable.",
                "No action required if the relaxation is intentional."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6005", Title = "Security scheme removed",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.SecuritySchemeRemoved],
                Description = "A named security scheme disappeared from components.securitySchemes / securityDefinitions.",
                Impact = "Operations referencing the scheme lose their documented authentication definition; tooling and code generation break.",
                Remediation = "Keep the scheme while any operation references it; remove references first.",
                BreakingExample = "components:\n  securitySchemes:\n    apiKeyAuth: {...}   # removed",
            },
            Build = c => FindingFactory.Create("CG6005", "Security scheme removed",
                Severity.High, Compatibility.Breaking, c,
                $"Security scheme '{c.OldValue}' was removed.",
                "Any operation still referencing this scheme has undefined authentication semantics.",
                "Remove all references to the scheme before deleting its definition, or restore the definition."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6006", Title = "Security scheme added",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.Informational,
                ChangeKinds = [ChangeKinds.SecuritySchemeAdded],
                Description = "A new security scheme definition was added.",
                Impact = "Additive documentation unless an operation requires it (see CG6001).",
                Remediation = "No action required.",
            },
            Build = c => FindingFactory.Create("CG6006", "Security scheme added",
                Severity.Info, Compatibility.Informational, c,
                $"New security scheme '{c.NewValue}' was defined.",
                "Additive; affects compatibility only once operations require it.",
                "No action required."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6007", Title = "Security scheme type changed",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.SecuritySchemeTypeChanged],
                Description = "The type of a security scheme changed (e.g. apiKey → http, http → oauth2).",
                Impact = "Clients authenticate in a fundamentally different way; existing credentials stop working.",
                Remediation = "Define a new scheme under a new name and migrate consumers instead of mutating the type.",
                BreakingExample = "type: apiKey  →  type: http (bearer)",
            },
            Build = c => FindingFactory.Create("CG6007", "Security scheme type changed",
                Severity.High, Compatibility.Breaking, c,
                $"Scheme '{c.Path}' type changed: {RuleText.Values(c)}.",
                "Clients using the old authentication mechanism will fail.",
                "Introduce a new scheme name and versioned migration instead of changing the type in place."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6008", Title = "API key location changed",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.ApiKeyLocationChanged],
                Description = "The in (header/query/cookie) of an apiKey scheme changed.",
                Impact = "Clients sending the key in the old location will be rejected.",
                Remediation = "Accept the key in both locations during migration.",
                BreakingExample = "in: header  →  in: query",
            },
            Build = c => FindingFactory.Create("CG6008", "API key location changed",
                Severity.High, Compatibility.Breaking, c,
                $"API key '{c.Path}' moved: {RuleText.Values(c)}.",
                "Clients still sending the key via the previous location receive 401 responses.",
                "Support both locations during a deprecation window."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6015", Title = "API key name changed",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.ApiKeyNameChanged],
                Description = "The documented name of an apiKey (the header/query/cookie parameter carrying the key) changed.",
                Impact = "Clients send the key under the old name; the server will not find it and rejects the request as unauthenticated.",
                Remediation = "Accept both names during migration, or version the scheme under a new name.",
                BreakingExample = "name: X-API-Key  →  name: X-Api-Token",
                Limitations = "Only applies to schemes of type apiKey.",
            },
            Build = c => FindingFactory.Create("CG6015", "API key name changed",
                Severity.High, Compatibility.Breaking, c,
                $"API key parameter name for '{c.Path}' changed: {RuleText.Values(c)}.",
                "Requests authenticated with the old key name will be treated as unauthenticated.",
                "Accept the old and new key names during a deprecation window."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6009", Title = "HTTP security scheme changed",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.HttpSchemeChanged],
                Description = "The scheme of an HTTP security scheme changed (e.g. basic → bearer).",
                Impact = "The wire format of credentials changes; clients must send different headers.",
                Remediation = "Version the scheme change and support the old format during migration.",
                BreakingExample = "scheme: basic  →  scheme: bearer",
            },
            Build = c => FindingFactory.Create("CG6009", "HTTP authentication scheme changed",
                Severity.High, Compatibility.Breaking, c,
                $"HTTP scheme for '{c.Path}' changed: {RuleText.Values(c)}.",
                "Authorization headers built for the old scheme will be rejected.",
                "Support both schemes during migration; communicate credential changes early."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6010", Title = "Bearer format changed",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.Low,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.BearerFormatChanged],
                Description = "The bearerFormat hint of an HTTP bearer scheme changed.",
                Impact = "Documentation-level hint, but may signal a token-format switch (e.g. JWT → opaque).",
                Remediation = "Confirm token issuance actually unchanged; update client token handling if not.",
            },
            Build = c => FindingFactory.Create("CG6010", "Bearer format changed",
                Severity.Low, Compatibility.PotentiallyBreaking, c,
                $"bearerFormat for '{c.Path}' changed: {RuleText.Values(c)}.",
                "If the actual token format changed, clients parsing tokens (e.g. JWT claims) may fail.",
                "Verify the runtime token format; update documentation and client parsers accordingly."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6011", Title = "OpenID Connect URL changed",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.OpenIdConnectUrlChanged],
                Description = "The openIdConnectUrl of a scheme changed.",
                Impact = "Discovery metadata moves; clients resolving scopes/discovery against the old URL break when it is retired.",
                Remediation = "Keep serving the old discovery document during migration.",
            },
            Build = c => FindingFactory.Create("CG6011", "OpenID Connect URL changed",
                Severity.Medium, Compatibility.PotentiallyBreaking, c,
                $"openIdConnectUrl for '{c.Path}' changed: {RuleText.Values(c)}.",
                "Clients discovering configuration at the old URL fail once it is decommissioned.",
                "Serve both discovery endpoints during migration."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6012", Title = "OAuth flow changed",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.OAuthFlowUrlChanged],
                Description = "An OAuth flow was added or removed, or an authorization/token/refresh URL of a flow changed.",
                Impact = "Removed flows break clients using that grant type; changed URLs break clients configured with the old endpoints.",
                Remediation = "Keep removed flows and old URLs reachable during a documented migration window.",
            },
            Build = c =>
            {
                c.Details.TryGetValue("flow", out var flow);
                c.Details.TryGetValue("urlKind", out var urlKind);
                var schemeName = c.Details.TryGetValue("scheme", out var s) && !string.IsNullOrEmpty(s) ? s : "(scheme)";

                if (urlKind == "flow.added")
                    return FindingFactory.Create("CG6012", "OAuth flow added",
                        Severity.Low, Compatibility.NonBreaking, c,
                        $"New OAuth flow '{flow}' was documented on security scheme '{schemeName}'.",
                        "Additive capability; existing clients are unaffected.",
                        "No action required; document the new grant option for integrators.");

                if (urlKind == "flow.removed")
                    return FindingFactory.Create("CG6012", "OAuth flow removed",
                        Severity.Medium, Compatibility.PotentiallyBreaking, c,
                        $"OAuth flow '{flow}' was removed from security scheme '{schemeName}'.",
                        "Clients authenticating through this grant flow lose their documented integration path.",
                        "Announce the removal and keep the flow available during a migration window.");

                return FindingFactory.Create("CG6012", "OAuth flow URL changed",
                    Severity.Medium, Compatibility.PotentiallyBreaking, c,
                    $"{urlKind} URL of flow '{flow}' on security scheme '{schemeName}' changed: {RuleText.Values(c)}.",
                    "Clients configured with the old URL fail once it is retired.",
                    "Keep the old endpoint reachable (redirect) during a deprecation window.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6013", Title = "OAuth scope definition removed",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.OAuthFlowScopeDefRemoved],
                Description = "A documented OAuth scope definition was removed from a flow.",
                Impact = "Scope no longer officially exists; clients requesting it may be denied or behave inconsistently.",
                Remediation = "Remove the scope from client configurations and requirements before deleting its definition.",
            },
            Build = c =>
            {
                c.Details.TryGetValue("flow", out var flow);
                return FindingFactory.Create("CG6013", "OAuth scope definition removed",
                    Severity.Medium, Compatibility.PotentiallyBreaking, c,
                    $"Scope definition '{c.OldValue}' was removed from flow '{flow}'.",
                    "Token requests for this scope may fail; requirements referencing it become invalid.",
                    "Retire usages of the scope before removing its definition.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG6014", Title = "OAuth scope definition added",
                Category = FindingCategory.Security,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.OAuthFlowScopeDefAdded],
                Description = "A new OAuth scope definition was documented in a flow.",
                Impact = "Additive documentation.",
                Remediation = "No action required.",
            },
            Build = c =>
            {
                c.Details.TryGetValue("flow", out var flow);
                return FindingFactory.Create("CG6014", "OAuth scope definition added",
                    Severity.Info, Compatibility.NonBreaking, c,
                    $"New scope definition '{c.NewValue}' in flow '{flow}'.",
                    "Additive; no existing behaviour changes.",
                    "No action required.");
            },
        };
    }
}

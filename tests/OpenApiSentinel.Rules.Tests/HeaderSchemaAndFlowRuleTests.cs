using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Rules.Tests;

/// <summary>
/// Rule-level classification for response-header payload changes (CG50xx with
/// Response direction) and OAuth flow add/remove events (CG6012 branches).
/// </summary>
public class HeaderSchemaAndFlowRuleTests
{
    private static IReadOnlyList<Finding> Evaluate(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings;
    }

    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    private const string HeaderOld = """
        openapi: 3.0.3
        info:
          title: Orders API
          version: "1.0"
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
                  headers:
                    X-Total-Count:
                      schema:
                        type: integer
        """;

    [Fact]
    public void Header_TypeWidening_OnResponse_IsPotentiallyBreaking_CG5001()
    {
        // integer -> number on a RESPONSE widens what clients may receive:
        // direction-aware classification is PotentiallyBreaking Medium.
        string newSpec = HeaderOld.Replace("type: integer", "type: number");

        var findings = Evaluate(HeaderOld, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG5001");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
        Assert.Contains("X-Total-Count", f.Location);
    }

    [Fact]
    public void Header_ConstraintTightening_OnResponse_Surfaces()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Orders API
          version: "1.0"
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
                  headers:
                    X-Request-Id:
                      schema:
                        type: string
                        maxLength: 10
        """;

        var findings = Evaluate(newSpec.Replace("maxLength: 10", ""), newSpec);
        Assert.Contains(findings, f => f.RuleId == "CG5006" && f.Location.Contains("X-Request-Id"));
    }

    [Fact]
    public void Flow_Added_Fires_CG6012_NonBreaking_Low()
    {
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Authed API
          version: "1.0"
        security:
          - oauth: []
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
        components:
          securitySchemes:
            oauth:
              type: oauth2
              flows:
                implicit:
                  authorizationUrl: https://example.com/authorize
                  scopes:
                    read: Read access
        """;
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Authed API
          version: "1.0"
        security:
          - oauth: []
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
        components:
          securitySchemes:
            oauth:
              type: oauth2
              flows:
                clientCredentials:
                  tokenUrl: https://example.com/token
                  scopes:
                    read: Read access
                implicit:
                  authorizationUrl: https://example.com/authorize
                  scopes:
                    read: Read access
        """;

        var findings = Evaluate(oldSpec, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG6012");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
        Assert.Equal("OAuth flow added", f.Title);
        Assert.Contains("clientCredentials", f.Explanation);
    }

    [Fact]
    public void Flow_Removed_Fires_CG6012_PotentiallyBreaking_Medium()
    {
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Authed API
          version: "1.0"
        security:
          - oauth: []
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
        components:
          securitySchemes:
            oauth:
              type: oauth2
              flows:
                implicit:
                  authorizationUrl: https://example.com/authorize
                  scopes:
                    read: Read access
                password:
                  tokenUrl: https://example.com/token
                  scopes:
                    read: Read access
        """;

        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Authed API
          version: "1.0"
        security:
          - oauth: []
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
        components:
          securitySchemes:
            oauth:
              type: oauth2
              flows:
                implicit:
                  authorizationUrl: https://example.com/authorize
                  scopes:
                    read: Read access
        """;

        var findings = Evaluate(oldSpec, newSpec);

        var f = Assert.Single(findings, x => x.RuleId == "CG6012");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
        Assert.Equal("OAuth flow removed", f.Title);
    }
}

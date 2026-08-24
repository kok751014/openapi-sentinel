using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;
using OpenApiSentinel.Rules;

namespace OpenApiSentinel.Rules.Tests;

/// <summary>
/// Regression tests for enum/const constraint introduction and removal semantics:
/// introducing an enum/const narrows acceptance (breaking on requests), dropping one
/// widens it (safe on requests; potentially breaking on responses). Found during the
/// final release audit — previously these transitions were misclassified as per-value
/// additions/removals or silently ignored.
/// </summary>
public class EnumConstConstraintRuleTests
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

    private const string FreeFormRequest = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            post:
              operationId: create
              requestBody:
                content:
                  application/json:
                    schema:
                      type: object
                      properties:
                        name: {type: string}
                        kind: {type: string}
              responses:
                '201': {description: created}
        """;

    private const string ConstrainedRequest = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            post:
              operationId: create
              requestBody:
                content:
                  application/json:
                    schema:
                      type: object
                      properties:
                        name: {type: string, enum: [a, b]}
                        kind: {type: string}
              responses:
                '201': {description: created}
        """;

    private const string PinnedRequest = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            post:
              operationId: create
              requestBody:
                content:
                  application/json:
                    schema:
                      type: object
                      properties:
                        name: {type: string}
                        kind: {type: string, const: legacy}
              responses:
                '201': {description: created}
        """;

    [Fact]
    public void RequestEnumIntroduced_IsBreaking()
    {
        var findings = Evaluate(FreeFormRequest, ConstrainedRequest);
        Assert.Contains(findings, f => f.RuleId == "CG5023" && f.Compatibility == Compatibility.Breaking);
        Assert.DoesNotContain(findings, f => f.RuleId == "CG5004");
    }

    [Fact]
    public void RequestEnumDropped_IsNotBreaking()
    {
        var findings = Evaluate(ConstrainedRequest, FreeFormRequest);
        Assert.Contains(findings, f => f.RuleId == "CG5024" && f.Compatibility == Compatibility.NonBreaking);
        Assert.DoesNotContain(findings, f => f.RuleId == "CG5003" && f.Compatibility == Compatibility.Breaking);
    }

    [Fact]
    public void RequestConstIntroduced_IsBreaking()
    {
        var findings = Evaluate(FreeFormRequest, PinnedRequest);
        Assert.Contains(findings, f => f.RuleId == "CG5025" && f.Compatibility == Compatibility.Breaking);
    }

    [Fact]
    public void RequestConstDropped_IsNotBreaking()
    {
        var findings = Evaluate(PinnedRequest, FreeFormRequest);
        Assert.Contains(findings, f => f.RuleId == "CG5026" && f.Compatibility == Compatibility.NonBreaking);
        Assert.DoesNotContain(findings, f => f.RuleId == "CG5005");
    }

    [Fact]
    public void ResponseEnumIntroduced_IsNotBreaking()
    {
        // Response narrowing mirrors CG5003's response posture: low severity, non-breaking.
        var findings = Evaluate(PinnedRequest, FreeFormRequest)
            .Where(f => f.RuleId is "CG5023" or "CG5024").ToList();
        Assert.All(findings, f => Assert.NotEqual(Compatibility.Breaking, f.Compatibility));
    }

    [Fact]
    public void ParameterEnumIntroduced_IsBreaking()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: list
                  parameters:
                    - name: mode
                      in: query
                      schema: {type: string}
                  responses:
                    '200': {description: ok}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: list
                  parameters:
                    - name: mode
                      in: query
                      schema: {type: string, enum: [fast, slow]}
                  responses:
                    '200': {description: ok}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        Assert.Contains(findings, f => f.RuleId == "CG2019" && f.Compatibility == Compatibility.Breaking);

        // Dropping it again is a relaxation and must not cry wolf.
        var reverse = Evaluate(newSpec, oldSpec);
        Assert.DoesNotContain(reverse, f => f.RuleId is "CG2007" or "CG2019");
    }
}

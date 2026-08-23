using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Rules.Tests;

/// <summary>
/// Callback rules CG1011–CG1013: out-of-band server→client contract changes.
/// </summary>
public class CallbackRuleTests
{
    private const string OldSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      responses:
                        '200':
                          description: received
        """;

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

    [Fact]
    public void Callback_Added_Fires_CG1011_PotentiallyBreaking()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      responses:
                        '200':
                          description: received
                shipmentEvent:
                  '{$request.body#/shipmentUrl}':
                    post:
                      responses:
                        '200':
                          description: received
        """;

        var findings = Evaluate(OldSpec, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG1011");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
        Assert.Contains("shipmentEvent", f.Explanation);
    }

    [Fact]
    public void Callback_Removed_Fires_CG1012_NonBreaking()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
        """;

        var findings = Evaluate(OldSpec, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG1012");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
    }

    [Fact]
    public void Expression_Changed_Fires_CG1013_Breaking_High()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/notificationUrl}':
                    post:
                      responses:
                        '200':
                          description: received
        """;

        var findings = Evaluate(OldSpec, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG1013");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(Compatibility.Breaking, f.Compatibility);
        Assert.Contains("notificationUrl", f.NewValue);
    }

    [Fact]
    public void Payload_Change_Inside_Callback_Surfaces_With_Callback_Path()
    {
        // A required property added to the callback payload must surface through the
        // existing schema rules with the callback path visible for suppression/glob use.
        const string oldPayload = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      requestBody:
                        content:
                          application/json:
                            schema:
                              type: object
                              properties:
                                signature:
                                  type: string
                      responses:
                        '200':
                          description: received
        """;
        const string newPayload = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      requestBody:
                        content:
                          application/json:
                            schema:
                              type: object
                              required:
                                - signature
                              properties:
                                signature:
                                  type: string
                      responses:
                        '200':
                          description: received
        """;

        var findings = Evaluate(oldPayload, newPayload);
        var req = Assert.Single(findings, f => f.RuleId == "CG5015");
        Assert.Contains("<callback:", req.Path);
    }
}

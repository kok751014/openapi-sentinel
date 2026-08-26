using System.Text.Json.Nodes;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>Blocker C — UUID/format query/path params must not waste slots on redundant
/// "wrong type" scenarios; the invalid-format scenario remains.</summary>
public class UuidWrongTypeSuppressionTests
{
    private static Operation OperationWithUuidParameter(string location) =>
        ReleaseBlockerSpec.Parse($$"""
          openapi: 3.0.0
          info: { title: t, version: "1" }
          paths:
            /items/{id}:
              get:
                operationId: getItem
                parameters:
                  - name: id
                    in: {{location}}
                    required: true
                    schema: { type: string, format: uuid }
          """).SingleOperation();

    [Fact]
    public void Uuid_Path_Does_Not_Generate_Redundant_Wrong_Type()
    {
        var scenarios = ReleaseBlockerSpec.GenerateFor(OperationWithUuidParameter("path"));
        Assert.DoesNotContain(scenarios, s => s.Category == ScenarioCategory.WrongType);
    }

    [Fact]
    public void Uuid_Query_Does_Not_Generate_Redundant_Wrong_Type()
    {
        var scenarios = ReleaseBlockerSpec.GenerateFor(OperationWithUuidParameter("query"));
        Assert.DoesNotContain(scenarios, s => s.Category == ScenarioCategory.WrongType);
    }

    [Fact]
    public void Uuid_Invalid_Format_Scenario_Still_Exists()
    {
        var scenarios = ReleaseBlockerSpec.GenerateFor(OperationWithUuidParameter("path"));
        Assert.Contains(scenarios, s => s.Category == ScenarioCategory.InvalidFormat && s.Name == "失敗－id 格式不正確");
    }

    [Fact]
    public void Wrong_Type_Remains_Meaningful_For_Integer_Query_Parameters()
    {
        // Integer query params genuinely exercise parse failures — wrong-type stays.
        var op = ReleaseBlockerSpec.Parse("""
            openapi: 3.0.0
            info: { title: t, version: "1" }
            paths:
              /items:
                get:
                  operationId: listItems
                  parameters:
                    - name: page
                      in: query
                      schema: { type: integer }
                  responses:
                    '200': { description: OK }
          """).SingleOperation();
        var scenarios = ReleaseBlockerSpec.GenerateFor(op);
        Assert.Contains(scenarios, s => s.Category == ScenarioCategory.WrongType);
    }

    [Fact]
    public void Duplicate_Equivalent_Scenarios_Are_DeDuplicated_And_Priority_Is_Ordered()
    {
        // quantity sample equals its declared minimum → the boundary-min request would be
        // byte-identical to success-full and must NOT earn a second slot.
        var doc = ReleaseBlockerSpec.Parse("""
            openapi: 3.0.0
            info: { title: t, version: "1" }
            paths:
              /x:
                post:
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          properties:
                            quantity:
                              type: integer
                              minimum: 1
                              maximum: 100
                            name:
                              type: string
                              minLength: 2
                              maxLength: 10
                              enum: [aa, bb]
            """);
        var op = doc.SingleOperation();
        var snapshot = BrunoSyncEngine.BuildScenarioBaseSnapshot(op, "{{baseUrl}}");
        Assert.Equal(1, JsonNode.Parse(snapshot.JsonBody!)!["quantity"]!.GetValue<int>()); // sample == minimum

        var scenarios = new ScenarioGenerator(new BrunoExampleSettings { MaxPerOperation = 32 })
            .Generate(OperationIdentity.From(op), op, snapshot);

        // Priority order: success-full → success-required-only → …
        Assert.Equal("success-full", scenarios[0].Id);
        Assert.DoesNotContain(scenarios, s => s.Id == "boundary-min-quantity"); // duplicate-equivalent
        Assert.True(scenarios.Select(s => s.Category).ToList().IndexOf(ScenarioCategory.BoundaryMaximum) <
                    scenarios.Select(s => s.Category).ToList().IndexOf(ScenarioCategory.AboveMaximum),
            "boundary constraints outrank off-boundary violations");
    }
}

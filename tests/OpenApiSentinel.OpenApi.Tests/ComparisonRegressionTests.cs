using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// Regression tests for comparison-engine correctness bugs found during sample-spec testing:
/// schema-comparer state leakage across calls, global-security inheritance, and
/// parameter sub-change locations.
/// </summary>
public class ComparisonRegressionTests
{
    private const string GlobalSecurityBase = """
        openapi: 3.0.3
        info:
          title: Secured API
          version: "1.0"
        security:
          - bearerAuth: []
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
          /health:
            get:
              operationId: health
              responses:
                '200':
                  description: ok
        components:
          securitySchemes:
            bearerAuth:
              type: http
              scheme: bearer
        """;

    [Fact]
    public void SchemaComparer_Reuse_DoesNotLeakChangesAcrossCalls()
    {
        // Two independent schema comparisons with the same comparer instance must each
        // return only their own changes (regression: shared _changes list accumulated).
        var comparer = new SchemaComparer();
        static SchemaInfo Info(string json) => new()
        {
            Node = System.Text.Json.Nodes.JsonNode.Parse(json)!,
            Spec = SpecVersion.OpenApi30,
        };

        var first = comparer.Compare(Info("""{"type":"string","minLength":1}"""), Info("""{"type":"string","minLength":5}"""),
            "/a", HttpMethodKind.Get, ApiDirection.Request, "loc-a");
        var second = comparer.Compare(Info("""{"type":"integer"}"""), Info("""{"type":"boolean"}"""),
            "/b", HttpMethodKind.Get, ApiDirection.Request, "loc-b");

        Assert.Single(first);
        Assert.Equal("schema.constraint.changed", first[0].Kind);
        Assert.Single(second);
        Assert.Equal("schema.type.changed", second[0].Kind);
    }

    [Fact]
    public void OperationSecurity_Null_InheritsGlobalSecurity()
    {
        // Old: global bearer auth, operations inherit. New: identical doc.
        // No security findings may appear (inheritance keeps effective security equal).
        var changes = TestSpecHelper.Compare(GlobalSecurityBase, GlobalSecurityBase);
        Assert.DoesNotContain(changes, c => c.Kind.StartsWith("security.", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationSecurity_Removed_BecomesPublic_AndIsReported()
    {
        const string newSpec = """
            openapi: 3.0.3
            info:
              title: Secured API
              version: "2.0"
            security:
              - bearerAuth: []
            paths:
              /orders:
                get:
                  operationId: listOrders
                  security: []
                  responses:
                    '200':
                      description: ok
              /health:
                get:
                  operationId: health
                  responses:
                    '200':
                      description: ok
            components:
              securitySchemes:
                bearerAuth:
                  type: http
                  scheme: bearer
            """;
        var changes = TestSpecHelper.Compare(GlobalSecurityBase, newSpec);
        var removed = changes.Where(c => c.Kind == ChangeKinds.SecurityRequirementRemoved).ToList();
        var change = Assert.Single(removed);
        Assert.Equal("/orders", change.Path);
        Assert.Equal(HttpMethodKind.Get, change.HttpMethod);
    }

    [Fact]
    public void ParameterConstraintChange_CarriesParameterLocation()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: pageSize
                      in: query
                      schema:
                        type: integer
                        maximum: 100
                  responses:
                    '200':
                      description: ok
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: pageSize
                      in: query
                      schema:
                        type: integer
                        maximum: 50
                  responses:
                    '200':
                      description: ok
            """;
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        var constraint = Assert.Single(changes, c => c.Kind == ChangeKinds.ParameterConstraintChanged);
        Assert.Contains("parameters.pageSize", constraint.Location, StringComparison.Ordinal);
    }

    [Fact]
    public void Parameter_LocationMove_IsReportedAsLocationChanged_NotRemoveAdd()
    {
        // Regression: parameters were keyed by "in:name", so a same-named parameter moving
        // between locations degraded into parameter.removed + parameter.added and rule
        // CG2015 (parameter.location.changed) could never fire.
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: traceId
                      in: query
                      schema: {type: string}
                  responses:
                    '200':
                      description: ok
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: traceId
                      in: header
                      schema: {type: string}
                  responses:
                    '200':
                      description: ok
            """;
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        var move = Assert.Single(changes);
        Assert.Equal(ChangeKinds.ParameterLocationChanged, move.Kind);
        Assert.Equal("Query", move.OldValue, ignoreCase: true);
        Assert.Equal("Header", move.NewValue, ignoreCase: true);
        Assert.DoesNotContain(changes, c => c.Kind is ChangeKinds.ParameterRemoved or ChangeKinds.ParameterAdded);
    }

    [Fact]
    public void Parameter_AmbiguousMove_FallsBackToRemoveAdd()
    {
        // One old location, two same-named candidates in the new spec: the pairing is
        // ambiguous, so the engine must keep the conservative removed/added reporting.
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: traceId
                      in: query
                      schema: {type: string}
                  responses:
                    '200':
                      description: ok
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: traceId
                      in: header
                      schema: {type: string}
                    - name: traceId
                      in: cookie
                      schema: {type: string}
                  responses:
                    '200':
                      description: ok
            """;
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.DoesNotContain(changes, c => c.Kind == ChangeKinds.ParameterLocationChanged);
        Assert.Single(changes, c => c.Kind == ChangeKinds.ParameterRemoved);
        Assert.Equal(2, changes.Count(c => c.Kind == ChangeKinds.ParameterAdded));
    }

    [Fact]
    public void Parameter_UnrelatedRemoveAndAdd_StillReportedSeparately()
    {
        // Different names must not be paired as a relocation.
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: page
                      in: query
                      schema: {type: integer}
                  responses:
                    '200':
                      description: ok
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: limit
                      in: query
                      schema: {type: integer}
                  responses:
                    '200':
                      description: ok
            """;
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.DoesNotContain(changes, c => c.Kind == ChangeKinds.ParameterLocationChanged);
        Assert.Single(changes, c => c.Kind == ChangeKinds.ParameterRemoved);
        Assert.Single(changes, c => c.Kind == ChangeKinds.ParameterAdded);
    }

    [Fact]
    public void SingleBranchAllOf_Wrap_DoesNotRegisterTypeChange()
    {
        // Regression: {type: string} restructured into {allOf: [{type: string}]} was
        // reported as "Schema type changed" (critical) because only the local `type`
        // keyword was inspected. A single-branch composition equals its branch.
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              id: {type: string}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              id:
                                allOf:
                                  - type: string
            """;
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.DoesNotContain(changes, c => c.Kind == ChangeKinds.SchemaTypeChanged);
    }

    [Fact]
    public void SingleBranchAllOf_RealTypeChange_StillDetected()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              id: {type: string}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              id:
                                allOf:
                                  - type: integer
            """;
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.Contains(changes, c => c.Kind == ChangeKinds.SchemaTypeChanged);
    }

    [Fact]
    public void MultiBranchAllOf_NoLocalType_DoesNotInventTypeChange()
    {
        // Ambiguous effective type (intersection semantics) must stay unreported,
        // matching pre-existing behavior for schemas without a declared type.
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              id: {type: string}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              id:
                                allOf:
                                  - type: string
                                    maxLength: 5
                                  - type: string
                                    pattern: "^a"
            """;
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.DoesNotContain(changes, c => c.Kind == ChangeKinds.SchemaTypeChanged);
    }

    [Fact]
    public void EnumIntroduced_IsConstraintIntroduction_NotValueAddition()
    {
        // Regression: an enum appearing where none existed is a narrowing (restriction),
        // not an expansion. It must surface once as schema.enum.constraint.introduced,
        // not as per-value "enum value added" changes.
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              id: {type: string}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              id: {type: string, enum: [x, y]}
            """;
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        var introduced = Assert.Single(changes, c => c.Kind == ChangeKinds.EnumConstraintIntroduced);
        Assert.Contains("x", introduced.NewValue, StringComparison.Ordinal);
        Assert.DoesNotContain(changes, c => c.Kind == ChangeKinds.EnumValueAdded);
        Assert.DoesNotContain(changes, c => c.Kind == ChangeKinds.EnumValueRemoved);
    }

    [Fact]
    public void EnumDropped_IsConstraintRemoval_NotPerValueRemovals()
    {
        // Regression: dropping the whole enum widens acceptance; it must not be reported
        // as N separate high-severity "enum value removed" findings.
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              id: {type: string, enum: [x, y]}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              id: {type: string}
            """;
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.Single(changes, c => c.Kind == ChangeKinds.EnumConstraintRemoved);
        Assert.DoesNotContain(changes, c => c.Kind == ChangeKinds.EnumValueRemoved);
        Assert.DoesNotContain(changes, c => c.Kind == ChangeKinds.EnumValueAdded);
    }

    [Fact]
    public void ParameterEnumIntroduced_EmitsParameterConstraintKind()
    {
        // Regression: parameter enums were only compared when both sides declared one;
        // introducing one silently restricted what clients may send.
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: mode
                      in: query
                      schema: {type: string}
                  responses:
                    '200':
                      description: ok
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: mode
                      in: query
                      schema: {type: string, enum: [fast, slow]}
                  responses:
                    '200':
                      description: ok
            """;
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.Single(changes, c => c.Kind == ChangeKinds.ParameterEnumConstraintIntroduced);

        var reverse = TestSpecHelper.Compare(newSpec, oldSpec);
        Assert.DoesNotContain(reverse, c => c.Kind == ChangeKinds.ParameterEnumValueRemoved);
    }
}

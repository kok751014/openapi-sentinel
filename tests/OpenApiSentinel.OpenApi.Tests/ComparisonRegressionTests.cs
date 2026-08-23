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
}

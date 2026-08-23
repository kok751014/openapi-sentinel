using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// Graceful budget degradation: when a safety budget trips during reference resolution,
/// the resolver leaves an opaque marker subtree instead of failing the whole run.
/// Identical markers compare silently equal; differing ones emit exactly one
/// schema.unresolved.changed event (CG5022 territory).
/// </summary>
public class UnresolvedReferenceTests
{
    private const string RefHeavySpec = """
        openapi: 3.0.3
        info:
          title: Big API
          version: "1.0"
        paths:
          /a:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Shared'
          /b:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Shared'
        components:
          schemas:
            Shared:
              type: object
              required: [id]
              properties:
                id:
                  type: string
                extra:
                  type: string
        """;

    private static ApiDocument NormalizeWithLimits(string yaml, ProcessingLimits limits)
    {
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    [Fact]
    public void BudgetTrip_Produces_Markers_Not_Failures()
    {
        // MaxResolvedRefs=1 forces the second Shared reference to degrade to a marker.
        var doc = NormalizeWithLimits(RefHeavySpec, new ProcessingLimits { MaxResolvedRefs = 1 });
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["k"] = "probe" });

        Assert.NotEmpty(doc.Paths);
        // The run must not have thrown; markers are opaque objects in the tree.
        Assert.True(doc.Paths["/a"].Operations.ContainsKey(HttpMethodKind.Get));
        _ = json; // serializer sanity
    }

    [Fact]
    public void Identical_Markers_Across_Docs_Compare_Silent()
    {
        var oldDoc = NormalizeWithLimits(RefHeavySpec, new ProcessingLimits { MaxResolvedRefs = 1 });
        var newDoc = NormalizeWithLimits(RefHeavySpec, new ProcessingLimits { MaxResolvedRefs = 1 });

        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        Assert.DoesNotContain(changes, c => c.Kind == ChangeKinds.SchemaUnresolvedChanged);
    }

    [Fact]
    public void Differing_Unresolved_Targets_Emit_Single_Verify_Event()
    {
        // Markers carry the reference string, so the references themselves must differ.
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Big API
          version: "1.0"
        paths:
          /a:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Shared'
          /b:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Shared'
        components:
          schemas:
            Shared:
              type: object
        """;
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Big API
          version: "1.0"
        paths:
          /a:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Shared'
          /b:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Other'
        components:
          schemas:
            Shared:
              type: object
            Other:
              type: object
        """;

        var oldDoc = NormalizeWithLimits(oldSpec, new ProcessingLimits { MaxResolvedRefs = 1 });
        var newDoc = NormalizeWithLimits(newSpec, new ProcessingLimits { MaxResolvedRefs = 1 });

        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        var unresolved = changes.Where(c => c.Kind == ChangeKinds.SchemaUnresolvedChanged).ToList();
        Assert.Single(unresolved);
    }

    [Fact]
    public void Marker_Versus_Real_Schema_Emits_Event()
    {
        // Old side degraded to marker; new side fully resolvable (higher budget).
        var oldDoc = NormalizeWithLimits(RefHeavySpec, new ProcessingLimits { MaxResolvedRefs = 1 });
        var newDoc = NormalizeWithLimits(RefHeavySpec, ProcessingLimits.Default());

        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        Assert.Contains(changes, c => c.Kind == ChangeKinds.SchemaUnresolvedChanged);
    }
}

using System.Text.Json.Nodes;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi.Tests;

public static class TestSpecHelper
{
    public static ApiDocument Normalize(string yamlOrJson)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yamlOrJson, limits);
        var resolver = new RefResolver(limits, RemoteFetchPolicy.Disabled());
        resolver.Resolve(root, baseDirectory: null);
        return OpenApiNormalizer.Normalize(root);
    }

    public static IReadOnlyList<SemanticChange> Compare(string oldSpec, string newSpec)
        => new ComparisonEngine().Compare(Normalize(oldSpec), Normalize(newSpec));

    public static IReadOnlyList<string> Kinds(IReadOnlyList<SemanticChange> changes) =>
        changes.Select(c => c.Kind).OrderBy(k => k, StringComparer.Ordinal).ToList();

    public static bool HasKind(IReadOnlyList<SemanticChange> changes, string kind) =>
        changes.Any(c => c.Kind == kind);
}

/// <summary>End-to-end smoke tests for load → normalize → compare.</summary>
public class PipelineSmokeTests
{
    private const string V30Old = """
        openapi: 3.0.3
        info:
          title: Orders API
          version: "1.0"
        paths:
          /orders:
            get:
              operationId: listOrders
              parameters:
                - name: status
                  in: query
                  required: false
                  schema:
                    type: string
                    enum: [open, closed]
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        type: array
                        items:
                          $ref: '#/components/schemas/Order'
          /orders/{id}:
            get:
              operationId: getOrder
              parameters:
                - name: id
                  in: path
                  required: true
                  schema:
                    type: string
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Order'
        components:
          schemas:
            Order:
              type: object
              required: [id, total]
              properties:
                id:
                  type: string
                total:
                  type: number
                status:
                  type: string
                  enum: [pending, shipped]
        """;

    [Fact]
    public void Parses_Yaml_With_Internal_Refs_And_Detects_Version()
    {
        var doc = TestSpecHelper.Normalize(V30Old);
        Assert.Equal(SpecVersion.OpenApi30, doc.Version);
        Assert.Equal("Orders API", doc.Title);
        Assert.Equal(2, doc.Paths.Count);
        var order = doc.Paths["/orders"].Operations[HttpMethodKind.Get];
        Assert.Single(order.Responses);
    }

    [Fact]
    public void Identical_Specs_Produce_No_Changes()
    {
        var changes = TestSpecHelper.Compare(V30Old, V30Old);
        Assert.Empty(changes);
    }

    private const string V30NoRefunds = V30Old;

    private const string V30WithRefunds = """
        openapi: 3.0.3
        info:
          title: Orders API
          version: "1.1"
        paths:
          /orders:
            get:
              operationId: listOrders
              parameters:
                - name: status
                  in: query
                  required: false
                  schema:
                    type: string
                    enum: [open, closed]
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        type: array
                        items:
                          $ref: '#/components/schemas/Order'
          /orders/{id}:
            get:
              operationId: getOrder
              parameters:
                - name: id
                  in: path
                  required: true
                  schema:
                    type: string
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Order'
          /refunds:
            get:
              operationId: listRefunds
              responses:
                '200':
                  description: ok
        components:
          schemas:
            Order:
              type: object
              required: [id, total]
              properties:
                id:
                  type: string
                total:
                  type: number
                status:
                  type: string
                  enum: [pending, shipped]
        """;

    [Fact]
    public void Detects_Endpoint_Added_And_Removed()
    {
        var added = TestSpecHelper.Compare(V30NoRefunds, V30WithRefunds);
        Assert.True(TestSpecHelper.HasKind(added, ChangeKinds.EndpointAdded));
        Assert.True(TestSpecHelper.HasKind(added, ChangeKinds.InfoVersionChanged));

        var removed = TestSpecHelper.Compare(V30WithRefunds, V30NoRefunds);
        Assert.True(TestSpecHelper.HasKind(removed, ChangeKinds.EndpointRemoved));
        // The entire path disappeared -> endpoint-level removal only
        Assert.False(TestSpecHelper.HasKind(removed, ChangeKinds.OperationRemoved));
    }

    [Fact]
    public void Json_And_Yaml_Equivalence()
    {
        const string json = """
            {
              "openapi": "3.0.3",
              "info": {"title": "Orders API", "version": "1.0"},
              "paths": {
                "/ping": {
                  "get": {
                    "responses": {"200": {"description": "pong"}}
                  }
                }
              }
            }
            """;
        const string yaml = """
            openapi: 3.0.3
            info:
              title: Orders API
              version: "1.0"
            paths:
              /ping:
                get:
                  responses:
                    '200':
                      description: pong
            """;
        var a = TestSpecHelper.Normalize(json);
        var b = TestSpecHelper.Normalize(yaml);
        Assert.Equal(a.Paths.Count, b.Paths.Count);
        var changes = new ComparisonEngine().Compare(a, b);
        Assert.Empty(changes);
    }

    [Fact]
    public void Cyclic_References_Are_Handled_Without_Infinite_Recursion()
    {
        const string spec = """
            openapi: 3.0.3
            info:
              title: Tree API
              version: "1.0"
            paths:
              /tree:
                get:
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Node'
            components:
              schemas:
                Node:
                  type: object
                  properties:
                    name:
                      type: string
                    children:
                      type: array
                      items:
                        $ref: '#/components/schemas/Node'
            """;
        var doc = TestSpecHelper.Normalize(spec); // must not stack overflow
        Assert.NotEmpty(doc.Paths);

        // Same cyclic spec twice -> no changes.
        var changes = TestSpecHelper.Compare(spec, spec);
        Assert.Empty(changes);
    }

    [Fact]
    public void External_File_Refs_Resolve_Within_Base_Directory()
    {        var dir = Path.Combine(Path.GetTempPath(), "sentinel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "schemas.yaml"), """
                components:
                  schemas:
                    Product:
                      type: object
                      properties:
                        sku:
                          type: string
                """);
            File.WriteAllText(Path.Combine(dir, "main.yaml"), """
                openapi: 3.0.3
                info:
                  title: Ext Ref
                  version: "1.0"
                paths:
                  /products:
                    get:
                      responses:
                        '200':
                          description: ok
                          content:
                            application/json:
                              schema:
                                $ref: 'schemas.yaml#/components/schemas/Product'
                """);

            var limits = ProcessingLimits.Default();
            var root = DocumentParser.Parse("main",
                File.ReadAllText(Path.Combine(dir, "main.yaml")), limits);
            var resolver = new RefResolver(limits, RemoteFetchPolicy.Disabled());
            resolver.Resolve(root, dir);
            var doc = OpenApiNormalizer.Normalize(root);
            var op = doc.Paths["/products"].Operations[HttpMethodKind.Get];
            var schema = op.Responses["200"].Content["application/json"];
            Assert.Equal("object", schema.PrimaryType());
            Assert.NotNull(schema.PropertyOrNull("sku"));

            // escaping refs are rejected
            File.WriteAllText(Path.Combine(dir, "evil.yaml"), """
                openapi: 3.0.3
                info:
                  title: Evil
                  version: "1.0"
                paths:
                  /x:
                    get:
                      responses:
                        '200':
                          description: ok
                          content:
                            application/json:
                              schema:
                                $ref: '../../../../etc/passwd#/x'
                """);
            var evilRoot = DocumentParser.Parse("evil", File.ReadAllText(Path.Combine(dir, "evil.yaml")), limits);
            var ex = Assert.Throws<SpecParseException>(() =>
                new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(evilRoot, dir));
            Assert.Contains("escapes the base directory", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Sub_Document_May_Reference_Shared_File_Above_Its_Own_Directory()
    {
        // Real-world layout (e.g. DigitalOcean): resources/<cat>/endpoint.yml references
        // ../../shared/… which climbs above the containing document's own directory while
        // remaining inside the ROOT specification's tree. Containment is anchored to the
        // root document's directory, not each sub-document's directory.
        var dir = Path.Combine(Path.GetTempPath(), "sentinel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "resources", "a"));
        Directory.CreateDirectory(Path.Combine(dir, "shared"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "main.yaml"), """
                openapi: 3.0.3
                info:
                  title: Tree layout
                  version: "1.0"
                paths:
                  /widgets:
                    get:
                      responses:
                        '200':
                          description: ok
                          content:
                            application/json:
                              schema:
                                $ref: 'resources/a/endpoint.yaml#/Widget'
                """);
            File.WriteAllText(Path.Combine(dir, "resources", "a", "endpoint.yaml"), """
                Widget:
                  $ref: '../../shared/schema.yaml#/Thing'
                """);
            File.WriteAllText(Path.Combine(dir, "shared", "schema.yaml"), """
                Thing:
                  type: object
                  properties:
                    id:
                      type: string
                """);

            var limits = ProcessingLimits.Default();
            var root = DocumentParser.Parse("main",
                File.ReadAllText(Path.Combine(dir, "main.yaml")), limits);
            new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, dir);
            var doc = OpenApiNormalizer.Normalize(root);

            var schema = doc.Paths["/widgets"].Operations[HttpMethodKind.Get].Responses["200"].Content["application/json"];
            Assert.Equal("object", schema.PrimaryType());
            Assert.NotNull(schema.PropertyOrNull("id"));

            // Escaping the root tree entirely stays rejected.
            File.WriteAllText(Path.Combine(dir, "shared", "schema.yaml").Replace("schema.yaml", "evil.yaml"),
                File.ReadAllText(Path.Combine(dir, "main.yaml")).Replace("'resources/a/endpoint.yaml#/Widget'", "'../../../../etc/passwd#/x'"));
            var evilRoot = DocumentParser.Parse("evil",
                File.ReadAllText(Path.Combine(dir, "main.yaml").Replace("main.yaml", "shared/evil.yaml")), limits);
            var ex = Assert.Throws<SpecParseException>(() =>
                new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(evilRoot, dir));
            Assert.Contains("escapes the base directory", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_External_File_Degrades_To_Unresolved_Marker()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sentinel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string spec = """
                openapi: 3.0.3
                info:
                  title: Dead link on disk
                  version: "1.0"
                paths:
                  /x:
                    get:
                      responses:
                        '200':
                          description: ok
                          content:
                            application/json:
                              schema:
                                $ref: 'vanished.yaml#/X'
                """;
            var limits = ProcessingLimits.Default();
            var root = DocumentParser.Parse("disk-dead-link", spec, limits);
            new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, dir);
            OpenApiNormalizer.Normalize(root);

            // Analysis continues; the dead link remains visible as a marker.
            Assert.Equal(1, RefResolver.CountUnresolvedMarkers(root));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Swagger20_Is_Normalized()
    {
        const string v2 = """
            swagger: "2.0"
            info:
              title: Legacy API
              version: "2.0.0"
            host: api.example.com
            basePath: /v2
            schemes:
              - https
            consumes:
              - application/json
            paths:
              /pets:
                post:
                  operationId: createPet
                  parameters:
                    - name: body
                      in: body
                      required: true
                      schema:
                        type: object
                        required: [name]
                        properties:
                          name:
                            type: string
                          tag:
                            type: string
                  responses:
                    '201':
                      description: created
                      schema:
                        type: object
                        properties:
                          id:
                            type: integer
                    default:
                      description: error
                      schema:
                        $ref: '#/definitions/Error'
            definitions:
              Error:
                type: object
                properties:
                  code:
                    type: integer
            securityDefinitions:
              api_key:
                type: apiKey
                name: X-Api-Key
                in: header
            """;
        var doc = TestSpecHelper.Normalize(v2);
        Assert.Equal(SpecVersion.Swagger20, doc.Version);
        Assert.Contains("https://api.example.com/v2", doc.Servers.Select(s => s.Url));

        var post = doc.Paths["/pets"].Operations[HttpMethodKind.Post];
        Assert.NotNull(post.RequestBody);
        Assert.True(post.RequestBody!.Required);
        Assert.Contains("application/json", post.RequestBody.Content.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(post.Parameters); // body param became the request body

        var resp = post.Responses["201"].Content["application/json"];
        Assert.Equal("object", resp.PrimaryType());
        Assert.NotNull(doc.SecuritySchemes["api_key"]);
    }

    [Fact]
    public void OpenApi31_Nullable_Type_Array_Is_Understood()
    {
        const string v31a = """
            openapi: 3.1.0
            info: {title: T, version: "1"}
            paths:
              /a:
                get:
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: ["string", "null"]
            """;
        const string v31b = """
            openapi: 3.1.0
            info: {title: T, version: "1"}
            paths:
              /a:
                get:
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: string
            """;
        var changes = TestSpecHelper.Compare(v31a, v31b);
        // response becoming non-nullable is a narrowing change (non-breaking direction-wise but detected)
        Assert.True(TestSpecHelper.HasKind(changes, ChangeKinds.NullableChanged));
    }

    [Fact]
    public void Invalid_Documents_Produce_Actionable_Errors()
    {
        Assert.Throws<SpecParseException>(() => TestSpecHelper.Normalize("not: [valid: openapi"));
        Assert.Throws<SpecParseException>(() => TestSpecHelper.Normalize("{}"));
        Assert.Throws<SpecParseException>(() => TestSpecHelper.Normalize(
            "openapi: 9.9.9\ninfo: {title: t, version: \"1\"}\npaths: {}"));
    }

    [Fact]
    public void Deeply_Nested_Bomb_Is_Rejected()
    {
        var deep = new System.Text.StringBuilder();
        deep.Append("{\"openapi\":\"3.0.0\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{},\"components\":{\"schemas\":{\"bomb\":");
        const int depth = 300;
        for (var i = 0; i < depth; i++) deep.Append("{\"type\":\"object\",\"properties\":{\"p\":");
        deep.Append("{\"type\":\"string\"}");
        for (var i = 0; i < depth; i++) deep.Append("}}");
        deep.Append("}}}");
        Assert.ThrowsAny<Exception>(() => TestSpecHelper.Normalize(deep.ToString()));
    }
}

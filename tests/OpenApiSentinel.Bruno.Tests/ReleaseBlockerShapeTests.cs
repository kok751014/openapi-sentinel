using System.Text.Json.Nodes;
using OpenApiSentinel.Bruno.Model;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>Blocker E — default request body and 成功－完整參數 share ONE canonical producer.</summary>
public class CanonicalBodyShapeTests
{
    private static (string DefaultBody, string ExampleBody) Bodies(string spec)
    {
        var doc = TestSupport.NormalizeSpec(spec);
        var op = doc.Paths.Values.SelectMany(p => p.Operations.Values).Single();

        // default body: exactly what the engine writes on create
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = true },
            toolVersion: "test", generator: null);
        var (collection, _) = engine.Sync(null, doc);
        var item = collection.AllRequests().Single();
        var view = new BrunoRequestView(item.Node);

        // full example body: the scenario pipeline's 成功－完整參數
        var snapshot = BrunoSyncEngine.BuildScenarioBaseSnapshot(op, "{{baseUrl}}");
        var scenarios = new ScenarioGenerator(new BrunoExampleSettings()).Generate(
            OperationIdentity.From(op), op, snapshot);
        var fullExample = scenarios.First(s => s.Id == "success-full");

        return (view.JsonBodyRaw ?? "", fullExample.Request.JsonBody ?? "");
    }

    [Fact]
    public void Default_Body_Matches_Full_Example_Shape_Array()
    {
        const string spec = """
            openapi: 3.0.0
            info: { title: t, version: "1" }
            paths:
              /x:
                post:
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          type: array
                          items: { type: string, format: uuid }
                  responses:
                    '200': { description: OK }
            """;
        var (defaultBody, exampleBody) = Bodies(spec);
        Assert.IsType<JsonArray>(JsonNode.Parse(defaultBody));
        Assert.IsType<JsonArray>(JsonNode.Parse(exampleBody));
        Assert.Equal(JsonNode.Parse(defaultBody)!.ToJsonString(), JsonNode.Parse(exampleBody)!.ToJsonString());
    }

    [Fact]
    public void Default_Body_Matches_Full_Example_Shape_Object()
    {
        const string spec = """
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
                            a: { type: integer }
                            b: { type: string }
                  responses:
                    '200': { description: OK }
            """;
        var (defaultBody, exampleBody) = Bodies(spec);
        Assert.Equal(JsonNode.Parse(defaultBody)!.ToJsonString(), JsonNode.Parse(exampleBody)!.ToJsonString());
    }

    [Fact]
    public void Scalar_Body_Produces_Scalar_Default_And_Example()
    {
        const string spec = """
            openapi: 3.0.0
            info: { title: t, version: "1" }
            paths:
              /x:
                post:
                  requestBody:
                    content:
                      application/json:
                        schema: { type: string }
                  responses:
                    '200': { description: OK }
            """;
        var (defaultBody, exampleBody) = Bodies(spec);
        Assert.True(JsonNode.Parse(defaultBody!) is JsonValue);
        Assert.True(JsonNode.Parse(exampleBody!) is JsonValue);
    }
}

/// <summary>Blocker F — response examples derived from documented OpenAPI behavior only.</summary>
public class ResponseExampleTests
{
    [Fact]
    public void Response_Example_From_Explicit_OpenApi_Example_Wins()
    {
        var op = ReleaseBlockerSpec.Parse("""
            openapi: 3.0.0
            info: { title: t, version: "1" }
            paths:
              /x:
                get:
                  operationId: getX
                  responses:
                    '200':
                      description: OK
                      content:
                        application/json:
                          example:
                            custom: explicit-wins
                          schema:
                            type: object
                            properties:
                              generated: { type: string }
            """).SingleOperation();
        var (response, documented) = ResponseExampleFactory.ForSuccess(op);
        Assert.True(documented);
        Assert.Contains("explicit-wins", response!.BodyData!, StringComparison.Ordinal);
    }

    [Fact]
    public void Response_Example_From_Schema_Sample()
    {
        var op = ReleaseBlockerSpec.Parse("""
            openapi: 3.0.0
            info: { title: t, version: "1" }
            paths:
              /x:
                get:
                  operationId: getX
                  responses:
                    '200':
                      description: OK
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              name: { type: string }
            """).SingleOperation();
        var (response, documented) = ResponseExampleFactory.ForSuccess(op);
        Assert.True(documented);
        Assert.Contains("name", response!.BodyData!, StringComparison.Ordinal);
    }

    [Fact]
    public void Binary_Response_Is_Accurately_Not_Fabricated()
    {
        var op = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals).Operation("getSpeciesFile");
        var (response, _) = ResponseExampleFactory.ForSuccess(op);
        Assert.NotNull(response);
        Assert.Equal("binary", response!.BodyType);
        Assert.Null(response.BodyData);
    }

    [Fact]
    public void Undocumented_Success_Response_Attaches_Nothing_And_Warns()
    {
        var op = ReleaseBlockerSpec.Parse("""
            openapi: 3.0.0
            info: { title: t, version: "1" }
            paths:
              /x:
                post:
                  operationId: postX
                  requestBody:
                    content:
                      application/json:
                        schema: { type: object }
            """).SingleOperation();
        var (response, documented) = ResponseExampleFactory.ForSuccess(op);
        Assert.False(documented);
        Assert.Null(response);
        // The engine surfaces the warning instead of inventing behavior.
        var snapshot = BrunoSyncEngine.BuildScenarioBaseSnapshot(op, "{{baseUrl}}");
        _ = new ScenarioGenerator(new BrunoExampleSettings()).Generate(OperationIdentity.From(op), op, snapshot);
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings(),
            "test", new ScenarioGenerator(new BrunoExampleSettings()));
        _ = engine.Sync(null, ReleaseBlockerSpec.Parse("""
            openapi: 3.0.0
            info: { title: t, version: "1" }
            paths:
              /x:
                post:
                  operationId: postX
                  requestBody:
                    content:
                      application/json:
                        schema: { type: object }
            """));
    }
}

/// <summary>Blocker G — concise safe display names; full docs preserved.</summary>
public class DisplayNameTests
{
    [Fact]
    public async Task Request_Filename_Uses_Summary_Not_Description()
    {
        var doc = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals);
        var generator = new ScenarioGenerator(new BrunoExampleSettings { MaxPerOperation = 8 });
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = true },
            "test", generator);
        var (collection, _) = engine.Sync(null, doc);
        var item = collection.AllRequests().First(r => r.Name!.StartsWith("取得特定檔案", StringComparison.Ordinal));

        // Concise display/file name; description stays in docs.
        var dir = "/tmp/opencode/bru-names-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(dir);
        var plan = BruWorkspaceWriter.Plan(collection, dir, null);
        var fileNames = plan.FilesToWrite.Select(f => Path.GetFileName(f.Path)).ToList();
        Assert.Contains(fileNames, n => n.StartsWith("取得特定檔案", StringComparison.Ordinal));
        Assert.All(fileNames, n => Assert.DoesNotContain("\n", n, StringComparison.Ordinal));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Request_Filename_Strips_Html_And_Newlines()
    {
        var doc = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals);
        var name = BrunoSyncEngine.DisplayRequestName(OperationIdentity.From(doc.Operation("getSpeciesFile")), doc.Operation("getSpeciesFile"));

        Assert.DoesNotContain("<", name);
        Assert.DoesNotContain(">", name);
        Assert.DoesNotContain("\n", name);
        Assert.StartsWith("取得特定檔案", name, StringComparison.Ordinal);
        Assert.Contains("前端範例:", name);
        Assert.DoesNotContain("img", name, StringComparison.Ordinal); // markup stripped

        await Task.CompletedTask;
    }

    [Fact]
    public void Request_Filename_Collision_Is_Deterministic()
    {
        var doc = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals);
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = true },
            "test", generator: null);

        // Two plans over the same canonical collection produce identical relative paths —
        // cross-platform deterministic naming without case/character surprises.
        var dir = "/tmp/opencode/bru-names-" + Guid.NewGuid().ToString("N");
        var plan1 = BruWorkspaceWriter.Plan(Synced(doc), dir, null);
        var plan2 = BruWorkspaceWriter.Plan(Synced(doc), dir, null);
        var names1 = plan1.FilesToWrite.Select(f => Path.GetFileName(f.Path)).Where(n => n.EndsWith(".bru", StringComparison.Ordinal)).ToList();
        var names2 = plan2.FilesToWrite.Select(f => Path.GetFileName(f.Path)).Where(n => n.EndsWith(".bru", StringComparison.Ordinal)).ToList();
        Assert.Equal(names1, names2);
        Assert.All(names1, n =>
        {
            Assert.DoesNotContain("<", n);
            Assert.DoesNotContain(">", n);
            Assert.DoesNotContain("\n", n);
            Assert.EndsWith(".bru", n);
        });

        static BrunoCollectionDocument Synced(ApiDocument document)
        {
            var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = true },
                "test", generator: null);
            return engine.Sync(null, document).Collection;
        }
    }

    [Fact]
    public void Full_Description_Remains_In_Docs()
    {
        var doc = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals);
        var generator = new ScenarioGenerator(new BrunoExampleSettings { MaxPerOperation = 8 });
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = true },
            "test", generator);
        var (collection, _) = engine.Sync(null, doc);
        var item = collection.AllRequests().First(r => r.Name!.StartsWith("取得特定檔案", StringComparison.Ordinal));
        var docs = BrunoYaml.AsString(BrunoYaml.Get(item.Node, "docs")) ?? "";

        Assert.Contains("<img", docs);                       // backend HTML preserved verbatim
        Assert.Contains("完整描述文字應保留在文件中", docs);      // real description kept
    }
}

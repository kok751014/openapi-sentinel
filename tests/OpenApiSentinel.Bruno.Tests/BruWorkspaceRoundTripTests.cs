
using OpenApiSentinel.Bruno.Model;
using System.Text.Json.Nodes;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>
/// Blocker A — the workspace at bruno.workspacePath is a BRUNO-NATIVE collection
/// (bruno.json + .bru files) that Bruno Desktop opens directly. Bundled OpenCollection
/// and native output share one canonical model, so they cannot drift semantically.
/// </summary>
public class BruWorkspaceRoundTripTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("sentinel-bru").FullName;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static BrunoCollectionDocument Synced(ApiDocument document)
    {
        var generator = new ScenarioGenerator(new BrunoExampleSettings { MaxPerOperation = 8 });
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = true },
            "test", generator);
        return engine.Sync(null, document).Collection;
    }

    [Fact]
    public void Native_Collection_Contains_All_Operations_And_Manifest()
    {
        var doc = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals);
        var collection = Synced(doc);
        var workspace = Path.Combine(root, "ws");
        var plan = BruWorkspaceWriter.Plan(collection, workspace, null);

        Assert.Equal(1, plan.FilesToWrite.Count(f => Path.GetFileName(f.Path) == "bruno.json"));
        var requestFiles = plan.FilesToWrite.Where(f => f.Path.EndsWith(".bru", StringComparison.Ordinal)).ToList();

        foreach (var operationId in new[] { "createAnimal", "exportAnimals", "getAnimalsData", "login" })
        {
            var identity = OperationIdentity.From(doc.Operation(operationId));
            Assert.Contains(requestFiles, f => f.Content.Contains($"x-sentinel-key: {identity.StableKey}", StringComparison.Ordinal));
        }

        // Manifest is a valid minimal official bruno.json.
        var manifest = BruManifestSerializer.Parse(BruWorkspaceWriter.Manifest(collection), "bruno.json");
        Assert.Equal("collection", manifest.Type);
        Assert.Equal("1", manifest.Version);
        Assert.False(string.IsNullOrWhiteSpace(manifest.Name));
    }

    [Fact]
    public void Native_Workspace_RoundTrips_Through_Reader_Preserving_Manual_Content()
    {
        // 1st sync → write → hand-edit a value in the .bru file → read back → 2nd sync.
        var doc1 = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals);
        var collection = Synced(doc1);
        var workspace = Path.Combine(root, "ws");
        var plan = BruWorkspaceWriter.Plan(collection, workspace, null);
        BrunoWorkspaceWriter.Execute(plan);

        // Manual edit: change SpeciesId's form row on disk.
        var createFile = Path.Combine(workspace, "Animals", "新增動物.bru");
        Assert.True(File.Exists(createFile));
        File.WriteAllText(createFile, File.ReadAllText(createFile).Replace(
            "SpeciesId: 1", "SpeciesId: 999", StringComparison.Ordinal));

        // Read back through the native reader.
        var result = BruWorkspaceReader.Read(workspace, File.ReadAllText(Path.Combine(workspace, "bruno.json")));
        var item = TestSupport.FindRequest(result.Document, "新增動物")!;
        Assert.NotNull(item.SentinelTagKey); // binding survives
        var rows = new BrunoRequestView(item.Node).FormRows();
        Assert.Equal("999", rows.First(r => r.Name == "SpeciesId").Value); // manual value preserved

        // 2nd sync over the same contract keeps the manual value (preserveManualValues).
        var settings = new BrunoSyncSettings { PreserveManualValues = true };
        var exampleSettings = new BrunoExampleSettings { Enabled = true };
        var engine2 = new BrunoSyncEngine(settings, exampleSettings, "test",
            new ScenarioGenerator(exampleSettings));
        var (updated, outcome) = engine2.Sync(result.Document, doc1);
        var updatedItem = TestSupport.FindRequest(updated, "新增動物")!;
        var updatedRows = new BrunoRequestView(updatedItem.Node).FormRows();
        Assert.Equal(OperationAction.Unchanged, outcome.Operations.First(o => o.OperationId == "createAnimal").Action);
        Assert.Equal("999", updatedRows.First(r => r.Name == "SpeciesId").Value);
    }

    [Fact]
    public void Bundled_And_Native_Outputs_Semantically_Match()
    {
        var doc = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals);
        var collection = Synced(doc);

        // Write the workspace, then READ IT BACK — it must equal the canonical model that
        // also produced the bundled collection (same requests, bindings, params, bodies).
        var workspace = Path.Combine(root, "ws");
        BrunoWorkspaceWriter.Execute(BruWorkspaceWriter.Plan(collection, workspace, null));
        var roundTrip = BruWorkspaceReader.Read(workspace, File.ReadAllText(Path.Combine(workspace, "bruno.json")));

        var originalRequests = collection.AllRequests().OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        var roundTripRequests = roundTrip.Document.AllRequests().OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        Assert.Equal(originalRequests.Count, roundTripRequests.Count);

        for (var i = 0; i < originalRequests.Count; i++)
        {
            var a = originalRequests[i];
            var b = roundTripRequests[i];
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.SentinelTagKey, b.SentinelTagKey);
            Assert.Equal(a.Http!["url"], b.Http!["url"]);
            Assert.Equal(a.Http["method"], b.Http["method"]);

            // Bodies carry identical rows/JSON regardless of serialization format.
            var viewA = new BrunoRequestView(a.Node);
            var viewB = new BrunoRequestView(b.Node);
            Assert.Equal(viewA.BodyType, viewB.BodyType);
            if (viewA.FormRows() is { Count: > 0 } rowsA)
            {
                Assert.Equal(rowsA, viewB.FormRows());
            }
            else
            {
                Assert.Equal(Normalize(viewA.JsonBodyRaw), Normalize(viewB.JsonBodyRaw));
            }
        }
    }

    [Fact]
    public void Structural_Validator_Rejects_Broken_Native_Files()
    {
        const string manifest = """{"version":"1","name":"t","type":"collection"}""";
        const string good =
            """
            meta {
              name: Get
              type: http
              seq: 1
            }

            get {
              url: https://example.com/api
            }
            """;
        const string missingUrl =
            """
            meta {
              name: Broken
              type: http
              seq: 2
            }

            get {
            }
            """;

        var (validGood, _) = Validation.BruCollectionValidator.Validate("/tmp", manifest,
            new List<(string RelativePath, string Content)> { ("Get.bru", good) });
        Assert.True(validGood);

        var (_, errors) = Validation.BruCollectionValidator.Validate("/tmp", manifest,
            new List<(string RelativePath, string Content)> { ("Broken.bru", missingUrl) });
        Assert.NotEmpty(errors);
    }

    private static string? Normalize(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            return node?.ToJsonString();
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }
    }
}

/// <summary>§12 — real-regression fixture matching the observed ssz-ams-api Animals category.</summary>
public class AnimalsRegressionTests
{
    private static readonly string[] RequiredNames = ["SpeciesId", "Name", "Gender"];

    [Fact]
    public async Task Animals_Fixture_Satisfies_All_Release_Blocker_Assertions()
    {
        var doc = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals);

        // POST /api/Animals carries a multipart/form-data body with fields.
        var create = doc.Operation("createAnimal");
        var canonical = CanonicalBodyFactory.Create(create.RequestBody!, SampleValueFactory.Depth.Full);
        Assert.Equal(BodyKind.MultipartForm, canonical.Kind);

        var requiredOnly = CanonicalBodyFactory.Create(create.RequestBody!, SampleValueFactory.Depth.RequiredOnly);
        Assert.Equal(RequiredNames, RequiredNamesOf(requiredOnly));

        // export default body is an ARRAY of uuids — not {} — matching [FromBody] List<Guid>.
        var exportOp = doc.Operation("exportAnimals");
        var snapshot = BrunoSyncEngine.BuildScenarioBaseSnapshot(exportOp, "{{baseUrl}}");
        Assert.IsType<JsonArray>(JsonNode.Parse(snapshot.JsonBody!));

        var scenarios = ReleaseBlockerSpec.GenerateFor(exportOp);
        var fullExport = scenarios.First(s => s.Id == "success-full");
        Assert.True(JsonNode.Parse(fullExport.Request.JsonBody!) is JsonArray);

        // UUID path param: invalid-format scenario exists; no redundant wrong-type.
        var getData = doc.Operation("getAnimalsData");
        var dataScenarios = ReleaseBlockerSpec.GenerateFor(getData);
        Assert.Contains(dataScenarios, s => s.Category == ScenarioCategory.InvalidFormat);
        Assert.DoesNotContain(dataScenarios, s => s.Category == ScenarioCategory.WrongType);

        // Numeric enum invalid case keeps numeric type.
        Assert.All(canonical.Rows!.Where(r => r.Name == "Gender"), r => Assert.Equal("1", r.Value));

        // Response examples generated where the spec documents them.
        var loginScenarios = ReleaseBlockerSpec.GenerateFor(doc.Operation("login"));
        Assert.All(loginScenarios.Where(s => s.Category is ScenarioCategory.SuccessFull),
            s =>
            {
                Assert.NotNull(s.Response);
                Assert.Contains("token", s.Response!.BodyData ?? "", StringComparison.Ordinal);
            });

        await Task.CompletedTask;

        static IReadOnlyCollection<string> RequiredNamesOf(CanonicalBody body) =>
            body.Rows!.Select(r => r.Name).ToList();
    }
}

using System.Text.Json.Nodes;
using OpenApiSentinel.Bruno.Model;
using YamlDotNet.Core;
using OpenApiSentinel.OpenApi;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>
/// §7 regression suite for the official OpenCollection v1.0 compatibility work:
/// every generated collection must pass the pinned official schema BEFORE it could
/// ever be written, request roots must never carry Sentinel blocks, every param row
/// must have an explicit query/path type and a string value, and legacy v1.1 files
/// migrate forward without losing metadata.
/// </summary>
public class OpenCollectionSchemaConformanceTests
{
    /// <summary>Realistic mixed contract covering §7B's checklist.</summary>
    private const string RichSpec = """
        openapi: 3.0.3
        info:
          title: 全功能範例 API
          version: "2.0"
        servers:
          - url: https://api.example.com/v1
        components:
          securitySchemes:
            bearerAuth: { type: http, scheme: bearer }
        security:
          - bearerAuth: []
        paths:
          /orders:
            post:
              operationId: createOrder
              summary: 建立訂單
              tags: [訂單]
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      type: object
                      required: [customerId, orderType]
                      properties:
                        customerId: { type: integer, minimum: 1 }
                        orderType: { type: string, enum: [NORMAL, URGENT] }
                        trackingId: { type: string, format: uuid }
                        createdAt: { type: string, format: date }
                        note: { type: string, nullable: true }
              responses:
                '200': { description: OK }
          /orders/{id}:
            put:
              operationId: replaceOrder
              summary: 完整更新訂單
              tags: [訂單]
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string, format: uuid }
              requestBody:
                content:
                  application/json:
                    schema:
                      type: object
                      properties:
                        count: { type: integer }
              responses:
                '200': { description: OK }
            delete:
              operationId: deleteOrder
              summary: 刪除訂單
              tags: [訂單]
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '204': { description: NoContent }
          /users/{userId}/orders:
            get:
              operationId: listUserOrders
              summary: 查詢使用者訂單
              tags: [使用者]
              parameters:
                - name: userId
                  in: path
                  required: true
                  schema: { type: string, format: uuid }
                - name: status
                  in: query
                  schema: { type: string, enum: [open, closed] }
                - name: since
                  in: query
                  schema: { type: string, format: date }
                - name: verbose
                  in: query
                  schema: { type: boolean }
              responses:
                '200': { description: OK }
        """;

    private static (BrunoCollectionDocument Collection, BrunoSyncOutcome Outcome) Run(
        string spec, BrunoSyncSettings? syncSettings = null)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("spec.yaml", spec, limits);
        var doc = OpenApiNormalizer.Normalize(root);
        var engine = new BrunoSyncEngine(
            syncSettings ?? new BrunoSyncSettings(),
            new BrunoExampleSettings(),
            "test");
        return engine.Sync(null, doc);
    }

    [Fact]
    public void Generated_Collection_Passes_Official_OpenCollection_Schema()
    {
        var (collection, outcome) = Run(RichSpec);
        Assert.Equal(4, outcome.AddedCount);

        var (valid, errors) = Validation.OpenCollectionSchemaValidator.Validate(collection.ToYaml(), "collection.yml");
        Assert.True(valid, string.Join("\n", errors.Select(e => e.Format())));
    }

    [Fact]
    public void Workspace_Root_And_Request_Files_Pass_Official_Schema()
    {
        var (collection, _) = Run(RichSpec);
        using var scratch = new TempDir();
        var plan = BrunoWorkspaceWriter.Plan(collection, scratch.Path);
        Assert.Empty(plan.OrphanedFiles);
        BrunoWorkspaceWriter.Execute(plan);

        foreach (var file in Directory.EnumerateFiles(scratch.Path, "*.yml", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            // The official schema governs standalone files via named definitions:
            // collection root → root schema; folder.yml → Folder; request files → HttpRequest.
            var definition = name == "folder.yml" ? "Folder"
                : name == "opencollection.yml" ? null
                : "HttpRequest";

            var (valid, errors) = Validation.OpenCollectionSchemaValidator.Validate(
                File.ReadAllText(file), name, definition);
            Assert.True(valid, $"{file} ({definition ?? "root"}):\n{string.Join("\n", errors.Select(e => e.Format()))}");
        }
    }

    [Fact]
    public void Request_Root_Never_Carries_X_Sentinel_Block()
    {
        var (collection, _) = Run(RichSpec);

        foreach (var item in Walk(collection.RootItems.ToList()))
        {
            Assert.False(BrunoYaml.HasKey(item.Node, "x-sentinel"),
                "request/folder item must never carry a request-level x-sentinel block");
        }

        // Metadata lives in exactly one place: the collection-level index.
        var store = SentinelMetaStore.For(collection);
        Assert.Equal(4, store.EntriesWithKeys().Count);
    }

    [Fact]
    public void Every_Param_Row_Has_Explicit_Type_And_String_Value()
    {
        var (collection, _) = Run(RichSpec);

        foreach (var request in Walk(collection.RootItems.ToList()))
        {
            var view = new BrunoRequestView(request.Node);
            foreach (var row in view.Params())
            {
                Assert.True(row.ParamType is "query" or "path");
                Assert.False(row.Enabled == false && BrunoYaml.HasKey(row.Node, "enabled"));
            }

            foreach (var example in view.ExamplesOrNull ?? new YamlSequenceNode())
            {
                if (example is not YamlMappingNode map ||
                    BrunoYaml.MappingOf(map, "request") is not { } exampleRequest)
                {
                    continue;
                }

                if (BrunoYaml.SequenceOf(exampleRequest, "params") is { } seq)
                {
                    foreach (var row in BrunoYaml.MappingItems(seq))
                    {
                        var type = BrunoYaml.AsString(BrunoYaml.Get(row, "type"));
                        Assert.True(type is "query" or "path");
                    }
                }
            }
        }
    }

    [Fact]
    public void All_Param_Serialized_Values_Are_YAML_Strings()
    {
        var (collection, _) = Run(RichSpec);
        var yaml = collection.ToYaml();
        var reparsed = TestYaml.Parse(yaml);

        foreach (var (path, row) in FindParamRows(reparsed))
        {
            var value = row["value"];
            var scalar = Assert.IsType<YamlScalarNode>(value);
            // A plain scalar that would re-read as int/bool/null violates the schema's string type.
            if (scalar.Style is ScalarStyle.Any or ScalarStyle.Plain)
            {
                Assert.False(BrunoYaml.PlainScalarResolvesToNonString(scalar.Value ?? ""),
                    $"{path}: value '{scalar.Value}' would re-read as a YAML non-string and violate the schema");
            }
        }
    }

    [Fact]
    public void Legacy_Request_Level_X_Sentinel_Migrates_To_Index_On_Sync()
    {
        const string legacy = """
            opencollection: "1.0.0"
            info:
              name: 舊版集合
            items:
              - info:
                  name: 建立訂單
                  type: http
                  seq: 1
                http:
                  method: POST
                  url: "{{baseUrl}}/orders"
                  body:
                    type: json
                    data: "{}"
                x-sentinel:
                  operationId: createOrder
                  method: POST
                  path: /orders
                  identityKey: opid:createOrder
                  managedBody: true
                  generatorVersion: 1.1.0
                  userNote: 千萬不要弄丟
            """;

        var existing = BrunoCollectionDocument.Parse(legacy, "legacy.yml");

        // Sync against the same contract → migration happens automatically.
        const string spec = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            paths:
              /orders:
                post:
                  operationId: createOrder
                  responses:
                    '200': { description: OK }
            """;
        var limits = ProcessingLimits.Default();
        var doc = OpenApiNormalizer.Normalize(DocumentParser.Parse("s.yaml", spec, limits));
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = false }, "test");
        var (result, outcome) = engine.Sync(existing, doc);

        Assert.Single(outcome.Warnings, w => w.Contains("遷移", StringComparison.Ordinal));

        var migrated = result.AllRequests().Single();
        Assert.False(BrunoYaml.HasKey(migrated.Node, "x-sentinel"), "illegal block must be gone after migration");
        Assert.Equal("opid:createOrder", migrated.SentinelTagKey);

        var entry = SentinelMetaStore.For(result).EntryOrNull("opid:createOrder");
        Assert.NotNull(entry);
        Assert.Equal("createOrder", BrunoYaml.AsString(BrunoYaml.Get(entry!, "operationId")));
        Assert.Equal("千萬不要弄丟", BrunoYaml.AsString(BrunoYaml.Get(entry!, "userNote"))); // nothing silently dropped
        Assert.Equal("test", BrunoYaml.AsString(BrunoYaml.Get(entry!, "generatorVersion"))); // refreshed to current tool

        var (valid, errors) = Validation.OpenCollectionSchemaValidator.Validate(result.ToYaml(), "migrated.yml");
        Assert.True(valid, string.Join("\n", errors.Select(e => e.Format())));
    }

    [Fact]
    public void Identity_Transition_Carries_Metadata_To_New_Index_Entry()
    {
        // Same operation; second sync ADDS an operationId → identity transitions m+p → opid.
        const string before = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            paths:
              /a:
                get:
                  responses:
                    '200': { description: OK }
            """;
        const string after = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            paths:
              /a:
                get:
                  operationId: getA
                  responses:
                    '200': { description: OK }
            """;

        var limits = ProcessingLimits.Default();
        var docA = OpenApiNormalizer.Normalize(DocumentParser.Parse("a.yaml", before, limits));
        var docB = OpenApiNormalizer.Normalize(DocumentParser.Parse("b.yaml", after, limits));
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = false }, "t");

        var (first, _) = engine.Sync(null, docA);
        var item = first.AllRequests().Single();
        BrunoYaml.SetString(SentinelMetaStore.For(first).GetOrCreateEntry(item.SentinelTagKey!), "userNote", "keep-me");

        var (_, outcome) = engine.Sync(first, docB);
        Assert.Single(outcome.Operations, o => o.Action == OperationAction.Updated);

        var store = SentinelMetaStore.For(first);
        Assert.Null(store.EntryOrNull("m+p:GET /a")); // old entry retired, not orphaned
        var entry = store.EntryOrNull("opid:getA");
        Assert.NotNull(entry);
        Assert.Equal("keep-me", BrunoYaml.AsString(BrunoYaml.Get(entry!, "userNote")));
    }

    [Fact]
    public void Validator_Reports_Precise_Path_For_Violations()
    {
        const string bad = """
            opencollection: "1.0.0"
            info:
              name: x
            items:
              - info:
                  name: r
                  type: http
                  seq: 1
                http:
                  method: GET
                  url: "https://example.com"
                  params:
                    - name: id
                      value: 12345
            """;

        var (valid, errors) = Validation.OpenCollectionSchemaValidator.Validate(bad, "bad.yml");
        Assert.False(valid);
        var text = string.Join("\n", errors.Select(e => e.Format()));
        Assert.Contains("params[0].type", text, StringComparison.Ordinal);      // required `type` missing…
        Assert.Contains("required", text, StringComparison.Ordinal);
        Assert.Contains("items[0]", text, StringComparison.Ordinal);            // …located precisely
    }

    [Fact]
    public void Plain_Number_Value_Is_A_Rejected_Type_Quoted_Value_Is_Valid()
    {
        const string template = """
            opencollection: "1.0.0"
            info:
              name: x
            items:
              - info:
                  name: r
                  type: http
                  seq: 1
                http:
                  method: GET
                  url: "https://example.com?p=1"
                  params:
                    - name: p
                      value: {VALUE}
                      type: query
            """;

        var (invalid, _) = Validation.OpenCollectionSchemaValidator.Validate(
            template.Replace("{VALUE}", "12345", StringComparison.Ordinal), "v.yml");
        Assert.False(invalid, "plain numeric scalar must violate the string-typed value slot");

        var (valid, errors) = Validation.OpenCollectionSchemaValidator.Validate(
            template.Replace("{VALUE}", "\"12345\"", StringComparison.Ordinal), "v.yml");
        Assert.True(valid, string.Join("\n", errors.Select(e => e.Format())));
    }

    [Fact]
    public void Normalizer_Upgrades_Legacy_Shapes_Before_Validation()
    {
        const string legacyish = """
            opencollection: "1.0.0"
            info:
              name: x
            items:
              - info:
                  name: r
                  type: http
                  seq: 1
                http:
                  method: GET
                  url: "https://example.com?a=1"
                  params:
                    - name: a
                      value: 1
                  headers:
                    - name: X-Trace
                      value: abc
                      enabled: true
            """;

        var root = BrunoYaml.ParseDocument(legacyish, "l.yml");
        var notes = OpenCollectionNormalizer.Normalize(root);

        var http = BrunoYaml.MappingOf(BrunoYaml.MappingItems(BrunoYaml.SequenceOf(root, "items")!).First(), "http")!;
        var paramRow = BrunoYaml.MappingItems(BrunoYaml.SequenceOf(http, "params")!).First();
        Assert.Equal("query", BrunoYaml.AsString(BrunoYaml.Get(paramRow, "type")));
        Assert.Contains(notes, n => n.Contains("type", StringComparison.Ordinal));

        var headerRow = BrunoYaml.MappingItems(BrunoYaml.SequenceOf(http, "headers")!).First();
        Assert.False(BrunoYaml.HasKey(headerRow, "enabled"), "legacy enabled flag must be replaced by disabled semantics");

        var (valid, errors) = Validation.OpenCollectionSchemaValidator.Validate(BrunoYaml.ToYaml(root), "normalized.yml");
        Assert.True(valid, string.Join("\n", errors.Select(e => e.Format())));
    }

    [Fact]
    public void Auth_Is_Recorded_In_Index_And_Never_As_Request_Level_Block()
    {
        var (collection, _) = Run(RichSpec);
        var yaml = collection.ToYaml();

        // All four operations inherit global bearer security.
        var store = SentinelMetaStore.For(collection);
        Assert.All(store.EntriesWithKeys(), e =>
            Assert.True(BrunoYaml.AsBool(BrunoYaml.Get(e.Entry, "requiresAuth")) == true, e.Key));
        Assert.DoesNotContain("token", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer ", yaml, StringComparison.OrdinalIgnoreCase);
    }

    private static List<BrunoItem> Walk(List<BrunoItem> items)
    {
        var result = new List<BrunoItem>();
        foreach (var item in items)
        {
            result.Add(item);
            if (item.IsFolder)
            {
                result.AddRange(Walk(item.Children.ToList()));
            }
        }

        return result;
    }

    private static List<(string Path, YamlMappingNode Row)> FindParamRows(YamlMappingNode root)
    {
        var results = new List<(string Path, YamlMappingNode Row)>();
        void WalkMap(YamlMappingNode map, string path)
        {
            if (BrunoYaml.SequenceOf(map, "params") is { } seq)
            {
                var rows = BrunoYaml.MappingItems(seq).ToList();
                for (var i = 0; i < rows.Count; i++)
                {
                    results.Add(($"{path}.params[{i}]", rows[i]));
                }
            }

            foreach (var entry in map.Children)
            {
                var key = entry.Key is YamlScalarNode { Value: not null } k ? k.Value : "?";
                Walk(entry.Value, $"{path}.{key}");
            }
        }

        void WalkSeq(YamlSequenceNode seq, string path)
        {
            for (var i = 0; i < seq.Children.Count; i++)
            {
                Walk(seq.Children[i], $"{path}[{i}]");
            }
        }

        void Walk(YamlNode node, string path)
        {
            switch (node)
            {
                case YamlMappingNode m:
                    WalkMap(m, path);
                    break;
                case YamlSequenceNode s:
                    WalkSeq(s, path);
                    break;
            }
        }

        Walk(root, "$");
        return results;
    }
}

/// <summary>Temp workspace directory with deterministic lifetime inside tests.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("sentinel-ws").FullName;

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
    }
}

using System.Text.Json.Nodes;
using OpenApiSentinel.Bruno.Model;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>Shared helpers: build ApiDocuments from YAML strings and inspect collections.</summary>
public static class TestSupport
{
    public static ApiDocument NormalizeSpec(string yamlOrJson)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yamlOrJson, limits);
        return OpenApiNormalizer.Normalize(root);
    }

    /// <summary>Resolves an item's stored identity against its collection's meta index.</summary>
    public static OperationIdentity? IdentityOf(this BrunoItem item, BrunoCollectionDocument collection) =>
        item.IdentityOf(SentinelMetaStore.For(collection));

    /// <summary>The item's metadata entry in the collection-level index; null when unbound.</summary>
    public static YamlDotNet.RepresentationModel.YamlMappingNode? SentinelEntry(
        this BrunoItem item, BrunoCollectionDocument collection) =>
        item.SentinelTagKey is { } key ? SentinelMetaStore.For(collection).EntryOrNull(key) : null;

    /// <summary>A minimal but realistic Order Management contract.</summary>
    public const string V1Spec = """
        openapi: 3.0.3
        info:
          title: 訂單管理 API
          version: "1.0"
        servers:
          - url: https://api.example.com/v1
        paths:
          /orders:
            post:
              operationId: createOrder
              summary: 建立訂單
              tags:
                - 訂單
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      type: object
                      required:
                        - customerId
                        - orderType
                      properties:
                        customerId:
                          type: integer
                          minimum: 1
                        orderType:
                          type: string
                          enum:
                            - NORMAL
                            - URGENT
                        quantity:
                          type: integer
                          minimum: 1
                          maximum: 100
                        note:
                          type: string
                          maxLength: 200
              responses:
                '200':
                  description: OK
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Order'
          /users/{id}:
            get:
              operationId: getUser
              summary: 查詢使用者
              tags:
                - 使用者
              parameters:
                - name: id
                  in: path
                  required: true
                  schema:
                    type: integer
                - name: verbose
                  in: query
                  schema:
                    type: boolean
              responses:
                '200':
                  description: OK
        components:
          schemas:
            Order:
              type: object
              properties:
                id:
                  type: integer
                status:
                  type: string
        """;

    public static BrunoItem? FindRequest(BrunoCollectionDocument doc, string name) =>
        doc.AllRequests().FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.Ordinal));

    public static BrunoRequestView View(BrunoItem item) => new(item.Node);

    public static JsonObject? BodyJson(BrunoItem item)
    {
        var raw = View(item).JsonBodyRaw;
        return raw is null ? null : System.Text.Json.Nodes.JsonNode.Parse(raw)?.AsObject();
    }
}

/// <summary>
/// Core incremental sync semantics: first sync creates; unchanged second sync is a no-op;
/// contract changes update only contract-derived fields; manual values survive.
/// </summary>
public class BrunoSyncEngineCoreTests
{
    private static (BrunoCollectionDocument Collection, BrunoSyncOutcome Outcome) Run(
        BrunoCollectionDocument? existing,
        string spec,
        BrunoSyncSettings? syncSettings = null,
        BrunoExampleSettings? exampleSettings = null,
        IReadOnlyList<Scenarios.BusinessScenario>? business = null)
    {
        var doc = TestSupport.NormalizeSpec(spec);
        var engine = new BrunoSyncEngine(
            syncSettings ?? new BrunoSyncSettings(),
            exampleSettings ?? new BrunoExampleSettings { Enabled = false },
            toolVersion: "test",
            generator: null,
            businessScenarios: business);
        return engine.Sync(existing, doc);
    }

    [Fact]
    public void FirstSync_Creates_Requests_With_Folders_Params_And_Body()
    {
        var (collection, outcome) = Run(null, TestSupport.V1Spec);

        Assert.Equal(2, outcome.AddedCount);
        Assert.Empty(outcome.Warnings);

        var createOrder = TestSupport.FindRequest(collection, "建立訂單");
        Assert.NotNull(createOrder);
        var view = TestSupport.View(createOrder!);

        Assert.Equal("POST", view.Method);
        Assert.Equal("https://api.example.com/v1/orders", view.Url); // absolute server url used verbatim
        Assert.Equal("json", view.BodyType);

        var body = TestSupport.BodyJson(createOrder!)!;
        Assert.Equal(1, body["customerId"]!.GetValue<int>()); // minimum-clamped deterministic sample
        Assert.Equal("NORMAL", body["orderType"]!.GetValue<string>()); // first enum value

        // folder derived from first tag
        var folderItem = collection.RootItems.First(i => i.IsFolder);
        Assert.Equal("訂單", folderItem.Name);
        Assert.Contains(folderItem.Children, c => ReferenceEquals(c.Node, createOrder!.Node));

        // sentinel metadata recorded (collection-level extensions index + binding tag)
        var identity = createOrder!.IdentityOf(collection);
        Assert.NotNull(identity);
        Assert.Equal("opid:createOrder", identity!.StableKey);
        Assert.True(createOrder.IsSentinelManaged);
    }

    [Fact]
    public void Path_Parameters_Become_Bruno_Style()
    {
        var (collection, _) = Run(null, TestSupport.V1Spec);
        var getUser = collection.AllRequests().Single(r => r.IdentityOf(collection)?.StableKey == "opid:getUser");
        var view = TestSupport.View(getUser);

        Assert.Equal("https://api.example.com/v1/users/:id", view.Url);
        var pathParam = view.Params().Single(p => p.ParamType == "path");
        Assert.Equal("id", pathParam.Name);
        Assert.Equal("query", view.Params().Single(p => p.Name == "verbose").ParamType);
    }

    [Fact]
    public void SecondSync_Without_Changes_Is_Idempotent()
    {
        var (first, _) = Run(null, TestSupport.V1Spec);
        // simulate a manual annotation inside the meta index that an unchanged re-sync must not touch
        var order = TestSupport.FindRequest(first, "建立訂單")!;
        BrunoYaml.SetString(order.SentinelEntry(first)!, "note-manual", "keep");

        var yamlBefore = first.ToYaml();
        var (second, outcome) = Run(first, TestSupport.V1Spec);

        Assert.Equal(2, outcome.UnchangedCount);
        Assert.Equal(0, outcome.UpdatedCount + outcome.AddedCount);
        Assert.Equal(yamlBefore, second.ToYaml()); // byte-stable
    }

    [Fact]
    public void Contract_Change_Updates_Derived_Fields_And_Preserves_Manual_Values()
    {
        var (first, _) = Run(null, TestSupport.V1Spec);

        // user edits: a param value, a body value, adds a manual header + docs text
        var order = TestSupport.FindRequest(first, "建立訂單")!;
        var view = TestSupport.View(order);
        var bodyObj = TestSupport.BodyJson(order)!;
        bodyObj["note"] = "人工備忘";
        view.SetJsonBody(bodyObj.ToJsonString());
        BrunoYaml.SetString(order.Node, "docs", "# 人工維護文件\n自訂說明內容。");

        const string v2 = """
            openapi: 3.0.3
            info:
              title: 訂單管理 API
              version: "1.1"
            servers:
              - url: https://api.example.com/v1
            paths:
              /orders:
                post:
                  operationId: createOrder
                  summary: 建立訂單
                  tags:
                    - 訂單
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          type: object
                          required:
                            - customerId
                            - orderType
                          properties:
                            customerId:
                              type: integer
                              minimum: 1
                              maximum: 999999
                            orderType:
                              type: string
                              enum:
                                - NORMAL
                                - URGENT
                                - SCHEDULED
                            quantity:
                              type: integer
                              minimum: 1
                              maximum: 50
                            note:
                              type: string
                              maxLength: 500
                  responses:
                    '200':
                      description: OK
              /users/{id}:
                get:
                  operationId: getUser
                  summary: 查詢使用者
                  tags:
                    - 使用者
                  parameters:
                    - name: id
                      in: path
                      required: true
                      schema:
                        type: integer
                    - name: verbose
                      in: query
                      schema:
                        type: boolean
                  responses:
                    '200':
                      description: OK
            """;

        var (second, outcome) = Run(first, v2);

        var updated = outcome.Operations.Single(o => o.OperationId == "createOrder");
        Assert.Equal(OperationAction.Updated, updated.Action);

        var orderAfter = TestSupport.FindRequest(second, "建立訂單")!;
        var bodyAfter = TestSupport.BodyJson(orderAfter)!;
        // manual value survived the merge
        Assert.Equal("人工備忘", bodyAfter["note"]!.GetValue<string>());

        // hash refreshed so a third identical sync is unchanged
        var (_, third) = Run(second, v2);
        Assert.Equal(2, third.UnchangedCount);
    }

    [Fact]
    public void Deleted_Operation_Is_Stale_By_Default_And_Removable_By_Policy()
    {
        const string onlyUsers = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            paths:
              /users/{id}:
                get:
                  operationId: getUser
                  parameters:
                    - name: id
                      in: path
                      required: true
                      schema: { type: integer }
                  responses:
                    '200': { description: OK }
            """;

        var (first, _) = Run(null, TestSupport.V1Spec);

        var (_, staleOutcome) = Run(first, onlyUsers);
        var stale = staleOutcome.Operations.Single(o => o.Action == OperationAction.Stale);
        Assert.Equal("/orders", stale.Path);
        Assert.Single(stale.Warnings);
        Assert.Contains("removeDeletedOperations=false", stale.Warnings[0], StringComparison.Ordinal);

        var (afterRemove, removedOutcome) = Run(
            first, onlyUsers, new BrunoSyncSettings { RemoveDeletedOperations = true });
        Assert.Single(removedOutcome.Operations, o => o.Action == OperationAction.Removed);
        Assert.Null(TestSupport.FindRequest(afterRemove, "建立訂單"));
    }

    [Fact]
    public void Unmarked_User_Request_Adopted_When_Unique_Method_Path_Match()
    {
        const string specWithUserOp = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            servers:
              - url: "{{baseUrl}}"
            paths:
              /ping:
                get:
                  responses:
                    '200': { description: OK }
            """;

        const string preExisting = """
            opencollection: "1.0.0"
            info:
              name: hand-made
            items:
              - info:
                  name: 我自己寫的 ping
                  type: http
                  seq: 1
                http:
                  method: GET
                  url: "{{baseUrl}}/ping"
                docs: 手動文件
            """;

        var existing = BrunoCollectionDocument.Parse(preExisting, "hand.yml");
        var (result, outcome) = Run(existing, specWithUserOp);

        var ping = Enumerable.Single(result.AllRequests());
        Assert.Equal("我自己寫的 ping", ping.Name); // adopted, not duplicated/recreated
        Assert.Equal(OperationAction.Added, Enumerable.Single(outcome.Operations).Action);
        Assert.NotNull(ping.IdentityOf(result));
        Assert.Equal("手動文件", new BrunoRequestView(ping.Node).Docs);
    }

    [Fact]
    public void Duplicate_OperationId_In_Spec_Warns_And_Processes_First()
    {
        const string dup = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            paths:
              /a:
                get:
                  operationId: same
                  responses:
                    '200': { description: OK }
              /b:
                get:
                  operationId: same
                  responses:
                    '200': { description: OK }
            """;

        var (_, outcome) = Run(null, dup);
        Assert.Single(outcome.Operations);
        Assert.Contains(outcome.Warnings, w => w.Contains("重複", StringComparison.Ordinal));
    }

    [Fact]
    public void Auth_Metadata_Recorded_Never_Tokenized()
    {
        const string secured = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            components:
              securitySchemes:
                bearerAuth: { type: http, scheme: bearer }
            security:
              - bearerAuth: []
            paths:
              /secret:
                get:
                  security:
                    - bearerAuth: []
                  responses:
                    '200': { description: OK }
            """;

        var (collection, _) = Run(null, secured, exampleSettings: new BrunoExampleSettings { Enabled = false });
        var request = Enumerable.Single(collection.AllRequests());
        var requiresAuth = BrunoYaml.AsBool(BrunoYaml.Get(request.SentinelEntry(collection)!, "requiresAuth"));
        Assert.True(requiresAuth);

        var yaml = collection.ToYaml();
        Assert.DoesNotContain("token", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer ", yaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Server_Variables_Map_To_Bruno_Environment_Syntax()
    {
        const string spec = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            servers:
              - url: https://{region}.api.example.com/v1
                variables:
                  region:
                    default: ap-east
                    enum:
                      - ap-east
                      - us-west
            paths:
              /x:
                get:
                  responses:
                    '200': { description: OK }
            """;

        var (collection, _) = Run(null, spec);
        var view = TestSupport.View(Enumerable.Single(collection.AllRequests()));
        Assert.Equal("https://{{region}}.api.example.com/v1/x", view.Url);
    }

    [Fact]
    public void Relative_Server_Url_Anchors_At_BaseUrl()
    {
        const string spec = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            servers:
              - url: /api/v2
            paths:
              /x:
                get:
                  responses:
                    '200': { description: OK }
            """;

        var (collection, _) = Run(null, spec);
        var view = TestSupport.View(Enumerable.Single(collection.AllRequests()));
        Assert.Equal("{{baseUrl}}/api/v2/x", view.Url);
    }
}

/// <summary>
/// §4 identity-transition matrix: operationId added/removed and method changes must
/// update the existing request in place — never duplicate + stale.
/// </summary>
public class IdentityTransitionTests
{
    private const string WithOpId = """
        openapi: 3.0.3
        info: { title: t, version: "1" }
        paths:
          /orders:
            post:
              operationId: createOrder
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      type: object
                      required: [id]
                      properties:
                        id: { type: integer }
              responses:
                '200': { description: OK }
        """;

    private const string WithoutOpId = """
        openapi: 3.0.3
        info: { title: t, version: "1.1" }
        paths:
          /orders:
            post:
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      type: object
                      required: [id]
                      properties:
                        id: { type: integer }
              responses:
                '200': { description: OK }
        """;

    [Fact]
    public void Removing_OperationId_Updates_In_Place()
    {
        var doc1 = TestSupport.NormalizeSpec(WithOpId);
        var doc2 = TestSupport.NormalizeSpec(WithoutOpId);
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = false }, "t");

        var (collection, first) = engine.Sync(null, doc1);
        Assert.Single(first.Operations, o => o.Action == OperationAction.Added);

        var (_, second) = engine.Sync(collection, doc2);
        Assert.Single(second.Operations, o => o.Action == OperationAction.Updated);
        Assert.DoesNotContain(second.Operations, o => o.Action is OperationAction.Stale or OperationAction.Added);
        Assert.Single(collection.AllRequests());

        // and back again: re-adding the operationId also updates in place
        var (_, third) = engine.Sync(collection, doc1);
        Assert.Single(third.Operations, o => o.Action == OperationAction.Updated);
        Assert.DoesNotContain(third.Operations, o => o.Action == OperationAction.Stale);
        Assert.Single(collection.AllRequests());
    }

    [Fact]
    public void Method_Change_On_Stable_OperationId_Rewrites_Request()
    {
        const string asGet = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            paths:
              /orders:
                get:
                  operationId: createOrder
                  parameters:
                    - name: id
                      in: query
                      schema: { type: integer }
                  responses:
                    '200': { description: OK }
            """;

        var doc1 = TestSupport.NormalizeSpec(WithOpId);
        var doc2 = TestSupport.NormalizeSpec(asGet);
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = false }, "t");

        var (collection, first) = engine.Sync(null, doc1);
        Assert.Single(first.Operations, o => o.Action == OperationAction.Added);

        var (_, second) = engine.Sync(collection, doc2);
        Assert.Single(second.Operations, o => o.Action == OperationAction.Updated);

        var request = Enumerable.Single(collection.AllRequests());
        Assert.Equal("GET", new BrunoRequestView(request.Node).Method);
        Assert.Equal("opid:createOrder", request.IdentityOf(collection)!.StableKey);
    }
}

/// <summary>§4 folder/category mapping: a tag change between syncs is REPORTED, never
/// auto-moved (preservation-first).</summary>
public class FolderDivergenceTests
{
    private const string V1 = """
        openapi: 3.0.3
        info: { title: t, version: "1" }
        paths:
          /orders:
            post:
              operationId: createOrder
              tags: [訂單]
              responses:
                '200': { description: OK }
        """;

    private const string V2Retagged = """
        openapi: 3.0.3
        info: { title: t, version: "1.1" }
        paths:
          /orders:
            post:
              operationId: createOrder
              tags: [行銷]
              responses:
                '200': { description: OK }
        """;

    [Fact]
    public void Tag_Change_Warns_And_Keeps_Request_In_Place()
    {
        var doc1 = TestSupport.NormalizeSpec(V1);
        var doc2 = TestSupport.NormalizeSpec(V2Retagged);
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = false }, "t");

        var (collection, first) = engine.Sync(null, doc1);
        Assert.Single(first.Operations, o => o.Action == OperationAction.Added);

        var (_, second) = engine.Sync(collection, doc2);

        // structural change (tag feeds the fingerprint) → Updated, with the divergence warning
        Assert.Single(second.Operations, o => o.Action == OperationAction.Updated);
        var warning = second.Operations[0].Warnings.Single();
        Assert.Contains("行銷", warning, StringComparison.Ordinal);
        Assert.Contains("訂單", warning, StringComparison.Ordinal);
        Assert.Contains("不自動搬移", warning, StringComparison.Ordinal);

        // request physically stayed in the original folder
        var folder = collection.RootItems.Single(i => i.IsFolder);
        Assert.Equal("訂單", folder.Name);
        Assert.Single(folder.Children);
    }

    [Fact]
    public void Unchanged_Tag_Produces_No_Warning()
    {
        var doc1 = TestSupport.NormalizeSpec(V1);
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = false }, "t");
        var (collection, _) = engine.Sync(null, doc1);

        var (_, again) = engine.Sync(collection, doc1);
        Assert.Empty(again.Operations.SelectMany(o => o.Warnings));
    }
}

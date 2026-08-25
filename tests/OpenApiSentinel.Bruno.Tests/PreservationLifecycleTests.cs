using System.Text.Json.Nodes;
using OpenApiSentinel.Bruno.Model;
using OpenApiSentinel.Bruno.Scenarios;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>
/// The §6-mandated lifecycle: first sync creates the collection; a human edits values,
/// scripts, tests and docs; the contract evolves; a second sync updates only
/// contract-derived fields while every piece of manual content survives.
/// </summary>
public class PreservationLifecycleTests
{
    private const string V1 = """
        openapi: 3.0.3
        info:
          title: 訂單 API
          version: "1.0"
        servers:
          - url: https://api.example.com/v1
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
                      required: [customerId]
                      properties:
                        customerId:
                          type: integer
                          minimum: 1
                        memo:
                          type: string
              responses:
                '200': { description: OK }
          /health:
            get:
              operationId: health
              responses:
                '200': { description: OK }
        """;

    private const string V2 = """
        openapi: 3.0.3
        info:
          title: 訂單 API
          version: "1.1"
        servers:
          - url: https://api.example.com/v1
        paths:
          /orders:
            post:
              operationId: createOrder
              summary: 建立訂單（新版）
              tags: [訂單]
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      type: object
                      required: [customerId, channel]
                      properties:
                        customerId:
                          type: integer
                          minimum: 1
                          maximum: 88888888
                        channel:
                          type: string
                          enum: [WEB, APP]
                        memo:
                          type: string
              responses:
                '200': { description: OK }
        """;

    [Fact]
    public void Full_Lifecycle_Preserves_Manual_Content_Across_Contract_Changes()
    {
        // ---- first sync ----
        var doc1 = TestSupport.NormalizeSpec(V1);
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings(),
            new BrunoExampleSettings { Enabled = false },
            toolVersion: "test");
        var (collection, firstOutcome) = engine.Sync(null, doc1);

        Assert.Equal(2, firstOutcome.AddedCount);

        // ---- human edits ----
        var order = TestSupport.FindRequest(collection, "建立訂單")!;
        var view = TestSupport.View(order);
        Assert.NotNull(view.JsonBodyRaw);

        var body = JsonNode.Parse(view.JsonBodyRaw!)!.AsObject();
        body["memo"] = "人工輸入的備忘";                       // manual value
        view.SetJsonBody(body.ToJsonString());

        var runtime = new YamlDotNet.RepresentationModel.YamlMappingNode();
        var scripts = new YamlDotNet.RepresentationModel.YamlSequenceNode();
        var beforeScript = new YamlDotNet.RepresentationModel.YamlMappingNode();
        BrunoYaml.SetString(beforeScript, "type", "before-request");
        BrunoYaml.Set(beforeScript, "code", new YamlDotNet.RepresentationModel.YamlScalarNode("console.log('我的腳本');")
        {
            Style = YamlDotNet.Core.ScalarStyle.Literal,
        });
        scripts.Children.Add(beforeScript);
        var tests = new YamlDotNet.RepresentationModel.YamlMappingNode();
        BrunoYaml.SetString(tests, "type", "tests");
        BrunoYaml.Set(tests, "code", new YamlDotNet.RepresentationModel.YamlScalarNode("expect(res.getStatus()).to.eq(200);")
        {
            Style = YamlDotNet.Core.ScalarStyle.Literal,
        });
        scripts.Children.Add(tests);
        runtime.Children.Add(BrunoYaml.Scalar("scripts"), scripts);
        order.Node.Children.Add(BrunoYaml.Scalar("runtime"), runtime);   // scripts + tests

        BrunoYaml.SetString(order.Node, "docs", "# 人工文件\n請勿覆寫。");   // docs

        var auth = new YamlDotNet.RepresentationModel.YamlMappingNode();
        BrunoYaml.SetString(auth, "mode", "bearer");
        var bearer = new YamlDotNet.RepresentationModel.YamlMappingNode();
        BrunoYaml.SetString(bearer, "token", "{{myToken}}");
        auth.Children.Add(BrunoYaml.Scalar("bearer"), bearer);
        var http = new BrunoRequestView(order.Node).Http;
        http.Children.Add(BrunoYaml.Scalar("auth"), auth);               // auth

        // ---- contract evolves ----
        var doc2 = TestSupport.NormalizeSpec(V2);
        var (_, secondOutcome) = engine.Sync(collection, doc2);

        var updated = secondOutcome.Operations.Single(o => o.OperationId == "createOrder");
        Assert.Equal(OperationAction.Updated, updated.Action);

        // removed operation reported stale, file kept
        Assert.Contains(secondOutcome.Operations, o => o.Action == OperationAction.Stale && o.Path == "/health");

        // ---- everything manual survived ----
        var collectionAfter = collection; // mutated in place by design
        var orderAfter = TestSupport.FindRequest(collectionAfter, "建立訂單")!;
        var viewAfter = TestSupport.View(orderAfter);

        var bodyAfter = TestSupport.BodyJson(orderAfter)!;
        Assert.Equal("人工輸入的備忘", bodyAfter["memo"]!.GetValue<string>());     // value preserved

        Assert.True(bodyAfter.ContainsKey("channel"));                            // new required added
        Assert.Equal("WEB", bodyAfter["channel"]!.GetValue<string>());            // first enum

        var runtimeAfter = BrunoYaml.Get(orderAfter.Node, "runtime") as YamlDotNet.RepresentationModel.YamlMappingNode;
        Assert.NotNull(runtimeAfter);                                            // scripts/tests intact
        var yaml = collectionAfter.ToYaml();
        Assert.Contains("我的腳本", yaml, StringComparison.Ordinal);
        Assert.Contains("expect(res.getStatus()).to.eq(200)", yaml, StringComparison.Ordinal);
        Assert.Contains("# 人工文件", yaml, StringComparison.Ordinal);             // docs preserved
        Assert.Contains("{{myToken}}", yaml, StringComparison.Ordinal);           // auth preserved

        Assert.Equal(OperationAction.Stale,
            secondOutcome.Operations.Single(o => o.Path == "/health").Action);
        Assert.Contains(updated.Preserved, p => p.Contains("主體值", StringComparison.Ordinal));

        // summary/description change alone does NOT rewrite preserved docs
        Assert.StartsWith("# 人工文件", new BrunoRequestView(orderAfter.Node).Docs!, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_Manual_Body_Is_Never_Overwritten()
    {
        var doc = TestSupport.NormalizeSpec(V1);
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = false }, "t");
        var (collection, _) = engine.Sync(null, doc);

        var order = TestSupport.FindRequest(collection, "建立訂單")!;
        var view = TestSupport.View(order);
        view.SetJsonBody("{ this is not json !!!");

        // Force a structural change so MergeBody runs.
        var v2 = V2.Replace("channel:\n                          type: string\n                          enum: [WEB, APP]",
                            "channel:\n                          type: string", StringComparison.Ordinal)
                   .Replace("required: [customerId, channel]", "required: [customerId]", StringComparison.Ordinal);
        var doc2 = TestSupport.NormalizeSpec(v2);
        var (_, outcome) = engine.Sync(collection, doc2);

        var orderAfter = TestSupport.FindRequest(collection, "建立訂單")!;
        Assert.Equal("{ this is not json !!!", TestSupport.View(orderAfter).JsonBodyRaw);
        var opOutcome = outcome.Operations.Single(o => o.OperationId == "createOrder");
        Assert.Contains(opOutcome.Warnings, w => w.Contains("JSON", StringComparison.Ordinal));
    }
}

/// <summary>Layer-2 business scenarios flow into examples alongside generated ones.</summary>
public class BusinessScenarioMergeTests
{
    private const string Spec = """
        openapi: 3.0.3
        info:
          title: t
          version: "1"
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
                      required: [customerId]
                      properties:
                        customerId: { type: integer }
              responses:
                '200': { description: OK }
        """;

    private static IReadOnlyList<BusinessScenario> Scenarios() =>
    [
        new BusinessScenario
        {
            OperationId = "createOrder",
            Name = "成功－一般訂單",
            Description = "使用有效客戶建立訂單。",
            Body = new JsonObject { ["customerId"] = 10001 },
            ExpectedStatus = 200,
            ExpectDescription = "訂單建立成功",
        },
        new BusinessScenario
        {
            OperationId = "createOrder",
            Name = "失敗－客戶不存在",
            Body = new JsonObject { ["customerId"] = 999999999 },
            ExpectedStatus = 404,
        },
    ];

    [Fact]
    public void Business_Scenarios_Become_Examples_With_Their_Own_Bodies()
    {
        var doc = TestSupport.NormalizeSpec(Spec);
        var generator = new ScenarioGenerator(new BrunoExampleSettings());
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings(),
            new BrunoExampleSettings(),
            "t",
            generator,
            Scenarios());

        var (collection, outcome) = engine.Sync(null, doc);
        Assert.Empty(outcome.Warnings);

        var request = Enumerable.First(collection.AllRequests());
        var examples = BrunoYaml.SequenceOf(request.Node, "examples");
        Assert.NotNull(examples);

        var names = examples!.Children
            .OfType<YamlDotNet.RepresentationModel.YamlMappingNode>()
            .Select(n => BrunoYaml.AsString(BrunoYaml.Get(n, "name")))
            .ToList();

        Assert.Contains("成功－一般訂單", names);
        Assert.Contains("失敗－客戶不存在", names);
        Assert.Contains("成功－完整參數", names); // auto scenario also present

        var bizExample = examples!.Children
            .OfType<YamlDotNet.RepresentationModel.YamlMappingNode>()
            .Single(n => BrunoYaml.AsString(BrunoYaml.Get(n, "name")) == "成功－一般訂單");
        var req = BrunoYaml.Get(bizExample, "request") as YamlDotNet.RepresentationModel.YamlMappingNode;
        var bodyNode = BrunoYaml.Get(req!, "body") as YamlDotNet.RepresentationModel.YamlMappingNode;
        var data = BrunoYaml.AsString(BrunoYaml.Get(bodyNode!, "data"));
        Assert.Contains("10001", data, StringComparison.Ordinal);
    }

    [Fact]
    public void Business_Scenario_File_Rejections()
    {
        const string badFile = """
            scenarios:
              - name: 缺少 operationId
              - operationId: noSuchOp
                name: 不存在的操作
              - operationId: createOrder
                name: 重複
              - operationId: createOrder
                name: 重複
            """;

        var tmp = Path.Combine(Path.GetTempPath(), $"sentinel-sc-{Guid.NewGuid():N}.yml");
        File.WriteAllText(tmp, badFile);
        try
        {
            var doc = TestSupport.NormalizeSpec(Spec);
            var result = BusinessScenarioFile.Load(tmp, doc);

            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("operationId", StringComparison.Ordinal));
            Assert.Contains(result.Errors, e => e.Contains("不存在", StringComparison.Ordinal));
            Assert.Contains(result.Errors, e => e.Contains("重複", StringComparison.Ordinal));
            Assert.Single(result.Scenarios, s => s.Name == "重複"); // first kept, duplicate rejected
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Removed_Business_Scenario_Is_Cleaned_Up_On_Resync()
    {
        var doc = TestSupport.NormalizeSpec(Spec);
        var generator = new ScenarioGenerator(new BrunoExampleSettings());

        var engineWithBiz = new BrunoSyncEngine(
            new BrunoSyncSettings(), new BrunoExampleSettings(), "t", generator, Scenarios());
        var (collection, _) = engineWithBiz.Sync(null, doc);

        var engineNoBiz = new BrunoSyncEngine(
            new BrunoSyncSettings(), new BrunoExampleSettings(), "t", generator, []);
        var (collection2, _) = engineNoBiz.Sync(collection, doc);

        var request = Enumerable.First(collection2.AllRequests());
        var names = BrunoYaml.SequenceOf(request.Node, "examples")!.Children
            .OfType<YamlDotNet.RepresentationModel.YamlMappingNode>()
            .Select(n => BrunoYaml.AsString(BrunoYaml.Get(n, "name")));
        Assert.DoesNotContain("成功－一般訂單", names);
        Assert.DoesNotContain("失敗－客戶不存在", names);
    }
}

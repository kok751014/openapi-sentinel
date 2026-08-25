using System.Text;
using OpenApiSentinel.Bruno;
using OpenApiSentinel.Bruno.Model;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>Deep structural equality over representation-model trees.</summary>
public static class YamlAssert
{
    public static void Equal(YamlNode expected, YamlNode actual, string? path = null)
    {
        path ??= "$";
        if (expected is YamlScalarNode expectedScalar && actual is YamlScalarNode actualScalar)
        {
            Assert.Equal(expectedScalar.Value, actualScalar.Value);
            return;
        }

        if (expected is YamlSequenceNode expectedSeq && actual is YamlSequenceNode actualSeq)
        {
            Assert.Equal(expectedSeq.Children.Count, actualSeq.Children.Count);
            for (var i = 0; i < expectedSeq.Children.Count; i++)
            {
                Equal(expectedSeq.Children[i], actualSeq.Children[i], $"{path}[{i}]");
            }

            return;
        }

        if (expected is YamlMappingNode expectedMap && actual is YamlMappingNode actualMap)
        {
            var expectedKeys = Keys(expectedMap).ToHashSet(StringComparer.Ordinal);
            var actualKeys = Keys(actualMap).ToHashSet(StringComparer.Ordinal);
            Assert.True(expectedKeys.SetEquals(actualKeys),
                $"Key mismatch at {path}: missing=[{string.Join(",", expectedKeys.Except(actualKeys))}] extra=[{string.Join(",", actualKeys.Except(expectedKeys))}]");
            foreach (var key in expectedKeys)
            {
                Equal(GetRequired(expectedMap, key), GetRequired(actualMap, key), $"{path}.{key}");
            }

            return;
        }

        Assert.Fail($"Node kind mismatch at {path}: {expected.GetType().Name} vs {actual.GetType().Name}");
    }

    private static IEnumerable<string> Keys(YamlMappingNode map) =>
        map.Children.Keys.Select(k => ((YamlScalarNode)k).Value!).Where(v => v is not null);

    private static YamlNode GetRequired(YamlMappingNode map, string key)
    {
        foreach (var entry in map.Children)
        {
            if (((YamlScalarNode)entry.Key).Value == key)
            {
                return entry.Value;
            }
        }

        throw new InvalidOperationException($"Key '{key}' not found.");
    }
}

public static class TestYaml
{
    public static YamlMappingNode Parse(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }
}

public class BrunoYamlRoundTripTests
{
    private const string RichCollection = """
        opencollection: "1.0.0"
        info:
          name: 訂單管理 API
          version: 1.2.0
        extensions:
          bruno:
            ignore:
              - node_modules
              - .git
          x-custom-vendor:
            anything: goes here
            nested:
              - 1
              - two
              - three: null
        items:
          - info:
              name: 訂單
              type: folder
              seq: 1
            docs:
              content: "# 訂單 API\n\n人工維護的資料夾說明。"
              type: text/markdown
            items:
              - info:
                  name: 建立訂單
                  type: http
                  seq: 1
                  tags:
                    - core
                http:
                  method: POST
                  url: "{{baseUrl}}/orders/:id"
                  params:
                    - name: id
                      value: "12345"
                      type: path
                    - name: verbose
                      value: "true"
                      disabled: true
                  headers:
                    - name: Content-Type
                      value: application/json
                      enabled: true
                    - name: X-Custom-Manual
                      value: keep-me
                      enabled: true
                  body:
                    type: json
                    data: |
                      {
                        "customerId": 10001,
                        "note": "人工輸入的值"
                      }
                  auth:
                    mode: inherit
                runtime:
                  scripts:
                    - type: before-request
                      code: |
                        // 使用者腳本，必須保留
                        console.log(req.getUrl());
                    - type: tests
                      code: |
                        expect(res.getStatusCode()).to.equal(200);
                settings:
                  timeout: 5000
                x-sentinel:
                  operationId: createOrder
                  method: POST
                  path: /orders/{id}
                  identityKey: opid:createOrder
                  managedHeaders:
                    - Content-Type
          - info:
              name: 獨立請求（無標記）
              type: http
              seq: 2
            http:
              method: GET
              url: https://api.example.com/ping
        """;

    [Fact]
    public void RoundTrip_Preserves_All_Known_And_Unknown_Keys()
    {
        var original = TestYaml.Parse(RichCollection);
        var doc = BrunoCollectionDocument.Parse(RichCollection, "test.yml");
        var serialized = doc.ToYaml();

        var reparsed = TestYaml.Parse(serialized);
        YamlAssert.Equal(original, reparsed);
    }

    [Fact]
    public void RoundTrip_Twice_Is_Stable()
    {
        var once = BrunoCollectionDocument.Parse(RichCollection, "t.yml").ToYaml();
        var twice = BrunoCollectionDocument.Parse(once, "t.yml").ToYaml();
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Typed_Facades_Read_Rich_File()
    {
        var doc = BrunoCollectionDocument.Parse(RichCollection, "t.yml");

        Assert.Equal("訂單管理 API", doc.CollectionName);

        var folder = doc.RootItems.First(i => i.IsFolder);
        Assert.Equal("訂單", folder.Name);
        var request = folder.Children.Single(c => c.IsHttpRequest);
        var view = new BrunoRequestView(request.Node);

        Assert.Equal("POST", view.Method);
        Assert.Equal("{{baseUrl}}/orders/:id", view.Url);
        Assert.Equal("json", view.BodyType);
        Assert.Contains("人工輸入的值", view.JsonBodyRaw);
        Assert.Equal(2, view.Params().Count);
        Assert.False(view.Params()[1].Enabled);
        Assert.True(request.IsSentinelManaged);
    }

    [Fact]
    public void Mutation_Keeps_Unknown_Siblings()
    {
        var doc = BrunoCollectionDocument.Parse(RichCollection, "t.yml");
        var folder = doc.RootItems.First(i => i.IsFolder);
        var item = folder.Children.Single(c => c.IsHttpRequest);
        var view = new BrunoRequestView(item.Node);

        view.Method = "PUT";
        view.SetJsonBody("{ \"replaced\": true }");

        var docAfter = BrunoCollectionDocument.Parse(doc.ToYaml(), "t.yml");
        var folderAfter = docAfter.RootItems.First(i => i.IsFolder);
        var itemAfter = folderAfter.Children.Single(c => c.IsHttpRequest);
        var viewAfter = new BrunoRequestView(itemAfter.Node);

        Assert.Equal("PUT", viewAfter.Method);
        Assert.Contains("\"replaced\"", viewAfter.JsonBodyRaw);
        // scripts preserved untouched
        Assert.NotNull(BrunoYaml.Get(itemAfter.Node, "runtime"));
        Assert.Equal("keep-me", viewAfter.Headers().First(h => h.Name == "X-Custom-Manual").Value);
        // sentinel block intact
        Assert.Equal("createOrder", BrunoYaml.AsString(
            BrunoYaml.Get(itemAfter.SentinelOrNull!, "operationId")));
    }

    [Fact]
    public void Parse_Rejects_NonOpenCollection_Yaml()
    {
        var ex = Assert.Throws<BrunoFormatException>(() =>
            BrunoCollectionDocument.Parse("openapi: 3.1.0\ninfo:\n  title: x\n", "spec.yml"));
        Assert.Contains("opencollection", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Rejects_Invalid_Yaml_With_Actionable_Error()
    {
        var ex = Assert.Throws<BrunoFormatException>(() =>
            BrunoCollectionDocument.Parse("opencollection: \"1.0.0\"\ninfo: [unclosed\n", "bad.yml"));
        Assert.Contains("not valid YAML", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateNew_Has_Marker_And_Name_And_Serializes()
    {
        var doc = BrunoCollectionDocument.CreateNew("我的集合");
        var yaml = doc.ToYaml();
        var reparsed = BrunoCollectionDocument.Parse(yaml, "new.yml");
        Assert.Equal("我的集合", reparsed.CollectionName);
    }

    [Fact]
    public void GetOrCreateFolderPath_Creates_Nested_Folders_Idempotently()
    {
        var doc = BrunoCollectionDocument.CreateNew("c");
        doc.GetOrCreateFolderPath(["訂單", "查詢"]);
        doc.GetOrCreateFolderPath(["訂單", "查詢"]);
        doc.GetOrCreateFolderPath(["訂單", "寫入"]);

        var yaml = doc.ToYaml();
        var reparsed = BrunoCollectionDocument.Parse(yaml, "c.yml");
        var orderFolder = reparsed.RootItems.Single(i => i.IsFolder);
        Assert.Equal("訂單", orderFolder.Name);
        var childNames = orderFolder.Children.Select(c => c.Name ?? "").ToList();
        Assert.Equal(new List<string> { "查詢", "寫入" }, childNames); // stable insertion order
    }

    [Fact]
    public void Literal_Block_Style_For_Bodies_Survives_Reparse_As_Text()
    {
        var doc = BrunoCollectionDocument.CreateNew("c");
        var seq = doc.ItemsSequence();
        var item = new YamlMappingNode();
        var info = new YamlMappingNode();
        BrunoYaml.SetString(info, "name", "r1");
        BrunoYaml.SetString(info, "type", "http");
        item.Children.Add(BrunoYaml.Scalar("info"), info);
        var view = new BrunoRequestView(item);
        view.Method = "POST";
        view.Url = "{{baseUrl}}/x";
        view.SetJsonBody("{\n  \"multi\": \"line\",\n  \"n\": 1\n}");
        seq.Children.Add(item);

        var yaml = doc.ToYaml();
        Assert.Contains("data: |", yaml, StringComparison.Ordinal); // literal block emitted
        var back = BrunoCollectionDocument.Parse(yaml, "c.yml").AllRequests().Single();
        Assert.Contains("\"multi\": \"line\"", new BrunoRequestView(back.Node).JsonBodyRaw, StringComparison.Ordinal);
    }
}

using System.Text.RegularExpressions;
using OpenApiSentinel.Bruno.Model;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>Regression coverage for Bruno v2's native POST/PUT wire format.</summary>
public sealed class NativePostPutHotfixTests
{
    [Fact]
    public void Native_Post_Put_Bodies_Use_Official_Bruno_V2_Modes_And_File_Syntax()
    {
        var collection = Sync(HotfixSpec);
        var plan = BruWorkspaceWriter.Plan(collection, Path.Combine(Path.GetTempPath(), "sentinel-native-hotfix"), null);

        var create = RequestFile(plan, "createAnimal");
        var update = RequestFile(plan, "updateAnimal");
        var createMovement = RequestFile(plan, "createMovement");
        var updateMovement = RequestFile(plan, "updateMovement");
        var login = RequestFile(plan, "login");
        var replaceText = RequestFile(plan, "replaceNote");
        var replaceXml = RequestFile(plan, "replaceXml");
        var getStatus = RequestFile(plan, "getStatus");

        Assert.Equal("multipartForm", OfficialBrunoV2Semantics.HttpBodyMode(create));
        Assert.Equal("multipartForm", OfficialBrunoV2Semantics.HttpBodyMode(update));
        Assert.Equal("json", OfficialBrunoV2Semantics.HttpBodyMode(createMovement));
        Assert.Equal("json", OfficialBrunoV2Semantics.HttpBodyMode(updateMovement));
        Assert.Equal("formUrlEncoded", OfficialBrunoV2Semantics.HttpBodyMode(login));
        Assert.Equal("text", OfficialBrunoV2Semantics.HttpBodyMode(replaceText));
        Assert.Equal("xml", OfficialBrunoV2Semantics.HttpBodyMode(replaceXml));
        Assert.Null(OfficialBrunoV2Semantics.HttpBodyMode(getStatus));

        foreach (var nativeMultipart in new[] { create, update })
        {
            Assert.Contains("Files: @file(choose-a-file-to-upload)", nativeMultipart, StringComparison.Ordinal);
            Assert.DoesNotContain("Files: @choose-a-file-to-upload", nativeMultipart, StringComparison.Ordinal);

            var examples = OfficialBrunoV2Semantics.Examples(nativeMultipart).ToList();
            Assert.Equal(["成功－完整參數", "成功－僅輸入必填參數"], examples.Take(2).Select(x => x.Name));
            Assert.All(examples, x => Assert.False(string.IsNullOrWhiteSpace(x.Name)));
            Assert.All(examples, x => Assert.Equal("multipartForm", x.BodyMode));
            Assert.All(examples, x => Assert.Contains("body:multipart-form: {", x.Content, StringComparison.Ordinal));

            var full = examples.Single(x => x.Name == "成功－完整參數");
            Assert.Contains("Tags: 1", full.Content, StringComparison.Ordinal);
            Assert.Contains("Tags: 1", full.Content, StringComparison.Ordinal);
            Assert.Contains("Files: @file(choose-a-file-to-upload)", full.Content, StringComparison.Ordinal);

            var requiredOnly = examples.Single(x => x.Name == "成功－僅輸入必填參數");
            Assert.Contains("AnimalId:", requiredOnly.Content, StringComparison.Ordinal);
            Assert.Contains("Name:", requiredOnly.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("Tags:", requiredOnly.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("Files:", requiredOnly.Content, StringComparison.Ordinal);
        }

        Assert.All(OfficialBrunoV2Semantics.Examples(createMovement).Concat(OfficialBrunoV2Semantics.Examples(updateMovement)),
            example =>
            {
                Assert.False(string.IsNullOrWhiteSpace(example.Name));
                Assert.Equal("json", example.BodyMode);
            });
    }

    [Fact]
    public void Official_File_Syntax_RoundTrips_To_Canonical_File_Rows_And_Preserves_Manual_Paths()
    {
        const string source = """
            meta {
              name: Upload
              type: http
              seq: 1
            }

            put {
              url: https://example.test/upload
              body: multipartForm
            }

            body:multipart-form {
              primary: @file(choose-a-file-to-upload)
              attachments: @file(manual-a.pdf|manual-b.pdf)
            }
            """;

        var (item, _) = BruItemMapper.ToCanonical(BruMarkup.Parse(source, "upload.bru"), "upload.bru");
        var rows = new BrunoRequestView(item.Node).FormRows().ToList();

        Assert.All(rows, row => Assert.True(row.IsFile));
        Assert.Equal("choose-a-file-to-upload", rows.Single(row => row.Name == "primary").Value);
        Assert.Equal("manual-a.pdf|manual-b.pdf", rows.Single(row => row.Name == "attachments").Value);

        var reserialized = BruMarkup.Serialize(BruItemWriter.Build(item, sentinelEntry: null));
        Assert.Contains("primary: @file(choose-a-file-to-upload)", reserialized, StringComparison.Ordinal);
        Assert.Contains("attachments: @file(manual-a.pdf|manual-b.pdf)", reserialized, StringComparison.Ordinal);
    }

    private static BrunoCollectionDocument Sync(string spec)
    {
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings(),
            new BrunoExampleSettings { Enabled = true, MaxPerOperation = 8 },
            toolVersion: "test",
            generator: new ScenarioGenerator(new BrunoExampleSettings { Enabled = true, MaxPerOperation = 8 }));
        return engine.Sync(existing: null, TestSupport.NormalizeSpec(spec)).Collection;
    }

    private static string RequestFile(WorkspacePlan plan, string operationId) =>
        plan.FilesToWrite.Single(file => file.Path.EndsWith(".bru", StringComparison.Ordinal) &&
            file.Content.Contains("x-sentinel-operation-id: " + operationId, StringComparison.Ordinal)).Content;

    private const string HotfixSpec = """
        openapi: 3.0.3
        info:
          title: Native hotfix
          version: "1"
        servers:
          - url: https://example.test
        paths:
          /animals:
            post:
              operationId: createAnimal
              summary: 新增動物
              requestBody:
                required: true
                content:
                  multipart/form-data:
                    schema:
                      type: object
                      required: [AnimalId, Name]
                      properties:
                        AnimalId: { type: integer }
                        Name: { type: string }
                        Tags:
                          type: array
                          items: { type: integer }
                        Files: { type: string, format: binary }
              responses:
                '200': { description: OK }
            put:
              operationId: updateAnimal
              summary: 更新動物
              requestBody:
                required: true
                content:
                  multipart/form-data:
                    schema:
                      type: object
                      required: [AnimalId, Name]
                      properties:
                        AnimalId: { type: integer }
                        Name: { type: string }
                        Tags:
                          type: array
                          items: { type: integer }
                        Files: { type: string, format: binary }
              responses:
                '200': { description: OK }
          /movements:
            post:
              operationId: createMovement
              summary: 新增移動紀錄
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      type: object
                      required: [AnimalId]
                      properties:
                        AnimalId: { type: integer }
                        Note: { type: string }
              responses:
                '200': { description: OK }
          /movements/{id}:
            put:
              operationId: updateMovement
              summary: 更新移動紀錄
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: integer }
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      type: object
                      required: [AnimalId]
                      properties:
                        AnimalId: { type: integer }
                        Note: { type: string }
              responses:
                '200': { description: OK }
          /login:
            post:
              operationId: login
              requestBody:
                required: true
                content:
                  application/x-www-form-urlencoded:
                    schema:
                      type: object
                      required: [username]
                      properties:
                        username: { type: string }
              responses:
                '200': { description: OK }
          /notes:
            put:
              operationId: replaceNote
              requestBody:
                required: true
                content:
                  text/plain:
                    schema: { type: string }
              responses:
                '200': { description: OK }
          /xml:
            put:
              operationId: replaceXml
              requestBody:
                required: true
                content:
                  application/xml:
                    schema: { type: string }
              responses:
                '200': { description: OK }
          /status:
            get:
              operationId: getStatus
              responses:
                '200': { description: OK }
        """;

    /// <summary>
    /// Small independent reader of the current official Bruno v2 wire grammar used here:
    /// HTTP <c>body:</c> is the request mode; examples carry <c>name:</c> and request
    /// <c>mode:</c>. It deliberately does not call Sentinel's .bru parser.
    /// </summary>
    private static class OfficialBrunoV2Semantics
    {
        public sealed record Example(string Name, string? BodyMode, string Content);

        public static string? HttpBodyMode(string text)
        {
            var method = Regex.Match(text, "(?ms)^(?:get|post|put|patch|delete|head|options|trace|connect) \\{\\r?\\n(?<body>.*?)^\\}");
            Assert.True(method.Success, "expected a Bruno HTTP method block");
            var body = Regex.Match(method.Groups["body"].Value, "(?m)^  body: (?<mode>[A-Za-z]+)\\r?$");
            return body.Success ? body.Groups["mode"].Value : null;
        }

        public static IEnumerable<Example> Examples(string text)
        {
            var starts = Regex.Matches(text, "(?m)^example \\{").Select(match => match.Index).ToList();
            for (var i = 0; i < starts.Count; i++)
            {
                var start = starts[i];
                var end = i + 1 < starts.Count ? starts[i + 1] : text.Length;
                var content = text[start..end];
                var name = Regex.Match(content, "(?m)^  name: (?<name>.+)\\r?$");
                var mode = Regex.Match(content, "(?m)^    mode: (?<mode>[A-Za-z]+)\\r?$");
                yield return new Example(name.Success ? name.Groups["name"].Value : "", mode.Success ? mode.Groups["mode"].Value : null, content);
            }
        }
    }
}

using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>
/// Layer-1 generator behavior: zh-TW naming, constraint-driven cases, deterministic
/// ranking, and the hard maxPerOperation cap.
/// </summary>
public class ScenarioGeneratorTests
{
    private static (ScenarioRequest Base, IReadOnlyList<GeneratedScenario> Scenarios) Generate(
        string spec,
        BrunoExampleSettings? settings = null)
    {
        var doc = TestSupport.NormalizeSpec(spec);
        var pathItem = doc.Paths.Values.Single();
        var operation = pathItem.Operations.Values.Single();
        var identity = OperationIdentity.From(operation);
        var baseUrl = BrunoSyncEngine.ResolveBaseUrl(doc);
        var url = BrunoSyncEngine.BuildUrl(baseUrl, identity.NormalizedPath);
        var parameters = new List<(string Name, string Value, string Type, bool Enabled)>();
        foreach (var parameter in operation.Parameters)
        {
            if (parameter.In is ParameterLocation.Query or ParameterLocation.Path)
            {
                parameters.Add((parameter.Name,
                    SampleValueFactory.ParameterSampleText(parameter),
                    parameter.In == ParameterLocation.Path ? "path" : "query", true));
            }
        }

        string? body = null;
        if (operation.RequestBody is { } requestBody &&
            ScenarioGenerator.PrimaryBodySchema(requestBody) is { } schema)
        {
            body = JsonBodyMerger.Merge(null, schema).Json;
        }

        var baseSnapshot = new ScenarioRequest(identity.Method, url, parameters,
            operation.RequestBody is null ? [] : [("Content-Type", "application/json", true)], body);

        var generator = new ScenarioGenerator(settings ?? new BrunoExampleSettings());
        return (baseSnapshot, generator.Generate(identity, operation, baseSnapshot));
    }

    [Fact]
    public void Generates_ZhTw_Success_Scenarios_With_Natural_Descriptions()
    {
        const string op = """
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
                      nickname:
                        type: string
            responses:
              '200': { description: OK }
            """;

        var (_, scenarios) = Generate(Spec.Post(op));

        Assert.Contains(scenarios, s => s.Name == "成功－完整參數");
        Assert.Contains(scenarios, s => s.Name == "成功－僅輸入必填參數");
        Assert.Equal("success-full", scenarios[0].Id);
        Assert.StartsWith("此範例使用", scenarios[0].Description, StringComparison.Ordinal);
        Assert.StartsWith("此範例僅填入", scenarios[1].Description, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredOnly_Skipped_When_No_Optional_Content()
    {
        const string op = """
            requestBody:
              required: true
              content:
                application/json:
                  schema:
                    type: object
                    required: [a]
                    properties:
                      a:
                        type: integer
            """;

        var (_, scenarios) = Generate(Spec.Post(op));
        Assert.DoesNotContain(scenarios, s => s.Id == "success-required-only");
    }

    [Fact]
    public void Enum_Min_Max_Length_Format_Violations_Are_Generated()
    {
        const string op = """
            requestBody:
              required: true
              content:
                application/json:
                  schema:
                    type: object
                    properties:
                      orderType:
                        type: string
                        enum: [NORMAL, URGENT]
                      quantity:
                        type: integer
                        minimum: 1
                        maximum: 100
                      name:
                        type: string
                        minLength: 2
                        maxLength: 10
                      email:
                        type: string
                        format: email
            """;

        var (_, scenarios) = Generate(Spec.Post(op), new BrunoExampleSettings { MaxPerOperation = 32 });

        Assert.Contains(scenarios, s => s.Name == "失敗－orderType 使用無效列舉值");
        Assert.Contains(scenarios, s => s.Name == "失敗－quantity 低於允許最小值");
        Assert.Contains(scenarios, s => s.Name == "失敗－quantity 超過允許最大值");
        Assert.Contains(scenarios, s => s.Name == "邊界值－quantity 使用最小允許值");
        Assert.Contains(scenarios, s => s.Name == "邊界值－quantity 使用最大允許值");
        Assert.Contains(scenarios, s => s.Name == "失敗－name 超過最大長度");
        Assert.Contains(scenarios, s => s.Name == "失敗－email 格式不正確");

        // spot check payload content
        var invalidEnum = scenarios.Single(s => s.Name == "失敗－orderType 使用無效列舉值");
        Assert.Contains("__sentinel_invalid_enum__", invalidEnum.Request.JsonBody ?? "", StringComparison.Ordinal);

        var overMax = scenarios.Single(s => s.Name == "失敗－name 超過最大長度");
        Assert.Contains(new string('a', 11), overMax.Request.JsonBody ?? "", StringComparison.Ordinal);

        var badEmail = scenarios.Single(s => s.Name == "失敗－email 格式不正確");
        Assert.Contains("not-an-email", badEmail.Request.JsonBody ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_Required_Body_Field_Case_Generated()
    {
        const string op = """
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
            """;

        var (_, scenarios) = Generate(Spec.Post(op));
        var missing = scenarios.Single(s => s.Name == "失敗－缺少 customerId");
        Assert.DoesNotContain("\"customerId\"", missing.Request.JsonBody ?? "", StringComparison.Ordinal);
        Assert.Contains("`customerId`", missing.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_Type_And_Array_Item_Count_Cases_Generated()
    {
        const string op = """
            requestBody:
              required: true
              content:
                application/json:
                  schema:
                    type: object
                    properties:
                      tags:
                        type: array
                        minItems: 2
                        maxItems: 4
                        items:
                          type: string
            """;

        var (_, scenarios) = Generate(Spec.Post(op));
        Assert.Contains(scenarios, s => s.Name == "失敗－tags 型別錯誤");       // array replaced by string
        Assert.Contains(scenarios, s => s.Name == "失敗－tags 項目數不足");     // empty array
        Assert.Contains(scenarios, s => s.Name == "失敗－tags 超過項目數上限"); // 5 items
        Assert.Contains(scenarios, s => s.Name == "邊界值－tags 使用最小項目數");
        Assert.Contains(scenarios, s => s.Name == "邊界值－tags 使用最大項目數");
    }

    [Fact]
    public void Query_Param_Constraints_Produce_Param_Level_Cases()
    {
        const string op = """
            parameters:
              - name: pageSize
                in: query
                schema:
                  type: integer
                  minimum: 1
                  maximum: 50
              - name: status
                in: query
                required: true
                schema:
                  type: string
                  enum: [open, closed]
            responses:
              '200': { description: OK }
            """;

        var (_, scenarios) = Generate(Spec.Post(op));
        var belowMin = scenarios.Single(s => s.Name == "失敗－pageSize 低於允許最小值");
        Assert.Equal("0", belowMin.Request.Params.Single(p => p.Name == "pageSize").Value);
        Assert.Equal("open", belowMin.Request.Params.Single(p => p.Name == "status").Value); // required param still present

        Assert.Contains(scenarios, s => s.Name == "失敗－status 使用無效列舉值");
    }

    [Fact]
    public void Nested_Body_Fields_Get_Dot_Path_Descriptions()
    {
        const string op = """
            requestBody:
              required: true
              content:
                application/json:
                  schema:
                    type: object
                    properties:
                      address:
                        type: object
                        properties:
                          zip:
                            type: integer
                            maximum: 99999
            """;

        var (_, scenarios) = Generate(Spec.Post(op));
        var overMax = scenarios.Single(s => s.Name == "失敗－zip 超過允許最大值");
        Assert.Contains("`address.zip`", overMax.Description, StringComparison.Ordinal);
        Assert.Equal("address.zip", overMax.Id.Replace("above-max-", "", StringComparison.Ordinal));
        Assert.NotEqual(-1, overMax.Id.IndexOf("address", StringComparison.Ordinal));
    }

    [Fact]
    public void MaxPerOperation_Cap_Is_Hard_And_Ranking_Is_Stable()
    {
        const string op = """
            parameters:
              - name: p1
                in: query
                schema: { type: integer, minimum: 5, maximum: 9 }
              - name: p2
                in: query
                schema: { type: integer, minimum: 5, maximum: 9 }
              - name: p3
                in: query
                schema: { type: integer, minimum: 5, maximum: 9 }
            requestBody:
              required: true
              content:
                application/json:
                  schema:
                    type: object
                    properties:
                      a: { type: string, pattern: "^[a-z]+$" }
            responses:
              '200': { description: OK }
            """;

        for (var run = 0; run < 3; run++)
        {
            var (_, scenarios) = Generate(Spec.Post(op), new BrunoExampleSettings { MaxPerOperation = 6 });
            Assert.Equal(6, scenarios.Count);
            // success scenarios always outrank violations; ranking identical across runs
            Assert.Equal("success-full", scenarios[0].Id);
            var ids = scenarios.Select(s => s.Id).ToList();
            var again = Generate(Spec.Post(op), new BrunoExampleSettings { MaxPerOperation = 6 }).Scenarios.Select(s => s.Id).ToList();
            Assert.Equal(ids, again);
        }
    }

    [Fact]
    public void Authentication_Is_Never_Scenario_Material()
    {
        const string op = """
            security:
              - bearerAuth: []
            requestBody:
              required: true
              content:
                application/json:
                  schema:
                    type: object
                    properties:
                      a: { type: string }
            components:
              securitySchemes:
                bearerAuth: { type: http, scheme: bearer }
            """;

        var (_, scenarios) = Generate(Spec.Post(op));
        foreach (var scenario in scenarios)
        {
            Assert.DoesNotContain("401", scenario.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("403", scenario.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("Token", scenario.Name + scenario.Description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("驗證失敗", scenario.Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Stable_Identifiers_Are_Ascii_Safe()
    {
        const string op = """
            requestBody:
              required: true
              content:
                application/json:
                  schema:
                    type: object
                    properties:
                      客戶編號: { type: integer, maximum: 9 }
            """;

        var (_, scenarios) = Generate(Spec.Post(op));
        var nonSuccess = scenarios.Where(s => !s.Id.StartsWith("success", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(nonSuccess);
        Assert.All(nonSuccess, s =>
            Assert.Matches("^[a-z0-9\\-]+$", s.Id));
    }
}


/// <summary>Builds a complete OpenAPI document around an operation-level YAML fragment.</summary>
public static class Spec
{
    public static string Post(string operationFragment) => """
        openapi: 3.0.3
        info:
          title: t
          version: "1"
        paths:
          /thing:
            post:
        """ + "\n" + Indent(Normalize(operationFragment), 10);

    /// <summary>Strips the fragment's common indentation so any raw-string layout works.</summary>
    public static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var minIndent = int.MaxValue;
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            minIndent = Math.Min(minIndent, line.Length - line.TrimStart().Length);
        }

        if (minIndent == int.MaxValue)
        {
            minIndent = 0;
        }

        return string.Join("\n", lines.Select(l => l.Length >= minIndent ? l[minIndent..] : l.TrimStart()));
    }

    public static string Indent(string text, int spaces) =>
        string.Join("\n", text.Split('\n').Select(l => l.Length == 0 ? l : new string(' ', spaces) + l));
}

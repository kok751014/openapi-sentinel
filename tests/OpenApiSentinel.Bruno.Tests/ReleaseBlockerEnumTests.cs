using System.Text.Json.Nodes;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>Shared fixtures/helpers for the final release-blocker regression tests.</summary>
public static class ReleaseBlockerSpec
{
    public const string Animals = """
        openapi: 3.0.0
        info:
          title: ssz-ams-api（動物管理）
          version: "1"
        servers:
          - url: http://localhost:8080
        paths:
          /api/Animals:
            post:
              operationId: createAnimal
              summary: 新增動物
              tags: [Animals]
              requestBody:
                required: true
                content:
                  multipart/form-data:
                    schema:
                      type: object
                      required: [SpeciesId, Name, Gender]
                      properties:
                        SpeciesId:
                          type: integer
                        Name:
                          type: string
                        Gender:
                          type: integer
                          enum: [1, 2]
                        Identification:
                          type: string
                        AreasId:
                          type: array
                          items: { type: integer }
                        Files:
                          type: string
                          format: binary
                        Remark:
                          type: string
              responses:
                '201': { description: OK }
          /api/Animals/export:
            post:
              operationId: exportAnimals
              summary: 匯出動物
              tags: [Animals]
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      type: array
                      items: { type: string, format: uuid }
              responses:
                '200': { description: OK }
          /api/Animals/animalsData/{id}:
            get:
              operationId: getAnimalsData
              summary: 查詢動物資料
              tags: [Animals]
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string, format: uuid }
              responses:
                '200': { description: OK }
          /api/Auth/login:
            post:
              operationId: login
              summary: 登入
              tags: [Auth]
              requestBody:
                required: true
                content:
                  application/x-www-form-urlencoded:
                    schema:
                      type: object
                      required: [username, password]
                      properties:
                        username: { type: string }
                        password: { type: string }
              responses:
                '200':
                  description: OK
                  content:
                    application/json:
                      schema:
                        type: object
                        properties:
                          token: { type: string }
          /api/Species/file/{id}:
            get:
              operationId: getSpeciesFile
              summary: "取得特定檔案\n前端範例: <img src=\"/api/Species/file/{id}\" />"
              description: |
                完整描述文字應保留在文件中，不應成為檔名。
                前端範例: <img src="/api/Species/file/{id}" />
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string, format: uuid }
              responses:
                '200':
                  description: OK
                  content:
                    application/octet-stream:
                      schema: { type: string, format: binary }
        """;

    public static ApiDocument Parse(string spec) => TestSupport.NormalizeSpec(spec);

    public static Operation SingleOperation(this ApiDocument document)
    {
        var pathItem = document.Paths.Values.Single();
        return pathItem.Operations.Values.Single();
    }

    public static Operation Operation(this ApiDocument document, string operationId) =>
        document.Paths.Values.SelectMany(p => p.Operations.Values)
            .Single(o => o.OperationId == operationId);

    public static SchemaInfo BodyProperty(Operation operation, string name)
    {
        var schema = ScenarioGenerator.PrimaryBodySchema(operation.RequestBody!)!;
        var merged = SampleValueFactory.MergeAllOf(schema);
        foreach (var (propertyName, propertySchema) in SampleValueFactory.MergedProperties(merged))
        {
            if (propertyName == name)
            {
                return propertySchema;
            }
        }

        throw new InvalidOperationException($"property '{name}' not found");
    }

    public static IReadOnlyList<GeneratedScenario> GenerateFor(Operation operation, int maxPerOperation = 32)
    {
        var snapshot = BrunoSyncEngine.BuildScenarioBaseSnapshot(operation, "{{baseUrl}}");
        return new ScenarioGenerator(new BrunoExampleSettings { MaxPerOperation = maxPerOperation })
            .Generate(OperationIdentity.From(operation), operation, snapshot);
    }
}

/// <summary>Blocker B — invalid-enum probes keep the SAME primitive type as the enum.</summary>
public class SameTypeInvalidEnumTests
{
    private static SchemaInfo Property(string fragment) =>
        ReleaseBlockerSpec.BodyProperty(
            ReleaseBlockerSpec.Parse($$"""
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
                                target:
                                  {{fragment}}
                """).SingleOperation(), "target");

    private static SpecVersion Version(string fragment) =>
        ReleaseBlockerSpec.Parse($$"""
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
                            {{fragment}}
            """).Version;

    [Fact]
    public void Numeric_Enum_Invalid_Value_Preserves_Integer_Type()
    {
        var doc = ReleaseBlockerSpec.Parse("""
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
                            target:
                              type: integer
                              enum: [1, 2, 3]
            """);
        var prop = ReleaseBlockerSpec.BodyProperty(doc.SingleOperation(), "target");
        var probe = SampleValueFactory.SameTypeInvalidEnumValue(SampleValueFactory.Wrap(prop.Node, doc.Version));
        Assert.NotNull(probe);
        Assert.True(propeIsLong(probe), "integer probe expected");
        Assert.Equal("4", probe!.ToJsonString());
    }

    [Fact]
    public void Numeric_Enum_Invalid_Value_Not_In_Allowed_Values()
    {
        var doc = ReleaseBlockerSpec.Parse("""
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
                            target:
                              type: integer
                              enum: [1, 2, 3]
            """);
        var prop = ReleaseBlockerSpec.BodyProperty(doc.SingleOperation(), "target");
        var wrapped = SampleValueFactory.Wrap(prop.Node, doc.Version);
        var values = SampleValueFactory.EnumValues(wrapped).Select(v => v!.GetValue<long>()).ToList();
        var probe = SampleValueFactory.SameTypeInvalidEnumValue(wrapped);
        Assert.Equal(4, probe!.GetValue<long>());
        Assert.DoesNotContain(probe.GetValue<long>(), values);
    }

    [Fact]
    public void String_Enum_Invalid_Value_Preserves_String_Type()
    {
        var doc = ReleaseBlockerSpec.Parse("""
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
                            target:
                              type: string
                              enum: [A, B]
            """);
        var prop = ReleaseBlockerSpec.BodyProperty(doc.SingleOperation(), "target");
        var probe = SampleValueFactory.SameTypeInvalidEnumValue(SampleValueFactory.Wrap(prop.Node, doc.Version));
        Assert.NotNull(probe);
        Assert.True(probe is JsonValue v && v.TryGetValue<string>(out _));
        Assert.Equal("__sentinel_invalid_enum__", probe!.GetValue<string>());
    }

    [Fact]
    public void Nullable_Enum_Probe_Remains_Non_Null_Same_Type()
    {
        var doc = ReleaseBlockerSpec.Parse("""
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
                            target:
                              type: integer
                              nullable: true
                              enum: [1, 2]
            """);
        var prop = ReleaseBlockerSpec.BodyProperty(doc.SingleOperation(), "target");
        var probe = SampleValueFactory.SameTypeInvalidEnumValue(SampleValueFactory.Wrap(prop.Node, doc.Version));
        Assert.NotNull(probe);
        Assert.True(propeIsLong(probe));
    }

    [Fact]
    public void Negative_And_Sparse_Enum_Produces_Out_Of_Set_Integer()
    {
        var doc = ReleaseBlockerSpec.Parse("""
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
                            target:
                              type: integer
                              enum: [-5, 10]
            """);
        var prop = ReleaseBlockerSpec.BodyProperty(doc.SingleOperation(), "target");
        Assert.Equal(11, SampleValueFactory.SameTypeInvalidEnumValue(
            SampleValueFactory.Wrap(prop.Node, doc.Version))!.GetValue<long>());
    }

    [Fact]
    public void Enum_At_Long_Maximum_Wraps_Below_Minimum()
    {
        var doc = ReleaseBlockerSpec.Parse("""
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
                            target:
                              type: integer
                              enum: [9223372036854775807]
            """);
        var prop = ReleaseBlockerSpec.BodyProperty(doc.SingleOperation(), "target");
        var probe = SampleValueFactory.SameTypeInvalidEnumValue(SampleValueFactory.Wrap(prop.Node, doc.Version));
        Assert.NotNull(probe);
        Assert.NotEqual(long.MaxValue, probe!.GetValue<long>());
    }

    [Fact]
    public void Number_Enum_Probe_Is_Number_Outside_Set()
    {
        var doc = ReleaseBlockerSpec.Parse("""
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
                            target:
                              type: number
                              enum: [0.25, 0.75]
            """);
        var prop = ReleaseBlockerSpec.BodyProperty(doc.SingleOperation(), "target");
        var probe = SampleValueFactory.SameTypeInvalidEnumValue(SampleValueFactory.Wrap(prop.Node, doc.Version));
        Assert.NotNull(probe);
        Assert.True(probe is JsonValue v && v.TryGetValue<double>(out _));
        Assert.Equal(1.75d, probe!.GetValue<double>(), precision: 6);
    }

    [Fact]
    public void Boolean_Enum_Covering_True_And_False_Generates_No_Scenario()
    {
        var doc = ReleaseBlockerSpec.Parse("""
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
                            flag:
                              type: boolean
                              enum: [true, false]
            """);
        var op = doc.SingleOperation();
        var scenarios = ReleaseBlockerSpec.GenerateFor(op);
        Assert.DoesNotContain(scenarios, s => s.Category == ScenarioCategory.InvalidEnum);
    }

    [Fact]
    public void Deterministic_Generation_Across_Repeated_Calls()
    {
        var doc = ReleaseBlockerSpec.Parse("""
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
                            target:
                              type: integer
                              enum: [7, 8]
            """);
        var prop = ReleaseBlockerSpec.BodyProperty(doc.SingleOperation(), "target");
        var first = SampleValueFactory.SameTypeInvalidEnumValue(SampleValueFactory.Wrap(prop.Node, doc.Version))!.ToJsonString();
        var second = SampleValueFactory.SameTypeInvalidEnumValue(SampleValueFactory.Wrap(prop.Node, doc.Version))!.ToJsonString();
        Assert.Equal(first, second);
    }

    [Fact]
    public void No_Collision_For_Sparse_Sets()
    {
        var doc = ReleaseBlockerSpec.Parse("""
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
                            target:
                              type: integer
                              enum: [100, 500, 900]
            """);
        var prop = ReleaseBlockerSpec.BodyProperty(doc.SingleOperation(), "target");
        Assert.Equal(901, SampleValueFactory.SameTypeInvalidEnumValue(
            SampleValueFactory.Wrap(prop.Node, doc.Version))!.GetValue<long>());
    }

    private static bool propeIsLong(JsonNode node) => node is JsonValue v && v.TryGetValue<long>(out _);
}

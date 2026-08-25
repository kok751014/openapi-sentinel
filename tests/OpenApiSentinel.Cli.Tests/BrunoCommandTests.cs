using OpenApiSentinel.Cli;

namespace OpenApiSentinel.Cli.Tests;

/// <summary>End-to-end CLI behavior of `bruno sync` / `bruno examples` in temp workspaces.</summary>
public sealed class BrunoCommandTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("sentinel-bruno-cli").FullName;
    private readonly RecordingConsole console = new();

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }

    private string WriteFile(string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private const string Spec = """
        openapi: 3.0.3
        info:
          title: Demo API
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
                        customerId: { type: integer, minimum: 1 }
                        note: { type: string }
              responses:
                '200': { description: OK }
        """;

    private string Config(string extra = "") => WriteFile("openapi-sentinel.yml", $"""
        bruno:
          enabled: true
          source: ./spec.yaml
          collectionPath: ./api-contract/bruno/collection.yml
          reportPath: ./api-contract/reports
          sync:
            preserveManualValues: true
            removeDeletedOperations: false
          examples:
            enabled: true
            language: zh-TW
            ignoreAuthentication: true
            maxPerOperation: 8
        {extra}
        """);

    [Fact]
    public void Sync_Creates_Collection_State_And_Reports()
    {
        WriteFile("spec.yaml", Spec);
        _ = Config();

        var exit = CommandLineRunner.Run(["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-color"], console);
        Assert.True(exit == 0, "exit=" + exit + " errors=" + string.Join(" | ", console.ErrorLines));

        var collection = Path.Combine(root, "api-contract/bruno/collection.yml");
        Assert.True(File.Exists(collection));
        Assert.True(File.Exists(Path.Combine(root, "api-contract/reports/bruno-sync-report.html")));
        Assert.True(File.Exists(Path.Combine(root, "api-contract/reports/bruno-sync-report.json")));
        Assert.True(Directory.EnumerateFiles(Path.GetDirectoryName(collection)!, ".*.sentinel-state.json", SearchOption.TopDirectoryOnly).Any());

        var json = File.ReadAllText(Path.Combine(root, "api-contract/reports/bruno-sync-report.json"));
        Assert.Contains("\"schemaVersion\": \"bruno-sync/1.0\"", json, StringComparison.Ordinal);
        Assert.Contains("createOrder", json, StringComparison.Ordinal);

        var yaml = File.ReadAllText(collection);
        Assert.Contains("成功－完整參數", yaml, StringComparison.Ordinal); // zh-TW scenario present
    }

    [Fact]
    public void Second_Sync_Skips_With_Exact_Message()
    {
        WriteFile("spec.yaml", Spec);
        _ = Config();
        string[] args = ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-color"];

        Assert.Equal(0, CommandLineRunner.Run(args, console));
        var second = new RecordingConsole();
        Assert.Equal(0, CommandLineRunner.Run(args, second));

        var text = string.Join("\n", second.OutLines);
        Assert.Contains("No API contract changes detected.", text, StringComparison.Ordinal);
        Assert.Contains("Nothing to update.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DryRun_Writes_Nothing()
    {
        WriteFile("spec.yaml", Spec);
        _ = Config();

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--dry-run"], console);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(Path.Combine(root, "api-contract/bruno/collection.yml")));
        Assert.False(File.Exists(Path.Combine(root, "api-contract/reports/bruno-sync-report.html")));
        Assert.Contains("(dry-run)", string.Join("\n", console.OutLines), StringComparison.Ordinal);
    }

    [Fact]
    public void Directory_Target_Is_Refused_Without_Corruption()
    {
        WriteFile("spec.yaml", Spec);
        Directory.CreateDirectory(Path.Combine(root, "existing-collection")); // .bru-style dir
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./existing-collection
            """);

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(3, exit); // invalid configuration
        Assert.Contains(console.ErrorLines, l => l.Contains(".bru", StringComparison.Ordinal));
    }

    [Fact]
    public void Identical_Report_And_Collection_Paths_Are_Rejected()
    {
        WriteFile("spec.yaml", Spec);
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./c.yml
              reportPath: ./c.yml
            """);

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(3, exit);
    }

    [Fact]
    public void Unsupported_Language_Fails_Configuration()
    {
        WriteFile("spec.yaml", Spec);
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./c.yml
              examples:
                language: zh-CN
            """);

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(3, exit);
        Assert.Contains(console.ErrorLines, l => l.Contains("zh-TW", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_Bruno_Key_Warns()
    {
        WriteFile("spec.yaml", Spec);
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./c.yml
              unknownKey: 42
            """);

        _ = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-examples"], console);

        Assert.Contains(console.ErrorLines, l =>
            l.Contains("bruno.unknownKey", StringComparison.Ordinal) && l.StartsWith("warning:", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_Source_Gives_Actionable_Error()
    {
        _ = Config();
        // spec.yaml intentionally absent AND no bruno.source override file exists → config source points at missing file
        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(2, exit); // invalid input (missing spec file)
    }

    [Fact]
    public void No_Source_Configured_At_All_Is_Actionable()
    {
        WriteFile("openapi-sentinel.yml", """
            bruno:
              collectionPath: ./c.yml
            """);

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(2, exit);
        Assert.Contains(console.ErrorLines, l => l.Contains("--source", StringComparison.Ordinal));
    }

    [Fact]
    public void Examples_Operation_Filter_Touches_Only_Matching_Request()
    {
        WriteFile("spec.yaml", Spec + """
            
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
            """);
        _ = Config();

        string[] args = ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-color"];
        Assert.Equal(0, CommandLineRunner.Run(args, console));

        // targeted regeneration for one operation only
        var filtered = new RecordingConsole();
        var filterExit = CommandLineRunner.Run(
            ["bruno", "examples", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--operation", "getUser"],
            filtered);
        Assert.True(filterExit == 0, "exit=" + filterExit + " errors=" + string.Join(" | ", filtered.ErrorLines));

        var text = string.Join("\n", filtered.OutLines);
        Assert.Contains("未變更：1", text, StringComparison.Ordinal);   // only the filtered op processed
        Assert.DoesNotContain("[新增]", text, StringComparison.Ordinal); // nothing else created
        Assert.Contains("/users/{id}", string.Join("\n", console.OutLines), StringComparison.Ordinal);
    }

    [Fact]
    public void Business_Scenario_Errors_Block_Sync_With_Config_Exit_Code()
    {
        WriteFile("spec.yaml", Spec);
        WriteFile("scenarios.yml", """
            scenarios:
              - operationId: doesNotExist
                name: 不存在的操作
            """);
        _ = Config("""
            bruno:
              enabled: true
              source: ./spec.yaml
              collectionPath: ./c.yml
              reportPath: ./reports
              examples:
                scenariosPath: ./scenarios.yml
            """);

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(3, exit);
    }

    [Fact]
    public void Examples_Command_Overrides_Config_Disabled_Examples()
    {
        WriteFile("spec.yaml", Spec);
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./c.yml
              examples:
                enabled: false
            """);

        // explicit `bruno examples` overrides config enabled=false; --no-examples suppresses.
        var exit = CommandLineRunner.Run(
            ["bruno", "examples", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);
        Assert.True(exit == 0, "exit=" + exit + " errors=" + string.Join("\n | ", console.ErrorLines));
        Assert.Contains("成功－完整參數",
            File.ReadAllText(Path.Combine(root, "c.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_Respects_Config_Disabled_Examples_With_No_Examples_Flag_Equivalent()
    {
        WriteFile("spec.yaml", Spec);
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./c.yml
              examples:
                enabled: false
            """);

        _ = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);
        Assert.DoesNotContain("成功－完整參數",
            File.ReadAllText(Path.Combine(root, "c.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_Source_Requires_Network_Permission_Path_Unchanged()
    {
        // URL source without allow-remote → the standard network-policy failure (exit 4),
        // proving the existing SSRF/security model applies to bruno sources too.
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: https://api.example.com/swagger.json
              collectionPath: ./c.yml
            """);

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(4, exit);
    }

    private sealed class RecordingConsole : IConsoleWriter
    {
        public List<string> OutLines { get; } = [];
        public List<string> ErrorLines { get; } = [];

        public void Out(string value = "") => OutLines.Add(value);

        public void WriteError(string value = "") => ErrorLines.Add(value);
    }
}

using OpenApiSentinel.Cli;
using OpenApiSentinel.Bruno;
using OpenApiSentinel.Bruno.Reports;
using System.Text;
using System.Net;
using System.Text.Json;

namespace OpenApiSentinel.Cli.Tests;

/// <summary>End-to-end CLI behavior of `bruno sync` / `bruno examples` in temp workspaces.</summary>
[Collection("process-cwd")]
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

    private string ReportsDirectory => Path.Combine(root, "api-contract", "reports");

    private IReadOnlyList<string> ReportFiles(string extension) =>
        Directory.Exists(ReportsDirectory)
            ? [.. Directory.EnumerateFiles(ReportsDirectory, "bruno-sync-report-*" + extension).Order(StringComparer.Ordinal)]
            : [];

    private static string ReportStem(string path) => Path.GetFileNameWithoutExtension(path);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public void Sync_Creates_Collection_State_And_Reports()
    {
        WriteFile("spec.yaml", Spec);
        _ = Config();

        var exit = CommandLineRunner.Run(["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-color"], console);
        Assert.True(exit == 0, "exit=" + exit + " errors=" + string.Join(" | ", console.ErrorLines));

        var collection = Path.Combine(root, "api-contract/bruno/collection.yml");
        Assert.True(File.Exists(collection));
        var htmlReports = ReportFiles(".html");
        var jsonReports = ReportFiles(".json");
        Assert.Single(htmlReports);
        Assert.Single(jsonReports);
        Assert.Matches("^bruno-sync-report-[0-9]{8}-[0-9]{6}(?:-[0-9]{2})?\\.html$", Path.GetFileName(htmlReports[0]));
        Assert.Equal(ReportStem(htmlReports[0]), ReportStem(jsonReports[0]));
        Assert.True(Directory.EnumerateFiles(Path.GetDirectoryName(collection)!, ".*.sentinel-state.json", SearchOption.TopDirectoryOnly).Any());

        var json = File.ReadAllText(jsonReports[0]);
        Assert.Contains("\"schemaVersion\": \"bruno-sync/1.0\"", json, StringComparison.Ordinal);
        Assert.Contains("createOrder", json, StringComparison.Ordinal);

        var yaml = File.ReadAllText(collection);
        Assert.Contains("成功－完整參數", yaml, StringComparison.Ordinal); // zh-TW scenario present
    }

    [Fact]
    public void Default_Config_Disables_Generated_Examples_But_Keeps_Normal_Requests()
    {
        WriteFile("spec.yaml", Spec);
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./api-contract/bruno/collection.yml
              workspacePath: ./api-contract/bruno-workspace
            """);

        Assert.Equal(0, CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-color"], console));

        var collection = File.ReadAllText(Path.Combine(root, "api-contract/bruno/collection.yml"));
        var native = File.ReadAllText(Directory.EnumerateFiles(
            Path.Combine(root, "api-contract/bruno-workspace"), "*.bru", SearchOption.AllDirectories)
            .Single(path => File.ReadAllText(path).Contains("body:json", StringComparison.Ordinal)));
        Assert.DoesNotContain("成功－完整參數", collection, StringComparison.Ordinal);
        Assert.DoesNotContain("examples:", collection, StringComparison.Ordinal);
        Assert.DoesNotContain("example {", native, StringComparison.Ordinal);
        Assert.Contains("customerId", collection, StringComparison.Ordinal);
        Assert.Contains("body: json", native, StringComparison.Ordinal);
        Assert.Contains("body:json", native, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, "api-contract/scenarios")));
    }

    [Fact]
    public void Disabling_Examples_Removes_Only_Recorded_Examples_And_Stabilizes()
    {
        WriteFile("spec.yaml", Spec);
        var configPath = WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./api-contract/bruno/collection.yml
              workspacePath: ./api-contract/bruno-workspace
              reportPath: ./api-contract/reports
              examples:
                enabled: true
            """);
        string[] args = ["bruno", "sync", "--config", configPath, "--no-color"];

        Assert.Equal(0, CommandLineRunner.Run(args, console));
        var requestFile = Directory.EnumerateFiles(
            Path.Combine(root, "api-contract/bruno-workspace"), "*.bru", SearchOption.AllDirectories)
            .Single(path => File.ReadAllText(path).Contains("body:json", StringComparison.Ordinal));
        File.AppendAllText(requestFile, "\nexample {\n  name: 使用者自訂範例\n}\n");

        File.WriteAllText(configPath, File.ReadAllText(configPath).Replace("enabled: true", "enabled: false", StringComparison.Ordinal));
        Assert.Equal(0, CommandLineRunner.Run(args, new RecordingConsole()));

        var collectionPath = Path.Combine(root, "api-contract/bruno/collection.yml");
        var collection = File.ReadAllText(collectionPath);
        var native = File.ReadAllText(requestFile);
        Assert.DoesNotContain("成功－完整參數", collection, StringComparison.Ordinal);
        Assert.DoesNotContain("成功－完整參數", native, StringComparison.Ordinal);
        Assert.Contains("使用者自訂範例", collection, StringComparison.Ordinal);
        Assert.Contains("使用者自訂範例", native, StringComparison.Ordinal);
        Assert.Contains("body:json", native, StringComparison.Ordinal);

        var beforeSecondDisabledSync = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var secondConsole = new RecordingConsole();
        Assert.Equal(0, CommandLineRunner.Run(args, secondConsole));
        Assert.All(beforeSecondDisabledSync, file => Assert.Equal(file.Value, File.ReadAllBytes(file.Key)));
        Assert.Contains("No API contract changes detected.", string.Join("\n", secondConsole.OutLines), StringComparison.Ordinal);
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
        Assert.Empty(ReportFiles(".html"));
        Assert.Contains("(dry-run)", string.Join("\n", console.OutLines), StringComparison.Ordinal);
    }

    [Fact]
    public void Report_History_Retains_Changed_Syncs_And_NoChange_Sync_Leaves_It_Untouched()
    {
        WriteFile("spec.yaml", Spec);
        _ = Config();
        string[] args = ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-color"];

        Assert.Equal(0, CommandLineRunner.Run(args, console));
        var firstReports = ReportFiles(".html").Concat(ReportFiles(".json")).ToDictionary(path => path, File.ReadAllBytes);
        Assert.Equal(2, firstReports.Count);

        var noChangeConsole = new RecordingConsole();
        Assert.Equal(0, CommandLineRunner.Run(args, noChangeConsole));
        Assert.Equal(firstReports.Keys.Order(StringComparer.Ordinal), ReportFiles(".html").Concat(ReportFiles(".json")).Order(StringComparer.Ordinal));
        Assert.All(firstReports, report => Assert.Equal(report.Value, File.ReadAllBytes(report.Key)));
        Assert.Contains("report generation skipped", string.Join("\n", noChangeConsole.OutLines), StringComparison.Ordinal);

        WriteFile("spec.yaml", Spec.Replace("note: { type: string }", "comment: { type: string }", StringComparison.Ordinal));
        Assert.Equal(0, CommandLineRunner.Run(args, new RecordingConsole()));

        var htmlReports = ReportFiles(".html");
        var jsonReports = ReportFiles(".json");
        Assert.Equal(2, htmlReports.Count);
        Assert.Equal(2, jsonReports.Count);
        Assert.All(firstReports, report => Assert.Equal(report.Value, File.ReadAllBytes(report.Key)));
        Assert.Equal(htmlReports.Select(ReportStem).Order(StringComparer.Ordinal), jsonReports.Select(ReportStem).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Report_Model_Excludes_Unchanged_Details_But_Retains_Their_Summary_Count()
    {
        var outcome = new BrunoSyncOutcome
        {
            Operations =
            [
                new OperationSyncOutcome { Action = OperationAction.Unchanged, Method = "GET", Path = "/same", OperationId = "same" },
                new OperationSyncOutcome { Action = OperationAction.Updated, Method = "POST", Path = "/changed", OperationId = "changed", Changes = ["contract changed"] },
            ],
            Conflicts = [],
            Warnings = [],
        };
        var report = BrunoWorkflow.BuildReport(outcome, new BrunoSyncRequest { SubCommand = "sync" }, "spec.yaml", "collection.yml", "test",
            new DateTimeOffset(2026, 8, 27, 15, 20, 45, TimeSpan.Zero));

        Assert.True(BrunoSyncReportPolicy.HasMeaningfulChanges(outcome));
        Assert.Equal(1, report.Summary.OperationsUnchanged);
        Assert.Equal(["changed"], report.Operations.Select(operation => operation.OperationId));

        var html = BrunoSyncHtmlReporter.Render(report);
        var json = BrunoSyncJsonReporter.Render(report);
        Assert.Contains("/changed", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/same", html, StringComparison.Ordinal);
        using var parsed = JsonDocument.Parse(json);
        var jsonOperationIds = parsed.RootElement.GetProperty("operations").EnumerateArray()
            .Select(operation => operation.GetProperty("operationId").GetString()).ToList();
        Assert.Equal(report.Operations.Select(operation => operation.OperationId), jsonOperationIds);
        Assert.DoesNotContain(
            parsed.RootElement.GetProperty("operations").EnumerateArray(),
            operation => operation.GetProperty("action").GetString() == "unchanged");
    }

    [Fact]
    public void Report_Timestamp_Uses_Injected_Clock()
    {
        var expected = new DateTimeOffset(2026, 8, 27, 15, 20, 45, TimeSpan.Zero);
        var actual = BrunoWorkflow.GetReportTimestamp(new BrunoSyncRequest
        {
            SubCommand = "sync",
            TimeProvider = new FixedTimeProvider(expected),
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Report_Pair_Names_Are_Deterministic_Utc_And_Collisions_Use_One_Matched_Suffix()
    {
        var reports = Path.Combine(root, "reports");
        var timestamp = new DateTimeOffset(2026, 8, 27, 15, 20, 45, TimeSpan.Zero);
        var first = BrunoWorkflow.ResolveReportArtifactPaths(reports, timestamp);
        Assert.EndsWith("bruno-sync-report-20260827-152045.html", first.HtmlPath, StringComparison.Ordinal);
        Assert.EndsWith("bruno-sync-report-20260827-152045.json", first.JsonPath, StringComparison.Ordinal);

        Directory.CreateDirectory(reports);
        File.WriteAllText(first.HtmlPath, "old html");
        File.WriteAllText(first.JsonPath, "old json");
        var second = BrunoWorkflow.ResolveReportArtifactPaths(reports, timestamp);

        Assert.EndsWith("bruno-sync-report-20260827-152045-01.html", second.HtmlPath, StringComparison.Ordinal);
        Assert.EndsWith("bruno-sync-report-20260827-152045-01.json", second.JsonPath, StringComparison.Ordinal);
        Assert.Equal("old html", File.ReadAllText(first.HtmlPath));
        Assert.Equal("old json", File.ReadAllText(first.JsonPath));
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
    public void Examples_DryRun_Writes_Nothing()
    {
        WriteFile("spec.yaml", Spec);
        _ = Config();

        // baseline collection exists
        Assert.Equal(0, CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-color"],
            console));
        var before = File.ReadAllText(Path.Combine(root, "api-contract/bruno/collection.yml"));

        var exit = CommandLineRunner.Run(
            ["bruno", "examples", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--dry-run"],
            console);

        Assert.Equal(0, exit);
        Assert.Equal(before, File.ReadAllText(Path.Combine(root, "api-contract/bruno/collection.yml")));
        Assert.Contains("(dry-run)", string.Join("\n", console.OutLines), StringComparison.Ordinal);
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
    public void CollectionPath_Under_A_File_Parent_Fails_As_Config_Error_Before_Writes()
    {
        WriteFile("spec.yaml", Spec);
        WriteFile("blocker", "i-am-a-file");
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./blocker/collection.yml
            """);

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(3, exit);
        Assert.Contains(console.ErrorLines, l =>
            l.Contains("blocker", StringComparison.Ordinal) && l.Contains("檔案", StringComparison.Ordinal));
        Assert.DoesNotContain(Directory.EnumerateFiles(root, "collection.yml", SearchOption.AllDirectories), _ => true);
    }

    [Fact]
    public void ReportDir_Pointing_At_Existing_File_Fails_As_Config_Error()
    {
        WriteFile("spec.yaml", Spec);
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./c.yml
              reportPath: ./reports-file
            """);
        WriteFile("reports-file", string.Empty);

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(3, exit); // config error, NOT opaque internal error
        Assert.Contains(console.ErrorLines, l =>
            l.Contains("reports-file", StringComparison.Ordinal) &&
            l.Contains("檔案", StringComparison.Ordinal));
        // nothing written anywhere: refusal precedes the collection write
        Assert.False(File.Exists(Path.Combine(root, "c.yml")));
        Assert.False(Directory.EnumerateFiles(root, "*.sentinel-state.json").Any());
    }

    [Fact]
    public void Zero_Match_Filter_Warns_Instead_Of_Silent_Success()
    {
        WriteFile("spec.yaml", Spec);
        _ = Config();

        var exit = CommandLineRunner.Run(
            ["bruno", "examples", "--config", Path.Combine(root, "openapi-sentinel.yml"),
             "--operation", "noSuchOp"], console);

        Assert.Equal(0, exit);
        Assert.Contains(console.ErrorLines, l =>
            l.Contains("過濾條件未符合任何操作", StringComparison.Ordinal));
    }

    [Fact]
    public void StatePath_Colliding_With_Collection_Is_Refused_And_Non_Destructive()
    {
        WriteFile("spec.yaml", Spec);
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./c.yml
            """);

        // seed a valid collection first
        _ = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-examples"],
            console);
        var collectionPath = Path.Combine(root, "c.yml");
        var before = File.ReadAllText(collectionPath);

        // now aim the state file AT the collection: must refuse loudly, not destroy it
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: ./spec.yaml
              collectionPath: ./c.yml
              sync:
                statePath: ./c.yml
            """);

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(3, exit);
        Assert.Equal(before, File.ReadAllText(collectionPath)); // untouched
        Assert.Contains(console.ErrorLines, l => l.Contains("statePath", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_Source_Requires_Network_Permission_Path_Unchanged()
    {
        // URL source WITHOUT allow-remote must be refused by POLICY (not by a DNS
        // failure) — the SSRF model applies to bruno sources identically to compare.
        WriteFile("openapi-sentinel.yml", """
            bruno:
              source: https://api.example.com/swagger.json
              collectionPath: ./c.yml
            """);

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);

        Assert.Equal(4, exit);
        Assert.Contains(console.ErrorLines, l =>
            l.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("remote", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Full_Remote_Policy_OptIn_Enables_Loopback_Source_End_To_End()
    {
        // Positive control: with the FULL remoteReferences opt-in chain (enabled +
        // allowHttp + allowPrivateNetwork — exactly what compare requires), an http
        // loopback source syncs for real. Proves bruno inherits the entire SSRF model.
        using var server = LoopbackSpecServer.Start(Spec);
        WriteFile("openapi-sentinel.yml", string.Join(Environment.NewLine,
            "bruno:",
            $"  source: {server.Prefix}spec.yaml",
            "  collectionPath: ./c.yml",
            "  examples:",
            "    enabled: true",
            "remoteReferences:",
            "  enabled: true",
            "  allowHttp: true",
            "  allowPrivateNetwork: true"));

        var exit = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")],
            console);

        Assert.True(exit == 0, "exit=" + exit + " errors=" + string.Join(" | ", console.ErrorLines));
        Assert.Contains("成功－完整參數",
            File.ReadAllText(Path.Combine(root, "c.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Private_Network_Flag_Is_Required_And_Enables_Only_The_Current_Run()
    {
        using var server = LoopbackSpecServer.Start(Spec);
        WriteFile("openapi-sentinel.yml", string.Join(Environment.NewLine,
            "bruno:",
            $"  source: {server.Prefix}spec.yaml",
            "  collectionPath: ./c.yml",
            "remoteReferences:",
            "  enabled: true",
            "  allowHttp: true",
            "  allowPrivateNetwork: false"));

        var denied = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml")], console);
        Assert.Equal(4, denied);
        Assert.False(File.Exists(Path.Combine(root, "c.yml")));

        var allowed = CommandLineRunner.Run(
            ["bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"),
             "--allow-private-network"], console);
        Assert.True(allowed == 0, "exit=" + allowed + " errors=" + string.Join(" | ", console.ErrorLines));
        Assert.True(File.Exists(Path.Combine(root, "c.yml")));
        Assert.Contains("allowPrivateNetwork: false", File.ReadAllText(Path.Combine(root, "openapi-sentinel.yml")),
            StringComparison.Ordinal);
    }

    private sealed class RecordingConsole : IConsoleWriter
    {
        public List<string> OutLines { get; } = [];
        public List<string> ErrorLines { get; } = [];

        public void Out(string value = "") => OutLines.Add(value);

        public void WriteError(string value = "") => ErrorLines.Add(value);
    }
}

/// <summary>Minimal loopback HTTP server serving one spec file (mirrors the OpenApi.Tests pattern).</summary>
public sealed class LoopbackSpecServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly string content;

    private LoopbackSpecServer(string content)
    {
        this.content = content;
        var port = GetFreePort();
        Prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(Prefix);
        listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public string Prefix { get; }

    public static LoopbackSpecServer Start(string specYaml) => new(specYaml);

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        try
        {
            return ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        }
        finally
        {
            l.Stop();
        }
    }

    private async Task AcceptLoop()
    {
        try
        {
            while (listener.IsListening)
            {
                var ctx = await listener.GetContextAsync();
                var bytes = Encoding.UTF8.GetBytes(content);
                ctx.Response.ContentType = "application/yaml";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        }
        catch (Exception)
        {
            // listener aborted during dispose — expected
        }
    }

    public void Dispose() => listener.Stop();
}

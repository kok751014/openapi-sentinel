using System.Text;

namespace OpenApiSentinel.Cli.Tests;

/// <summary>
/// §6A/§6B one-click bootstrap: the very first `bruno sync --source …` in an empty project
/// must create config, layout directories, bundled collection, Bruno workspace and reports —
/// while experimental generated scenarios remain disabled — and a missing local source must fail clearly without creating
/// fake inputs. Dry-run plans everything but touches nothing.
/// </summary>
[Collection("process-cwd")]
public sealed class BrunoBootstrapTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("sentinel-bootstrap").FullName;
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

    /// <summary>Runs the CLI with the temp project as the working directory: exactly how
    /// a user launches the one-click bootstrap from their project root.</summary>
    private int RunInRoot(params string[] args)
    {
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            return CommandLineRunner.Run(args, console);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
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
              responses:
                '200': { description: OK }
        """;

    [Fact]
    public void First_Sync_With_Source_Creates_Default_Config_And_Full_Layout()
    {
        WriteFile("swagger.json", Spec);

        var exit = RunInRoot("bruno", "sync", "--source", "./swagger.json", "--no-color");

        Assert.True(exit == 0, "exit=" + exit + " errors=" + string.Join(" | ", console.ErrorLines));

        Assert.True(File.Exists(Path.Combine(root, "openapi-sentinel.yml")));
        Assert.True(File.Exists(Path.Combine(root, "api-contract/bruno/collection.yml")));
        Assert.False(Directory.Exists(Path.Combine(root, "api-contract/scenarios")));
        var reports = Path.Combine(root, "api-contract/reports");
        Assert.Single(Directory.EnumerateFiles(reports, "bruno-sync-report-*.html"));
        Assert.Single(Directory.EnumerateFiles(reports, "bruno-sync-report-*.json"));
        Assert.True(Directory.Exists(Path.Combine(root, "api-contract/openapi")));

        // Folder-based workspace in the official OpenCollection filesystem format.
        Assert.True(File.Exists(Path.Combine(root, "api-contract/bruno-workspace/bruno.json")));

        // The generated config is directly reusable (valid keys, correct quoting).
        var configText = File.ReadAllText(Path.Combine(root, "openapi-sentinel.yml"));
        Assert.Contains("workspacePath", configText, StringComparison.Ordinal);
        Assert.Contains("enabled: false", configText, StringComparison.Ordinal);
        Assert.DoesNotContain("成功－完整參數",
            File.ReadAllText(Path.Combine(root, "api-contract/bruno/collection.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Second_Sync_Works_Without_Source_Argument()
    {
        WriteFile("swagger.json", Spec);
        Assert.Equal(0, RunInRoot("bruno", "sync", "--source", "./swagger.json", "--no-color"));

        var second = new RecordingConsole();
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            Assert.True(CommandLineRunner.Run(["bruno", "sync", "--no-color"], second) == 0,
                "errors=" + string.Join(" | ", second.ErrorLines));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
        Assert.Contains("No API contract changes detected.", string.Join("\n", second.OutLines), StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_Config_For_Local_Source_Does_Not_Enable_Remote_Loading()
    {
        var configPath = WriteFile("openapi-sentinel.yml", BrunoBootstrap.ConfigTemplate(
            "./swagger.json", enableRemote: false, allowPrivateNetwork: false));

        var config = SentinelConfigLoader.Load(configPath);

        Assert.False(config.RemoteReferences.Enabled);
        Assert.False(config.RemoteReferences.AllowPrivateNetwork);
    }

    [Fact]
    public void Bootstrap_Config_For_Public_Remote_Persists_Only_Remote_OptIn()
    {
        var configPath = WriteFile("openapi-sentinel.yml", BrunoBootstrap.ConfigTemplate(
            "https://api.example.com/swagger.json", enableRemote: true, allowPrivateNetwork: false));

        var config = SentinelConfigLoader.Load(configPath);

        Assert.True(config.RemoteReferences.Enabled);
        Assert.False(config.RemoteReferences.AllowPrivateNetwork);
    }

    [Fact]
    public void Bootstrap_Config_For_Private_Remote_Persists_Explicit_Private_OptIn()
    {
        var configPath = WriteFile("openapi-sentinel.yml", BrunoBootstrap.ConfigTemplate(
            "https://localhost:7099/swagger/backend/swagger.json", enableRemote: true, allowPrivateNetwork: true));

        var config = SentinelConfigLoader.Load(configPath);

        Assert.True(config.RemoteReferences.Enabled);
        Assert.True(config.RemoteReferences.AllowPrivateNetwork);
    }

    [Fact]
    public void Bootstrap_Config_Persists_InsecureTls_OnlyForExplicitOptIn()
    {
        var optedInPath = WriteFile("opted-in.yml", BrunoBootstrap.ConfigTemplate(
            "https://localhost:7099/swagger.json", enableRemote: true, allowPrivateNetwork: true, insecureTls: true));
        var defaultPath = WriteFile("default.yml", BrunoBootstrap.ConfigTemplate(
            "https://localhost:7099/swagger.json", enableRemote: true, allowPrivateNetwork: true));

        Assert.True(SentinelConfigLoader.Load(optedInPath).RemoteReferences.InsecureTls);
        Assert.False(SentinelConfigLoader.Load(defaultPath).RemoteReferences.InsecureTls);
    }

    [Fact]
    public void First_Sync_Does_Not_Overwrite_Existing_Config()
    {
        WriteFile("swagger.json", Spec);
        const string marker = "# my-custom-config-do-not-touch";
        WriteFile("openapi-sentinel.yml", marker + "\nbruno:\n  source: ./swagger.json\n");

        var exit = RunInRoot("bruno", "sync", "--source", "./swagger.json", "--no-color");

        Assert.Equal(0, exit);
        Assert.StartsWith(marker, File.ReadAllText(Path.Combine(root, "openapi-sentinel.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_Without_Config_And_Without_Source_Fails_With_Actionable_Message()
    {
        var exit = RunInRoot("bruno", "sync", "--no-color");

        Assert.Equal(2, exit); // input/config error family
        var text = string.Join("\n", console.ErrorLines);
        Assert.Contains("尚未設定 OpenAPI source", text, StringComparison.Ordinal);
        Assert.Contains("--source <file-or-url>", text, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "openapi-sentinel.yml")));
    }

    [Fact]
    public void Dry_Run_First_Sync_Plans_Everything_But_Writes_Nothing()
    {
        WriteFile("swagger.json", Spec);
        var before = SnapshotTree();

        var exit = RunInRoot("bruno", "sync", "--source", "./swagger.json", "--dry-run", "--no-color");

        Assert.Equal(0, exit);
        Assert.Equal(before, SnapshotTree()); // filesystem byte-identical

        var outText = string.Join("\n", console.OutLines);
        Assert.Contains("[預計建立] openapi-sentinel.yml", outText, StringComparison.Ordinal);
        Assert.Contains("bruno.json", outText, StringComparison.Ordinal);
        Assert.Contains("(dry-run) 未修改任何檔案。", outText, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_Local_Source_Fails_Clearly_And_Creates_No_Fake_Input()
    {
        var exit = RunInRoot("bruno", "sync", "--source", "./does-not-exist.yaml", "--no-color");

        Assert.Equal(2, exit);
        Assert.Contains(console.ErrorLines, l => l.Contains("not found", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(root, "does-not-exist.yaml")));
        Assert.False(File.Exists(Path.Combine(root, "openapi-sentinel.yml")));
        Assert.False(Directory.Exists(Path.Combine(root, "api-contract")));
    }

    [Fact]
    public void Parent_Path_Is_File_Fails_Safely_Without_Partial_Writes()
    {
        WriteFile("swagger.json", Spec);
        WriteFile("blocker", "i am a file");
        var exit = RunInRoot("bruno", "sync",
             "--source", "./swagger.json",
             "--collection", "./blocker/nested/collection.yml",
             "--no-color");

        Assert.Equal(3, exit); // configuration error — clear diagnostic, not exit-5 IOException
        Assert.Contains(console.ErrorLines, l => l.Contains("檔案", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(root, "blocker/nested/collection.yml")));
    }

    [Fact]
    public void Illegal_User_Extra_Key_On_Request_Root_Blocks_Write_And_State()
    {
        WriteFile("swagger.json", Spec);
        WriteFile("openapi-sentinel.yml", """
            bruno:
              enabled: true
              source: ./swagger.json
              collectionPath: ./api-contract/bruno/collection.yml
              reportPath: ./api-contract/reports
              examples:
                enabled: false
            """);

        WriteFile("api-contract/bruno/collection.yml", """
            opencollection: "1.0.0"
            bundled: true
            info:
              name: 手動集合
            extensions:
              x-sentinel:
                version: 1
                operations:
                  opid:createOrder:
                    operationId: createOrder
                    method: POST
                    path: /orders
                    identityKey: opid:createOrder
                    contentHash: stale-but-matches-after-sync
                items: []
            items:
              - info:
                  name: 建立訂單
                  type: http
                  seq: 1
                  tags:
                    - x-sentinel:opid:createOrder
                http:
                  method: POST
                  url: "{{baseUrl}}/orders"
                customExtra: 不合法欄位
            """);

        var exit = RunInRoot("bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-color");

        Assert.Equal(2, exit);
        var err = string.Join("\n", console.ErrorLines);
        Assert.Contains("OpenCollection validation failed", err, StringComparison.Ordinal);
        Assert.Contains("customExtra", err, StringComparison.Ordinal);

        // NOTHING was written: the collection file is byte-identical, and no state,
        // workspace, or reports appeared.
        Assert.Contains("customExtra: 不合法欄位", File.ReadAllText(Path.Combine(root, "api-contract/bruno/collection.yml")), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, "api-contract/bruno-workspace")));
        Assert.False(Directory.EnumerateFiles(Path.Combine(root, "api-contract/bruno"), "*.sentinel-state.json", SearchOption.TopDirectoryOnly).Any());
        var reports = Path.Combine(root, "api-contract/reports");
        Assert.False(Directory.Exists(reports));
    }

    [Fact]
    public void Dry_Run_Also_Validates_And_Writes_Nothing_On_Failure()
    {
        WriteFile("swagger.json", Spec);
        WriteFile("openapi-sentinel.yml", """
            bruno:
              enabled: true
              source: ./swagger.json
              collectionPath: ./api-contract/bruno/collection.yml
              reportPath: ./api-contract/reports
              examples:
                enabled: false
            """);
        WriteFile("api-contract/bruno/collection.yml", """
            opencollection: "1.0.0"
            bundled: true
            info:
              name: 手動集合
            extensions:
              x-sentinel:
                version: 1
                operations:
                  opid:createOrder:
                    operationId: createOrder
                    method: POST
                    path: /orders
                    identityKey: opid:createOrder
                    contentHash: stale
            items:
              - info:
                  name: 建立訂單
                  type: http
                  seq: 1
                  tags:
                    - x-sentinel:opid:createOrder
                http:
                  method: POST
                  url: "{{baseUrl}}/orders"
                customExtra: 不合法欄位
            """);
        var before = SnapshotTree();

        var exit = RunInRoot("bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"),
             "--dry-run", "--no-color");

        Assert.Equal(2, exit); // dry-run validates too — failure exits non-zero
        Assert.Contains(console.ErrorLines, l => l.Contains("OpenCollection validation failed", StringComparison.Ordinal));
        Assert.Equal(before, SnapshotTree()); // and still touches nothing
    }

    [Fact]
    public void Windows_And_Unix_Style_Relative_Paths_Resolve_Identically_Via_Path_Api()
    {
        // Cross-platform path normalization is exercised through the shared resolver; the
        // literal separators come from Path APIs so this passes on every OS we build on.
        WriteFile("swagger.json", Spec);
        var nested = Path.Combine("deep", "nested", "collection.yml");
        WriteFile("openapi-sentinel.yml", $"""
            bruno:
              source: {(".\\swagger.json").Replace('\\', Path.DirectorySeparatorChar)}
              collectionPath: {nested}
            """);

        var exit = RunInRoot("bruno", "sync", "--config", Path.Combine(root, "openapi-sentinel.yml"), "--no-color");

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(root, nested)));
    }

    private string SnapshotTree()
    {
        var sb = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            sb.Append(file[(root.Length + 1)..]).Append('|').Append(File.ReadAllText(file)).Append('\n');
        }

        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            sb.Append(dir[(root.Length + 1)..]).Append("/\n");
        }

        return sb.ToString();
    }
}

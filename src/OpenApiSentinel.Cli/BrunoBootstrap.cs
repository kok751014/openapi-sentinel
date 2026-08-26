using OpenApiSentinel.Bruno;

namespace OpenApiSentinel.Cli;

/// <summary>
/// One-click bootstrap: when a project has NO openapi-sentinel.yml, the first
/// <c>bruno sync --source …</c> generates a production-ready config plus the full
/// api-contract layout (openapi snapshot dir, bundled collection, Bruno workspace,
/// scenario template, reports) so no manual mkdir/file creation is ever required.
/// </summary>
public static class BrunoBootstrap
{
    public const string DefaultConfigFileName = "openapi-sentinel.yml";
    public const string DefaultCollectionPath = "./api-contract/bruno/collection.yml";
    public const string DefaultWorkspacePath = "./api-contract/bruno-workspace";
    public const string DefaultReportPath = "./api-contract/reports";
    public const string DefaultScenariosPath = "./api-contract/scenarios/openapi-sentinel.scenarios.yml";
    public const string OpenApiDirRelative = "api-contract/openapi";
    public const string SnapshotFileName = "source-snapshot.json";

    /// <summary>Production-ready default configuration written on first sync.</summary>
    public static string ConfigTemplate(string source)
    {
        return $"""
        # OpenAPI Sentinel 設定（由第一次 `bruno sync` 自動產生）
        # 文件：docs/configuration.md
        bruno:
          enabled: true

          # OpenAPI/Swagger 來源：本機檔案或 URL。
          source: {QuoteYaml(source)}

          collectionPath: "{DefaultCollectionPath}"
          workspacePath: "{DefaultWorkspacePath}"
          reportPath: "{DefaultReportPath}"

          sync:
            preserveManualValues: true
            preserveScripts: true
            preserveTests: true
            preserveDocs: true
            removeDeletedOperations: false

          examples:
            enabled: true
            language: zh-TW
            ignoreAuthentication: true
            maxPerOperation: 8
            scenariosPath: "{DefaultScenariosPath}"
        """;
    }

    /// <summary>Editable business-scenario starter file. Empty template must never fail a sync.</summary>
    public const string ScenarioTemplate = """
        # OpenAPI Sentinel 商業情境範例
        # OpenAPI 無法推導的後端商業邏輯可以寫在這裡。
        scenarios: []

        # 範例：
        # - operationId: createOrder
        #   name: "成功－一般訂單"
        #   description: "使用有效資料建立一般訂單。"
        #   request:
        #     body:
        #       customerId: 10001
        #   expect:
        #     status: 200
        #     description: "建立成功"
        """;

    /// <summary>
    /// The workspace collection folder name: derived from the current directory (or git
    /// repository name), sanitized for Windows/Linux/macOS. Config may override it via
    /// the last segment of <c>workspacePath</c>.
    /// </summary>
    public static string DeriveProjectName(string workingDirectory)
    {
        var dir = new DirectoryInfo(string.IsNullOrEmpty(workingDirectory) ? "." : workingDirectory);
        var name = dir.Name;
        if (string.IsNullOrEmpty(name))
        {
            name = "api-collection";
        }

        return BrunoWorkspaceNames.Sanitize(name);
    }

    /// <summary>Double-quoted YAML scalar with backslash/quote escaping — safe for Windows paths and URLs.</summary>
    public static string QuoteYaml(string value)
    {
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}

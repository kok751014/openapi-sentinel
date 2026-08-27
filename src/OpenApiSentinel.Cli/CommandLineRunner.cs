using OpenApiSentinel.Bruno;
using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.Cli;

/// <summary>
/// Entry orchestration: parses arguments, loads config, dispatches commands,
/// and maps exceptions to the documented exit codes.
/// </summary>
public static class CommandLineRunner
{
    private static readonly HashSet<string> KnownFormats = new(StringComparer.Ordinal)
        { "console", "json", "markdown", "html", "sarif", "junit" };

    public static int Run(string[] args, IConsoleWriter? consoleWriter = null)
    {
        var console = consoleWriter ?? ConsoleWriter.Instance;
        try
        {
            return RunCore(args, console);
        }
        catch (SpecParseException ex)
        {
            console.WriteError($"error: invalid OpenAPI specification: {ex.Message}");
            return ExitCodes.InvalidInput;
        }
        catch (SpecFetchException ex)
        {
            console.WriteError($"error: could not fetch specification: {ex.Message}");
            return ExitCodes.NetworkFailure;
        }
        catch (SentinelConfigException ex)
        {
            console.WriteError($"configuration error: {ex.Message}");
            return ExitCodes.InvalidConfiguration;
        }
        catch (BrunoFormatException ex)
        {
            console.WriteError($"error: invalid Bruno collection: {ex.Message}");
            return ExitCodes.InvalidInput;
        }
        catch (BrunoSyncException ex)
        {
            console.WriteError($"bruno sync error: {ex.Message}");
            return ExitCodes.InvalidConfiguration;
        }
        catch (Exception ex)
        {
            var debug = CommandLineParser.Parse(args).Has("debug");
            if (debug) throw;
            console.WriteError($"internal error: {ex.Message} (rerun with --debug for details)");
            return ExitCodes.InternalError;
        }
    }

    /// <summary>Options that require an explicit value.</summary>
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "format", "fail-on", "config", "baseline", "update-baseline",
        "output", "o", "json", "markdown", "html", "sarif", "junit",
        "changelog", "pr-summary", "explain-target",
        "source", "collection", "report-dir", "operation", "path",
    };

    private static int RunCore(string[] args, IConsoleWriter console)
    {
        var parsed = CommandLineParser.Parse(args);
        var command = parsed.Command?.ToLowerInvariant();

        // A value-taking option without its value would otherwise be silently ignored.
        foreach (var key in ValueOptions)
        {
            if (parsed.Has(key) && parsed.Value(key) is null)
            {
                console.WriteError($"error: option '--{key}' requires a value.");
                return ExitCodes.InvalidConfiguration;
            }
        }

        // Standard global flags that work with or without an explicit command.
        if (command is null && parsed.Has("version"))
            return SimpleCommands.Version(console);

        // Subcommand-specific help wins over the generic usage text.
        if (command is not null && parsed.Has("help"))
        {
            PrintCommandHelp(console, command);
            return ExitCodes.Ok;
        }

        if (parsed.Has("help") || command is null or "help")
        {
            PrintUsage(console);
            return ExitCodes.Ok;
        }

        switch (command)
        {
            case "version":
                return SimpleCommands.Version(console);

            case "init":
                return SimpleCommands.Init(parsed.Has("force"), console);

            case "rules":
                return SimpleCommands.Rules(console, asJson: parsed.Value("format") == "json");

            case "explain":
                return parsed.Positional.Count > 0
                    ? SimpleCommands.Explain(parsed.Positional[0], console)
                    : UsageError(console, "explain <RULE-ID>");

            case "validate":
                return parsed.Positional.Count == 1
                    ? SimpleCommands.ValidateAsync(
                        parsed.Positional[0], LoadConfig(parsed, console), parsed.Has("allow-remote"), console, default).GetAwaiter().GetResult()
                    : UsageError(console, "validate <SPEC>");

            case "compare":
                return CompareAsync(parsed, console).GetAwaiter().GetResult();

            case "bruno":
                return parsed.Positional.Count >= 1
                    ? BrunoAsync(parsed, parsed.Positional[0].ToLowerInvariant(), console).GetAwaiter().GetResult()
                    : UsageError(console, "bruno <sync|examples> [options]");

            default:
                console.WriteError($"Unknown command '{parsed.Command}'.");
                PrintUsage(console);
                return ExitCodes.InvalidInput;
        }
    }

    private static async Task<int> BrunoAsync(ParsedCommandLine parsed, string subCommand, IConsoleWriter console)
    {
        if (subCommand is not ("sync" or "examples"))
        {
            console.WriteError($"error: unknown bruno sub-command '{subCommand}'. Use 'sync' or 'examples'.");
            return ExitCodes.InvalidInput;
        }

        // Config discovery happens here (not inside LoadConfig) so the workflow can tell a
        // MISSING default config apart from an explicit one and bootstrap when appropriate.
        var explicitConfig = parsed.Value("config");
        var configPath = explicitConfig ?? FindDefaultConfig();
        SentinelConfiguration config;
        if (configPath is null)
        {
            config = new SentinelConfiguration();
        }
        else if (!File.Exists(configPath))
        {
            if (explicitConfig is not null)
            {
                throw new SentinelConfigException($"Configuration file not found: '{configPath}'.");
            }

            config = new SentinelConfiguration();
        }
        else
        {
            config = LoadConfigFromPath(configPath, console);
        }

        string? sourceOverride = parsed.Value("source") ?? (parsed.Positional.Count > 1 ? parsed.Positional[1] : null);

        var request = new BrunoSyncRequest
        {
            SubCommand = subCommand,
            Config = config,
            ConfigPath = configPath,
            DryRun = parsed.Has("dry-run"),
            Verbose = parsed.Has("verbose"),
            SourceOverride = sourceOverride,
            CollectionOverride = parsed.Value("collection"),
            ReportDirOverride = parsed.Value("report-dir"),
            RemoveDeletedOperationsOverride = parsed.Has("remove-deleted") ? true : null,
            AllowRemoteOverride = parsed.Has("allow-remote"),
            AllowPrivateNetworkOverride = parsed.Has("allow-private-network"),
            ExamplesEnabledOverride = !parsed.Has("no-examples"),
            OperationFilter = parsed.Value("operation"),
            PathFilter = parsed.Value("path"),
        };

        return await BrunoWorkflow.SyncAsync(request, console);
    }

    private static async Task<int> CompareAsync(ParsedCommandLine parsed, IConsoleWriter console)
    {
        if (parsed.Positional.Count != 2)
            return UsageError(console, "compare <OLD-SPEC> <NEW-SPEC>");

        var config = LoadConfig(parsed, console);
        var format = parsed.Value("format") ?? "console";
        if (!KnownFormats.Contains(format))
            throw new SentinelConfigException($"Unknown --format '{format}'. Supported: console, json, markdown, html.");

        var failOnOverride = parsed.Value("fail-on") is { } fo ? SentinelConfigLoader.ParseFailOn(fo) : null;

        // CLI output path options override config output section entries.
        Dictionary<string, string> cliOutputs = new(StringComparer.Ordinal);
        foreach (var key in new[] { "output", "o", "json", "markdown", "html", "sarif", "junit", "changelog", "pr-summary" })
        {
            // "-o/--output" targets whatever the primary --format is; the other keys are explicit formats.
            var canonical = key is "output" or "o" ? format : key;
            var value = parsed.Value(key);
            if (value is not null) cliOutputs[canonical] = value;
        }

        var request = new CompareWorkflow.Request
        {
            OldSource = parsed.Positional[0],
            NewSource = parsed.Positional[1],
            Config = config,
            OutputPaths = cliOutputs,
            Format = format,
            NoColor = parsed.Has("no-color"),
            Verbose = parsed.Has("verbose"),
            AllowRemoteOverride = parsed.Has("allow-remote"),
            FailOnOverride = failOnOverride,
            BaselinePathOverride = parsed.Value("baseline"),
            UpdateBaselinePath = parsed.Value("update-baseline"),
            ReportUnknownKinds = parsed.Has("report-unknown-kinds"),
            HideSuppressed = parsed.Has("no-suppressed"),
        };

        return await CompareWorkflow.RunAsync(request, console);
    }

    internal static SentinelConfiguration LoadConfig(ParsedCommandLine parsed, IConsoleWriter console)
    {
        var path = parsed.Value("config") ?? FindDefaultConfig();
        if (path is null)
            return new SentinelConfiguration();

        return LoadConfigFromPath(path, console);
    }

    private static SentinelConfiguration LoadConfigFromPath(string path, IConsoleWriter console)
    {
        var warnings = new List<string>();
        var config = SentinelConfigLoader.Load(path, w => warnings.Add(w));
        foreach (var warning in warnings)
        {
            console.WriteError($"warning: {warning}");
        }
        return config;
    }

    /// <summary>Looks for openapi-sentinel.yml/.yaml in the working directory.</summary>
    private static string? FindDefaultConfig()
    {
        foreach (var name in new[] { "openapi-sentinel.yml", "openapi-sentinel.yaml" })
        {
            if (File.Exists(name)) return name;
        }
        return null;
    }

    private static int UsageError(IConsoleWriter console, string usage)
    {
        console.WriteError($"usage: openapi-sentinel {usage}");
        return ExitCodes.InvalidInput;
    }

    private static void PrintCommandHelp(IConsoleWriter console, string command)
    {
        switch (command)
        {
            case "compare":
                console.Out("""
                    openapi-sentinel compare <OLD-SPEC> <NEW-SPEC> [options]

                    Compare two contracts and enforce the configured policy.
                    Specs may be files, http(s) URLs, '-' (stdin), or git:<rev>:<path>.

                    options:
                      --format <console|json|markdown|html>   primary output format
                      -o/--output <path>                      write primary format to a file
                      --json|--markdown|--html|--sarif|--junit|--changelog|--pr-summary <path>
                                                              additional artifacts (combinable)
                      --fail-on <severity|all|none>           override failOn for this run
                      --baseline <file>                       suppress baselined findings
                      --update-baseline <file>                adopt current findings as baseline
                      --allow-remote                          enable remote references this run
                      --config <path>                         explicit configuration file
                      --no-color                              plain text output
                      --verbose                               impact + suggested action per finding
                      --report-unknown-kinds                  flag changes no rule evaluates (CG9003)
                      --no-suppressed                         omit the suppressed-findings section
                    """);
                break;

            case "validate":
                console.Out("""
                    openapi-sentinel validate <SPEC>

                    Parse, resolve references and normalize one contract; prints a summary
                    (spec version, paths, operations, security schemes) or an actionable error.
                    Accepts the same input sources as compare. Exit 2 on invalid specs.
                    """);
                break;

            case "rules":
                console.Out("""
                    openapi-sentinel rules [--format json]

                    List every built-in rule with id, title and default severity.
                    --format json emits machine-readable output used to generate docs/rules.md.
                    """);
                break;

            case "explain":
                console.Out("""
                    openapi-sentinel explain <RULE-ID>

                    Print the full documentation of one rule (description, impact,
                    remediation, examples, limitations). Example: explain CG2003
                    """);
                break;

            case "init":
                console.Out("""
                    openapi-sentinel init [--force]

                    Write a commented starter openapi-sentinel.yml in the current directory.
                    Refuses to overwrite without --force.
                    """);
                break;

            case "bruno":
                console.Out("""
                    openapi-sentinel bruno <sync|examples> [options]

                    sync      把 OpenAPI contract 同步成 Bruno collection（OpenCollection YAML），
                              保留人工 values / scripts / tests / docs / auth，並可自動產生測試範例。
                              產出前會以官方 OpenCollection v1.0 schema 驗證，未通過絕不寫檔。
                    examples  只（重）產生測試範例；可用 --operation 或 --path 縮小範圍。

                    一鍵初始化（第一次使用不需要手動建立任何檔案或資料夾）：
                      openapi-sentinel bruno sync --source <file-or-url>

                      若 openapi-sentinel.yml 不存在會自動建立；api-contract/ 底下的
                      openapi/、bruno/、bruno-workspace/、scenarios/、reports/ 一併自動生成。
                      之後在 Bruno 直接「開啟集合」選 api-contract/bruno-workspace 即可，
                      不需要每次 Import。第二次起只需執行 `openapi-sentinel bruno sync`。

                    options:
                      --source <file-or-url>    OpenAPI/Swagger 來源（覆寫設定檔 bruno.source）
                      --collection <path.yml>   Bruno collection 目標（單檔 OpenCollection YAML）
                      --report-dir <dir>        報告輸出目錄（預設 ./api-contract/reports）
                      --config <path>           明確指定設定檔
                      --dry-run                 只分析與顯示計畫變更，不寫入任何檔案
                      --remove-deleted          允許刪除已從規格移除的操作（預設僅標示失效）
                      --no-examples             本次不產生測試範例
                      --operation <operationId> examples：只處理此 operation
                      --path <api-path>         examples：只處理此 API path 開頭的操作
                      --allow-remote            允許此 run 使用遠端來源／$refs
                                                （需符合既有 remoteReferences 安全政策）
                      --allow-private-network    明確允許本次使用 localhost／私有網路來源或 $refs
                      --verbose                 顯示每個操作的變更與保留明細
                      --no-color                純文字輸出

                    說明：
                      * 自動產生的範例名稱／描述預設為繁體中文（zh-TW）。
                      * authentication 不會被拿來產生失敗情境；既有 auth 設定永不改動。
                      * 原生 Bruno workspace（bruno.json + .bru）可直接用 Collection → Open Collection 開啟。
                      * Sentinel metadata 存放於 schema 合法的集合層 extensions.x-sentinel 索引，
                        舊版 request-level x-sentinel 檔案會在下一次 sync 自動遷移。
                    """);
                break;

            default:
                PrintUsage(console);
                break;
        }
    }

    private static void PrintUsage(IConsoleWriter console)
    {
        console.Out("""
            OpenAPI Sentinel — detect breaking API contract changes before they reach production.

            usage:
              openapi-sentinel compare <OLD-SPEC> <NEW-SPEC> [options]
              openapi-sentinel validate <SPEC>
              openapi-sentinel bruno <sync|examples> [options]
              openapi-sentinel rules [--format json]
              openapi-sentinel explain <RULE-ID>
              openapi-sentinel init [--force]
              openapi-sentinel version

            compare inputs:
              local files, http(s) URLs, '-' for stdin, git:<rev>:<path>

            options:
              --format <console|json|markdown|html>   primary output format (default: console)
              --fail-on <severity|all|none>           override failOn policy for this run
              --no-color                              disable ANSI colors
              --verbose                               include impact and suggested actions
              --debug                                 show stack traces on internal errors
              --config <path>                         explicit configuration file
              --allow-remote                          enable remote references for this run
              --allow-private-network                 allow localhost/private-network remote sources or refs for this Bruno run
              --baseline <file>                       suppress findings listed in baseline
              --update-baseline <file>                write current findings as the new baseline
              -o/--output <path>                      write primary format to a file
              --json|--markdown|--html|--sarif|--junit|--changelog|--pr-summary <path>
                                                      additional artifacts (combined outputs)

            exit codes:
              0 passed   1 policy violated   2 invalid spec/input
              3 invalid configuration   4 network failure   5 internal error

            documentation: https://github.com/openapi-sentinel/openapi-sentinel
            """);
    }
}

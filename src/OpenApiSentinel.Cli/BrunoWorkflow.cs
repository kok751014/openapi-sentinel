using System.Security.Cryptography;
using System.Text.Json;
using OpenApiSentinel.Bruno;
using OpenApiSentinel.Bruno.Model;
using OpenApiSentinel.Bruno.Reports;
using OpenApiSentinel.Bruno.Scenarios;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Cli;

/// <summary>Options for one bruno sync / examples invocation.</summary>
public sealed record BrunoSyncRequest
{
    /// <summary>"sync" or "examples".</summary>
    public required string SubCommand { get; init; }

    public SentinelConfiguration Config { get; init; } = new();

    public bool DryRun { get; init; }

    public bool Verbose { get; init; }

    public string? SourceOverride { get; init; }

    public string? CollectionOverride { get; init; }

    public string? ReportDirOverride { get; init; }

    public bool? RemoveDeletedOperationsOverride { get; init; }

    /// <summary>Mirrors compare's --allow-remote: enables remote sources/refs for this
    /// run ONLY when explicitly requested. Never defaulted on.</summary>
    public bool AllowRemoteOverride { get; init; }

    public bool ExamplesEnabledOverride { get; init; } = true;

    /// <summary>Filter to a single operationId (examples command).</summary>
    public string? OperationFilter { get; init; }

    /// <summary>Filter to operations whose path starts with this API path (examples command).</summary>
    public string? PathFilter { get; init; }
}

/// <summary>Implements the bruno sync / examples commands end-to-end.</summary>
public static class BrunoWorkflow
{
    private const string ReportHtmlName = "bruno-sync-report.html";
    private const string ReportJsonName = "bruno-sync-report.json";

    public static async Task<int> SyncAsync(BrunoSyncRequest request, IConsoleWriter console, CancellationToken ct = default)
    {
        var config = request.Config;
        var bruno = config.Bruno ?? new BrunoConfiguration();

        if (!bruno.Enabled && request.SubCommand != "sync")
        {
            console.WriteError("bruno is disabled by configuration ('bruno.enabled: false').");
            return ExitCodes.InvalidConfiguration;
        }

        // ---- resolve paths ----
        var baseDir = config.ConfigBaseDirectory;
        var rawSource = request.SourceOverride ?? bruno.Source;
        if (string.IsNullOrWhiteSpace(rawSource))
        {
            console.WriteError(
                "error: no OpenAPI source configured. Use --source <file-or-url> or set 'bruno.source' in openapi-sentinel.yml.");
            return ExitCodes.InvalidInput;
        }

        // A source from the CONFIG resolves against the config file's directory so CI/MSBuild
        // runs from any cwd behave identically. CLI --source overrides stay shell-cwd relative.
        var isSpecialSource = rawSource == "-" || rawSource.StartsWith("git:", StringComparison.Ordinal) ||
                              Uri.TryCreate(rawSource, UriKind.Absolute, out _);
        var source = request.SourceOverride is not null || isSpecialSource || Path.IsPathRooted(rawSource)
            ? rawSource
            : BrunoPaths.Resolve(rawSource, baseDir);

        if (string.IsNullOrWhiteSpace(request.CollectionOverride) && string.IsNullOrWhiteSpace(bruno.CollectionPath))
        {
            console.WriteError(
                "error: no Bruno collection target configured. Use --collection <path.yml> or set 'bruno.collectionPath'.");
            return ExitCodes.InvalidInput;
        }

        var rawCollectionPath = request.CollectionOverride ?? bruno.CollectionPath!;
        var collectionFile = BrunoPaths.Resolve(rawCollectionPath, baseDir);
        BrunoPaths.ValidateCollectionTarget(collectionFile);

        var rawReportPath = request.ReportDirOverride ?? bruno.ReportPath ?? "./api-contract/reports";
        var reportDir = BrunoPaths.Resolve(rawReportPath, baseDir);
        BrunoPaths.ValidateReportAndCollectionDisjoint(reportDir, collectionFile);

        // ---- load contract ----
        var outcomeDoc = await AnalysisPipeline.LoadDocumentAsync(
            source, config.RemoteReferences, config.HeaderEnv,
            allowRemoteOverride: request.AllowRemoteOverride,
            config.Limits, config.AllowAboveDocumentTree, ct);
        var document = outcomeDoc.Document;

        // ---- incremental fast path ----
        var settings = bruno.Sync with { RemoveDeletedOperations = request.RemoveDeletedOperationsOverride ?? bruno.Sync.RemoveDeletedOperations };
        // `bruno examples` is an explicit user request to generate scenarios, so it
        // overrides config examples.enabled=false; --no-examples suppresses everywhere.
        var examplesEnabled = request.SubCommand == "examples"
            ? request.ExamplesEnabledOverride
            : bruno.Examples.Enabled && request.ExamplesEnabledOverride;
        var examples = bruno.Examples with { Enabled = examplesEnabled };
        var toolVersion = ToolVersion.Get();

        var sourceHash = ContractFingerprint.Compute(document);
        var expectedState = new BrunoSyncState
        {
            GeneratorVersion = toolVersion,
            SourceHash = sourceHash,
            BrunoConfigHash = HashJson(JsonSerializer.Serialize(settings)),
            ExampleConfigHash = HashJson(JsonSerializer.Serialize(examples)),
            ScenariosFileHash = HashOptionalFile(examples.ScenariosPath, baseDir),
            LastSyncUtc = DateTime.UtcNow.ToString("u"),
        };

        var statePath = ResolveStatePath(settings.StatePath, collectionFile, baseDir);
        var filtering = request.OperationFilter is not null || request.PathFilter is not null;

        if (!filtering &&
            !request.DryRun &&
            File.Exists(collectionFile) &&
            BrunoSyncStateStore.Matches(BrunoSyncStateStore.Read(statePath), expectedState))
        {
            console.Out("OpenAPI Sentinel Bruno Sync");
            console.Out("No API contract changes detected.");
            console.Out("Nothing to update.");
            return ExitCodes.Ok;
        }

        // ---- parse existing collection (or start fresh) ----
        BrunoCollectionDocument? existing = null;
        if (File.Exists(collectionFile))
        {
            existing = BrunoCollectionDocument.Parse(File.ReadAllText(collectionFile), collectionFile);
        }

        // ---- business scenarios (Layer 2) ----
        IReadOnlyList<BusinessScenario> businessScenarios = [];
        var warnings = new List<string>();
        if (examples.Enabled && !string.IsNullOrWhiteSpace(examples.ScenariosPath))
        {
            var scenariosFullPath = BrunoPaths.Resolve(examples.ScenariosPath!, baseDir);
            var loaded = BusinessScenarioFile.Load(scenariosFullPath, document);
            foreach (var error in loaded.Errors)
            {
                throw new SentinelConfigException($"商業情境檔驗證失敗：{error}");
            }

            foreach (var warning in loaded.Warnings)
            {
                warnings.Add($"[scenarios] {warning}");
            }

            businessScenarios = loaded.Scenarios;
        }

        // ---- run engine ----
        Func<OperationIdentity, bool>? filter = BuildFilter(request);
        var generator = new ScenarioGenerator(examples);
        var engine = new BrunoSyncEngine(settings, examples, toolVersion, generator, businessScenarios);
        var (collection, syncOutcome) = engine.Sync(existing, document, filter);

        var allWarnings = warnings.Concat(syncOutcome.Warnings).ToList();
        var finalOutcome = syncOutcome.Warnings.SequenceEqual(allWarnings)
            ? syncOutcome
            : syncOutcome with { Warnings = allWarnings };

        // ---- render console summary ----
        RenderConsoleSummary(finalOutcome, request, source, collectionFile, console);

        // ---- persist ----
        if (!request.DryRun && !finalOutcome.Skipped)
        {
            SafeFileWriter.Write(collectionFile, collection.ToYaml());
            BrunoSyncStateStore.Write(statePath, expectedState with { LastSyncUtc = DateTime.UtcNow.ToString("u") });

            var report = BuildReport(finalOutcome, request,
                request.SourceOverride is null ? rawSource : source, rawCollectionPath, toolVersion);
            Directory.CreateDirectory(reportDir);
            var htmlPath = Path.Combine(reportDir, ReportHtmlName);
            var jsonPath = Path.Combine(reportDir, ReportJsonName);
            SafeFileWriter.Write(htmlPath, BrunoSyncHtmlReporter.Render(report));
            SafeFileWriter.Write(jsonPath, BrunoSyncJsonReporter.Render(report));
            console.Out($"已寫入報告：{htmlPath}");
            console.Out($"             {jsonPath}");
        }
        else if (request.DryRun)
        {
            console.Out("(dry-run) 未修改任何檔案。");
        }

        return ExitCodes.Ok;
    }

    private static string ResolveStatePath(string? configured, string collectionFile, string? baseDir)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return BrunoPaths.Resolve(configured, baseDir);
        }

        var dir = Path.GetDirectoryName(collectionFile);
        var name = Path.GetFileName(collectionFile);
        return Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, $".{name}.sentinel-state.json");
    }

    private static Func<OperationIdentity, bool>? BuildFilter(BrunoSyncRequest request)
    {
        if (request.OperationFilter is { } opId)
        {
            var wanted = opId.Trim();
            return identity => string.Equals(identity.OperationId, wanted, StringComparison.Ordinal);
        }

        if (request.PathFilter is { } path)
        {
            var normalized = OperationIdentity.NormalizePath(path).TrimEnd('/');
            return identity => identity.NormalizedPath.StartsWith(normalized, StringComparison.Ordinal) ||
                               string.Equals(identity.NormalizedPath, normalized + "/", StringComparison.Ordinal) ||
                               normalized == "/" ||
                               string.Equals(identity.NormalizedPath, "/", StringComparison.Ordinal);
        }

        return null;
    }

    internal static BrunoSyncReport BuildReport(
        BrunoSyncOutcome outcome,
        BrunoSyncRequest request,
        string source,
        string collectionFile,
        string toolVersion)
    {
        _ = request;
        var operations = outcome.Operations.Select(o => new BrunoSyncReportOperation
        {
            Action = o.Action switch
            {
                OperationAction.Added => "added",
                OperationAction.Updated => "updated",
                OperationAction.Unchanged => "unchanged",
                OperationAction.Stale => "stale",
                OperationAction.Removed => "removed",
                _ => o.Action.ToString().ToLowerInvariant(),
            },
            Method = o.Method,
            Path = o.Path,
            OperationId = o.OperationId,
            RequestName = o.RequestName,
            Changes = [.. o.Changes],
            Preserved = [.. o.Preserved],
            Examples = [.. o.Examples.Select(e => new BrunoSyncReportExample(e.Name, e.Action))],
            Warnings = [.. o.Warnings],
        }).ToList();

        return new BrunoSyncReport
        {
            ToolVersion = toolVersion,
            GeneratedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            DryRun = request.DryRun,
            Skipped = outcome.Skipped,
            Source = source,
            CollectionPath = collectionFile,
            Summary = new BrunoSyncReportSummary
            {
                OperationsAdded = outcome.AddedCount,
                OperationsUpdated = outcome.UpdatedCount,
                OperationsStale = outcome.StaleCount,
                OperationsRemoved = outcome.RemovedCount,
                OperationsUnchanged = outcome.UnchangedCount,
                ExamplesAdded = outcome.ExamplesAdded,
                ExamplesUpdated = outcome.ExamplesUpdated,
                OperationsWithManualPreserved = outcome.ManualPreservedCount,
                Conflicts = outcome.Conflicts.Count,
                Warnings = outcome.Warnings.Count,
            },
            Operations = operations,
            Conflicts = [.. outcome.Conflicts.Select(c => new BrunoSyncReportConflict(c.Kind, c.Detail))],
            Warnings = [.. outcome.Warnings],
        };
    }

    private static void RenderConsoleSummary(
        BrunoSyncOutcome outcome,
        BrunoSyncRequest request,
        string source,
        string collectionFile,
        IConsoleWriter console)
    {
        console.Out("OpenAPI Sentinel Bruno Sync");

        if (outcome.Skipped)
        {
            console.Out("No API contract changes detected.");
            console.Out("Nothing to update.");
            return;
        }

        if (request.DryRun)
        {
            console.Out("模式：dry-run（僅預覽，不會修改任何檔案）");
        }

        console.Out($"來源：{source}");
        console.Out($"Bruno Collection：{(request.DryRun ? "(dry-run)" : collectionFile)}");

        var s = $"新增 API：{outcome.AddedCount}  更新 API：{outcome.UpdatedCount}  失效／刪除：{outcome.StaleCount + outcome.RemovedCount}  未變更：{outcome.UnchangedCount}";
        console.Out(s);

        foreach (var op in outcome.Operations.Where(o => o.Action != OperationAction.Unchanged)
                     .OrderBy(o => o.Path, StringComparer.Ordinal).ThenBy(o => o.Method, StringComparer.Ordinal))
        {
            var label = op.Action switch
            {
                OperationAction.Added => "新增",
                OperationAction.Updated => "更新",
                OperationAction.Stale => "失效",
                OperationAction.Removed => "已刪除",
                _ => "",
            };

            var nameSuffix = string.IsNullOrEmpty(op.RequestName) ? "" : $" — {op.RequestName}";
            console.Out($"[{label}] {op.Method} {op.Path}{nameSuffix}");

            if (request.Verbose)
            {
                foreach (var change in op.Changes)
                {
                    console.Out($"  變更：{change}");
                }

                foreach (var kept in op.Preserved)
                {
                    console.Out($"  保留：✓ {kept}");
                }
            }

            foreach (var example in op.Examples.Where(e => e.Action != "kept"))
            {
                var mark = example.Action == "added" ? "✓" : "↻";
                console.Out($"  範例 {mark} {example.Name}");
            }

            foreach (var warning in op.Warnings)
            {
                console.WriteError($"  警告：{warning}");
            }
        }

        foreach (var conflict in outcome.Conflicts)
        {
            console.WriteError($"同步衝突 [{conflict.Kind}]：{conflict.Detail}");
        }

        foreach (var warning in outcome.Warnings)
        {
            console.WriteError($"警告：{warning}");
        }
    }

    private static string HashJson(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string? HashOptionalFile(string? path, string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var full = BrunoPaths.Resolve(path, baseDir);
            if (!File.Exists(full))
            {
                return null;
            }

            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or BrunoSyncException)
        {
            return null;
        }
    }
}

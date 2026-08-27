using System.Security.Cryptography;
using System.Globalization;
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

    /// <summary>Path the configuration was loaded from; null when no config file exists
    /// (enables one-click config bootstrap on first sync).</summary>
    public string? ConfigPath { get; init; }

    public bool DryRun { get; init; }

    public bool Verbose { get; init; }

    public string? SourceOverride { get; init; }

    public string? CollectionOverride { get; init; }

    public string? ReportDirOverride { get; init; }

    public bool? RemoveDeletedOperationsOverride { get; init; }

    /// <summary>Mirrors compare's --allow-remote: enables remote sources/refs for this
    /// run ONLY when explicitly requested. Never defaulted on.</summary>
    public bool AllowRemoteOverride { get; init; }

    /// <summary>Explicit one-run opt-in for localhost/private remote sources and refs.
    /// This never enables remote loading by itself; <c>--allow-remote</c> remains required.</summary>
    public bool AllowPrivateNetworkOverride { get; init; }

    public bool ExamplesEnabledOverride { get; init; } = true;

    /// <summary>Clock for report timestamps; defaults to UTC system time and is injectable for tests.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Filter to a single operationId (examples command).</summary>
    public string? OperationFilter { get; init; }

    /// <summary>Filter to operations whose path starts with this API path (examples command).</summary>
    public string? PathFilter { get; init; }
}

/// <summary>Implements the bruno sync / examples commands end-to-end.</summary>
public static class BrunoWorkflow
{
    internal sealed record ReportArtifactPaths(string HtmlPath, string JsonPath, string Timestamp);

    /// <summary>Mirrors the bootstrap config template values for dry-run (nothing written).</summary>
    private static BrunoConfiguration BootstrappedBruno(string source) => new()
    {
        Enabled = true,
        Source = source,
        CollectionPath = BrunoBootstrap.DefaultCollectionPath,
        WorkspacePath = BrunoBootstrap.DefaultWorkspacePath,
        ReportPath = BrunoBootstrap.DefaultReportPath,
        Examples = new BrunoExampleSettings { ScenariosPath = BrunoBootstrap.DefaultScenariosPath },
    };

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

        // ---- one-click config bootstrap (§6B) ----
        // No config file anywhere + --source given ⇒ generate a production-ready
        // openapi-sentinel.yml and adopt its defaults immediately. Never overwrite an
        // existing user config.
        var bootstrapped = false;
        string? bootstrapConfigPath = null;
        string? bootstrapConfigContent = null;
        var haveConfig = request.ConfigPath is not null && File.Exists(request.ConfigPath);
        if (!haveConfig)
        {
            if (string.IsNullOrWhiteSpace(rawSource))
            {
                console.WriteError("尚未設定 OpenAPI source。");
                console.WriteError("第一次使用請指定：");
                console.WriteError("  openapi-sentinel bruno sync --source <file-or-url>");
                return ExitCodes.InvalidInput;
            }

            var newConfigPath = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(), request.ConfigPath ?? BrunoBootstrap.DefaultConfigFileName));
            var isRemoteBootstrapSource = Uri.TryCreate(rawSource, UriKind.Absolute, out var bootstrapUri) &&
                                          bootstrapUri.Scheme is "http" or "https";
            var remotePolicy = isRemoteBootstrapSource
                ? new RemoteFetchPolicy
                {
                    Enabled = request.AllowRemoteOverride,
                    AllowPrivateNetwork = request.AllowPrivateNetworkOverride,
                }
                : config.RemoteReferences;

            // Do not write bootstrap configuration until every source and schema gate has
            // succeeded. A rejected URL or invalid document must leave no misleading config.
            bruno = BootstrappedBruno(rawSource);
            config = config with
            {
                Bruno = bruno,
                RemoteReferences = remotePolicy,
                ConfigBaseDirectory = Directory.GetCurrentDirectory(),
            };
            baseDir = config.ConfigBaseDirectory;
            bootstrapConfigPath = newConfigPath;
            bootstrapConfigContent = BrunoBootstrap.ConfigTemplate(
                rawSource,
                enableRemote: isRemoteBootstrapSource && request.AllowRemoteOverride,
                allowPrivateNetwork: isRemoteBootstrapSource && request.AllowPrivateNetworkOverride);
            if (request.DryRun)
            {
                console.Out($"[預計建立] openapi-sentinel.yml");
            }

            bootstrapped = true;
        }

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
            : BrunoPaths.Resolve(rawSource!, baseDir);

        var rawCollectionPath = request.CollectionOverride ?? bruno.CollectionPath ?? BrunoBootstrap.DefaultCollectionPath;
        var collectionFile = BrunoPaths.Resolve(rawCollectionPath, baseDir);
        BrunoPaths.ValidateCollectionTarget(collectionFile);
        var collectionParent = Path.GetDirectoryName(collectionFile);
        if (!string.IsNullOrEmpty(collectionParent))
        {
            BrunoPaths.EnsureDirectoryAvailable(collectionParent);
        }

        var rawReportPath = request.ReportDirOverride ?? bruno.ReportPath ?? BrunoBootstrap.DefaultReportPath;
        var reportDir = BrunoPaths.Resolve(rawReportPath, baseDir);
        BrunoPaths.ValidateReportAndCollectionDisjoint(reportDir, collectionFile);
        // A reportDir (or one of its parents) that is an existing FILE would otherwise
        // surface as an opaque exit-5 IOException only AFTER the collection was written.
        BrunoPaths.EnsureDirectoryAvailable(reportDir);

        var workspaceDir = BrunoPaths.Resolve(bruno.WorkspacePath ?? BrunoBootstrap.DefaultWorkspacePath, baseDir);
        // The workspace target must be creatable too — a file sitting where the workspace
        // directory belongs is a config error, not an opaque mid-run IOException.
        BrunoPaths.EnsureDirectoryAvailable(workspaceDir);

        // The native collection reader treats every *.bru under the workspace as items —
        // the bundled collection or the reports must never live inside it.
        if (IsSameOrUnder(collectionFile, workspaceDir))
        {
            throw new SentinelConfigException(
                $"collectionPath ('{collectionFile}') 不可位於 Bruno 工作區目錄 '{workspaceDir}' 內；兩者必須分離。");
        }

        if (IsSameOrUnder(reportDir, workspaceDir))
        {
            throw new SentinelConfigException(
                $"reportPath ('{reportDir}') 不可位於（或等於）Bruno 工作區目錄 '{workspaceDir}'；兩者必須分離。");
        }

        // ---- load contract ----
        var outcomeDoc = await AnalysisPipeline.LoadDocumentAsync(
            source, config.RemoteReferences, config.HeaderEnv,
            allowRemoteOverride: request.AllowRemoteOverride,
            allowPrivateNetworkOverride: request.AllowPrivateNetworkOverride,
            config.Limits, config.AllowAboveDocumentTree, ct);
        var document = outcomeDoc.Document;

        // Remote sources get a deterministic snapshot of the RAW entry document as fetched
        // (never resolved/normalized, never any auth header content) for reviewable diffs.
        // Local files are never copied or modified. Writes are deferred until both schema
        // gates pass so a failed bootstrap never leaves partial output behind.
        var isRemoteSource = Uri.TryCreate(source, UriKind.Absolute, out var srcUri) &&
                             srcUri.Scheme is "http" or "https";
        var openApiDir = BrunoPaths.Resolve(BrunoBootstrap.OpenApiDirRelative, baseDir);
        var snapshotFile = Path.Combine(openApiDir, BrunoBootstrap.SnapshotFileName);

        // ---- incremental fast path ----
        var settings = bruno.Sync with { RemoveDeletedOperations = request.RemoveDeletedOperationsOverride ?? bruno.Sync.RemoveDeletedOperations };
        // `bruno examples` is an explicit user request to generate scenarios, so it
        // overrides config examples.enabled=false; --no-examples suppresses everywhere.
        var examplesEnabled = request.SubCommand == "examples"
            ? request.ExamplesEnabledOverride
            : bruno.Examples.Enabled && request.ExamplesEnabledOverride;
        var examples = bruno.Examples with { Enabled = examplesEnabled };
        var toolVersion = ToolVersion.Get();

        // The empty scenario template is committed only after both schema gates pass. Use
        // its prospective hash now so the second run can take the unchanged fast path.
        var scenarioTemplatePending = examples.Enabled && !string.IsNullOrWhiteSpace(examples.ScenariosPath) &&
                                      !File.Exists(BrunoPaths.Resolve(examples.ScenariosPath!, baseDir));

        var statePath = ResolveStatePath(settings.StatePath, collectionFile, baseDir);
        // A state path colliding with the collection (or a report artifact) would replace
        // it with JSON on the post-sync write — refuse loudly instead of destroying data.
        if (PathsCollide(statePath, collectionFile))
        {
            throw new SentinelConfigException(
                $"'bruno.sync.statePath' ('{statePath}') must not point at the collection file itself.");
        }

        var stateDir = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(stateDir))
        {
            BrunoPaths.EnsureDirectoryAvailable(stateDir);
        }

        // Computed AFTER scenario-template creation so a first-run template doesn't
        // force the next run into an unnecessary full sync (state must match next time).
        var expectedState = new BrunoSyncState
        {
            GeneratorVersion = toolVersion,
            SourceHash = ContractFingerprint.Compute(document),
            BrunoConfigHash = HashJson(JsonSerializer.Serialize(settings)),
            ExampleConfigHash = HashJson(JsonSerializer.Serialize(examples)),
            ScenariosFileHash = HashOptionalFile(examples.ScenariosPath, baseDir) ??
                                (scenarioTemplatePending ? HashText(BrunoBootstrap.ScenarioTemplate) : null),
            LastSyncUtc = DateTime.UtcNow.ToString("u"),
        };

        var filtering = request.OperationFilter is not null || request.PathFilter is not null;

        // The fast path must also notice a deleted workspace and rebuild it. A legacy
        // OpenCollection folder workspace (previous generator) forces a full migration run.
        var legacyWorkspacePresent = !BruWorkspaceReader.IsBruWorkspace(workspaceDir) &&
                                     OpenApiSentinel.Bruno.BrunoWorkspaceReader.IsWorkspace(workspaceDir);
        var workspacePresent = BruWorkspaceReader.IsBruWorkspace(workspaceDir);
        if (!filtering &&
            !request.DryRun &&
            File.Exists(collectionFile) &&
            workspacePresent &&
            !legacyWorkspacePresent &&
            BrunoSyncStateStore.Matches(BrunoSyncStateStore.Read(statePath), expectedState))
        {
            console.Out("OpenAPI Sentinel Bruno Sync");
            console.Out("No API contract changes detected.");
            console.Out("Nothing to update.");
            console.Out("[sentinel] no API contract changes; report generation skipped");
            return ExitCodes.Ok;
        }

        // ---- load existing state: native .bru workspace first (daily driver), bundled file fallback ----
        var warnings = new List<string>();
        BruRawRegistry? rawRegistry = new();
        BrunoCollectionDocument? existing = null;
        if (workspacePresent)
        {
            var read = BruWorkspaceReader.Read(
                workspaceDir,
                File.ReadAllText(Path.Combine(workspaceDir, BruWorkspaceNames.ManifestFileName)));
            existing = read.Document;
            rawRegistry = read.RawRegistry;
        }
        else if (legacyWorkspacePresent)
        {
            // Migration source: previous generator's folder-based OpenCollection workspace.
            existing = OpenApiSentinel.Bruno.BrunoWorkspaceReader.Read(workspaceDir);
            rawRegistry = new BruRawRegistry();
            foreach (var note in OpenCollectionNormalizer.Normalize(existing.Root))
            {
                warnings.Add($"[schema-相容] {note}");
            }

            warnings.Add("[migration] 偵測到舊版 OpenCollection 資料夾工作區；將轉換為 Bruno 原生（bruno.json + .bru）格式。");
        }
        else if (File.Exists(collectionFile))
        {
            existing = BrunoCollectionDocument.Parse(File.ReadAllText(collectionFile), collectionFile);
            rawRegistry = new BruRawRegistry(); // no .bru files were read — nothing raw survives
            // Bring legacy/user-edited files up to shapes the official schema accepts
            // (missing param `type`, numeric-typed strings, legacy `enabled:` flags).
            foreach (var note in OpenCollectionNormalizer.Normalize(existing.Root))
            {
                warnings.Add($"[schema-相容] {note}");
            }

            warnings.Add("[native] 工作區不存在，將從 bundled collection 重建 Bruno 原生工作區。");
        }

        // ---- business scenarios (Layer 2) + scenario template bootstrap (§6B) ----
        IReadOnlyList<BusinessScenario> businessScenarios = [];
        if (examples.Enabled && !string.IsNullOrWhiteSpace(examples.ScenariosPath))
        {
            var scenariosFullPath = BrunoPaths.Resolve(examples.ScenariosPath!, baseDir);
            if (!File.Exists(scenariosFullPath))
            {
                // Only reachable in dry-run: the real run created the template before this
                // point. Nothing was provided yet — treat as no business scenarios.
                warnings.Add($"[scenarios] 情境檔尚未建立，實際執行時將自動產生範本：{examples.ScenariosPath}");
            }
            else
            {
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
        }

        // ---- run engine ----
        Func<OperationIdentity, bool>? filter = BuildFilter(request);
        var generator = new ScenarioGenerator(examples);
        var engine = new BrunoSyncEngine(settings, examples, toolVersion, generator, businessScenarios);
        var (collection, syncOutcome) = engine.Sync(existing, document, filter);

        // A filter that matches nothing is almost always a typo — say so instead of
        // reporting a silent "success".
        if (filtering && syncOutcome.Operations.Count == 0)
        {
            warnings.Add("指定的 --operation/--path 過濾條件未符合任何操作；未做任何變更。");
        }

        var allWarnings = warnings.Concat(syncOutcome.Warnings).ToList();
        var finalOutcome = syncOutcome.Warnings.SequenceEqual(allWarnings)
            ? syncOutcome
            : syncOutcome with { Warnings = allWarnings };
        var writeReport = BrunoSyncReportPolicy.HasMeaningfulChanges(finalOutcome);
        var reportTimestamp = writeReport ? GetReportTimestamp(request) : (DateTimeOffset?)null;
        var reportPaths = reportTimestamp is { } timestamp
            ? ResolveReportArtifactPaths(reportDir, timestamp)
            : null;
        if (reportPaths is not null &&
            (PathsCollide(statePath, reportPaths.HtmlPath) || PathsCollide(statePath, reportPaths.JsonPath)))
        {
            throw new SentinelConfigException(
                $"'bruno.sync.statePath' ('{statePath}') collides with a sync-report artifact path.");
        }

        // ---- official OpenCollection schema gate (bundled interchange artifact) ----
        // generate → serialize → validate against the pinned official v1.0 schema.
        string collectionYaml = collection.ToYaml();
        var (schemaValid, schemaErrors) = OpenApiSentinel.Bruno.Validation.OpenCollectionSchemaValidator.Validate(
            collectionYaml, collectionFile);
        if (!schemaValid)
        {
            ReportValidationFailure(schemaErrors, console);
            return ExitCodes.InvalidInput;
        }

        // ---- Bruno-native (.bru) workspace: build plan + structural validation gate ----
        var workspacePlan = BruWorkspaceWriter.Plan(collection, workspaceDir, rawRegistry);
        var bruFiles = new List<(string RelativePath, string Content)>();
        foreach (var (file, content) in workspacePlan.FilesToWrite)
        {
            if (Path.GetFileName(file) == BruWorkspaceNames.ManifestFileName)
            {
                continue; // validated separately as JSON below
            }

            var relative = Path.GetFullPath(file)[workspaceDir.TrimEnd(Path.DirectorySeparatorChar).Length..]
                .TrimStart(Path.DirectorySeparatorChar)
                .Replace('\\', '/');
            bruFiles.Add((relative, content));
        }

        var manifestContent = BruWorkspaceWriter.Manifest(collection);
        var (nativeValid, nativeErrors) = OpenApiSentinel.Bruno.Validation.BruCollectionValidator.Validate(
            workspaceDir, manifestContent, bruFiles);
        if (!nativeValid)
        {
            console.WriteError("Bruno native collection validation failed:");
            foreach (var error in nativeErrors.Take(OpenApiSentinel.Bruno.Validation.OpenCollectionValidationException.MaxReportedErrors))
            {
                console.WriteError($"  - {error.Format()}");
            }

            console.WriteError("已拒絕寫入：Bruno 原生工作區未通過結構驗證。");
            return ExitCodes.InvalidInput;
        }

        // ---- render console summary ----
        RenderConsoleSummary(finalOutcome, request, source, collectionFile, workspaceDir, console);

        // ---- persist ----
        if (!request.DryRun && !finalOutcome.Skipped)
        {
            if (isRemoteSource)
            {
                BrunoPaths.EnsureDirectoryAvailable(openApiDir);
                Directory.CreateDirectory(openApiDir);
                var snapshotContent = outcomeDoc.Loaded?.Content ?? "";
                if (!File.Exists(snapshotFile) ||
                    !string.Equals(File.ReadAllText(snapshotFile), snapshotContent, StringComparison.Ordinal))
                {
                    SafeFileWriter.Write(snapshotFile, snapshotContent);
                }
            }
            else if (bootstrapped && !Directory.Exists(openApiDir))
            {
                BrunoPaths.EnsureDirectoryAvailable(openApiDir);
                Directory.CreateDirectory(openApiDir);
                console.Out($"[建立] {BrunoBootstrap.OpenApiDirRelative}");
            }

            if (scenarioTemplatePending)
            {
                var scenariosFullPath = BrunoPaths.Resolve(examples.ScenariosPath!, baseDir);
                var scenariosDir = Path.GetDirectoryName(scenariosFullPath);
                BrunoPaths.EnsureDirectoryAvailable(scenariosFullPath);
                Directory.CreateDirectory(string.IsNullOrEmpty(scenariosDir) ? "." : scenariosDir);
                SafeFileWriter.Write(scenariosFullPath, BrunoBootstrap.ScenarioTemplate);
                console.Out($"[建立] {examples.ScenariosPath}");
            }

            if (!string.IsNullOrEmpty(collectionParent))
            {
                BrunoPaths.EnsureDirectoryAvailable(collectionParent);
                Directory.CreateDirectory(collectionParent);
            }

            SafeFileWriter.Write(collectionFile, collectionYaml);

            foreach (var change in BrunoWorkspaceExecute(workspacePlan, existing, workspaceDir).Order(StringComparer.Ordinal))
            {
                console.Out($"[建立] {change}");
            }

            if (reportPaths is not null && reportTimestamp is { } capturedReportTimestamp)
            {
                var report = BuildReport(finalOutcome, request,
                    request.SourceOverride is null ? rawSource : source, rawCollectionPath, toolVersion, capturedReportTimestamp);
                Directory.CreateDirectory(reportDir);
                SafeFileWriter.Write(reportPaths.HtmlPath, BrunoSyncHtmlReporter.Render(report));
                SafeFileWriter.Write(reportPaths.JsonPath, BrunoSyncJsonReporter.Render(report));
            }
            else
            {
                console.Out("[sentinel] no API contract changes; report generation skipped");
            }

            // State is written LAST: any artifact failure above leaves it stale so the next
            // run re-syncs instead of wrongly skipping.
            BrunoSyncStateStore.Write(statePath, expectedState with { LastSyncUtc = DateTime.UtcNow.ToString("u") });

            if (bootstrapConfigPath is not null && bootstrapConfigContent is not null)
            {
                SafeFileWriter.Write(bootstrapConfigPath, bootstrapConfigContent);
                console.Out("[建立] openapi-sentinel.yml");
            }

            if (reportPaths is not null)
            {
                console.Out($"已寫入報告：{reportPaths.HtmlPath}");
                console.Out($"             {reportPaths.JsonPath}");
            }
        }
        else if (request.DryRun)
        {
            // Full bootstrap preview without touching the filesystem.
            foreach (var line in workspacePlan.Describe().Order(StringComparer.Ordinal))
            {
                console.Out(line);
            }

            if (!File.Exists(collectionFile))
            {
                console.Out($"[預計建立檔案] {collectionFile}");
            }

            console.Out("(dry-run) 未修改任何檔案。");
        }

        return ExitCodes.Ok;
    }

    private static void ReportValidationFailure(
        IReadOnlyList<OpenApiSentinel.Bruno.Validation.OpenCollectionValidationError> errors,
        IConsoleWriter console)
    {
        console.WriteError("OpenCollection validation failed:");
        foreach (var error in errors.Take(OpenApiSentinel.Bruno.Validation.OpenCollectionValidationException.MaxReportedErrors))
        {
            console.WriteError($"  - {error.Format()}");
        }

        console.WriteError("已拒絕寫入：collection 未通過官方 OpenCollection v1.0 schema 驗證。");
    }

    /// <summary>
    /// Executes a native-workspace plan. During a legacy OpenCollection-folder migration
    /// (opencollection.yml present), Sentinel-bound legacy artifacts are deleted so Bruno
    /// does not show duplicated items: the root opencollection.yml, every folder.yml, and
    /// every bound request .yml file. Unbound user .yml files are never touched.
    /// </summary>
    private static List<string> BrunoWorkspaceExecute(
        WorkspacePlan plan,
        BrunoCollectionDocument? existing,
        string workspaceDir)
    {
        if (existing is not null && File.Exists(Path.Combine(workspaceDir, "opencollection.yml")))
        {
            plan.OrphanedFiles.Add(Path.GetFullPath(Path.Combine(workspaceDir, "opencollection.yml")));
            foreach (var file in Directory.EnumerateFiles(workspaceDir, "*.yml", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(file);
                var fileName = Path.GetFileName(full);
                if (fileName is "opencollection.yml" or "folder.yml")
                {
                    if (!plan.OrphanedFiles.Contains(full))
                    {
                        plan.OrphanedFiles.Add(full);
                    }

                    continue;
                }

                try
                {
                    var node = BrunoYaml.ParseDocument(File.ReadAllText(full), full);
                    if (new OpenApiSentinel.Bruno.Model.BrunoItem(node).IsSentinelManaged && !plan.OrphanedFiles.Contains(full))
                    {
                        plan.OrphanedFiles.Add(full);
                    }
                }
                catch (BrunoFormatException)
                {
                    // unreadable/unrelated YAML — user data, never deleted
                }
            }
        }

        return OpenApiSentinel.Bruno.BrunoWorkspaceWriter.Execute(plan);
    }

    private static bool IsSameOrUnder(string path, string directory)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (PathsCollide(full, dir))
        {
            return true;
        }

        return full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsCollide(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        // Windows paths are case-insensitive; OrdinalIgnoreCase is deliberately over-strict
        // on case-sensitive filesystems rather than risk a destructive collision there.
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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
        string toolVersion,
        DateTimeOffset reportTimestamp)
    {
        var operations = BrunoSyncReportPolicy.DetailOperations(outcome).Select(o => new BrunoSyncReportOperation
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
            GeneratedAtUtc = reportTimestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
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

    internal static DateTimeOffset GetReportTimestamp(BrunoSyncRequest request) =>
        request.TimeProvider.GetUtcNow();

    /// <summary>Allocates a UTC timestamped HTML/JSON pair without ever overwriting history.
    /// A same-second collision receives the same deterministic numeric suffix on both files.</summary>
    internal static ReportArtifactPaths ResolveReportArtifactPaths(string reportDir, DateTimeOffset timestamp)
    {
        var stamp = timestamp.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        for (var suffix = 0; ; suffix++)
        {
            var suffixText = suffix == 0 ? "" : $"-{suffix:D2}";
            var baseName = $"bruno-sync-report-{stamp}{suffixText}";
            var htmlPath = Path.Combine(reportDir, baseName + ".html");
            var jsonPath = Path.Combine(reportDir, baseName + ".json");
            if (!File.Exists(htmlPath) && !File.Exists(jsonPath))
            {
                return new ReportArtifactPaths(htmlPath, jsonPath, stamp + suffixText);
            }
        }
    }

    private static void RenderConsoleSummary(
        BrunoSyncOutcome outcome,
        BrunoSyncRequest request,
        string source,
        string collectionFile,
        string workspaceDir,
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
        console.Out($"Bruno Collection（OpenCollection 交換格式）：{(request.DryRun ? "(dry-run)" : collectionFile)}");
        console.Out($"Bruno 原生工作區（Bruno Desktop 直接開啟）：{(request.DryRun ? "(dry-run)" : workspaceDir)}");

        // Honest validation-gate reporting (§11).
        console.Out("Bundled OpenCollection Validation: PASS");
        console.Out("Bruno Native Collection Validation: PASS");
        console.Out("Bruno Desktop Manual Smoke Test: REQUIRED");

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

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

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

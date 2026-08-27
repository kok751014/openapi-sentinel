using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenApiSentinel.Bruno.Model;
using OpenApiSentinel.Bruno.Scenarios;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno;

/// <summary>
/// Incremental, preservation-first sync of an OpenAPI contract into a Bruno
/// OpenCollection YAML file. Contract-derived fields are owned by Sentinel; user-maintained
/// values, scripts, tests, docs and auth are preserved. Deleted operations are reported
/// (stale) unless removal is explicitly enabled.
/// </summary>
public sealed class BrunoSyncEngine
{
    private static readonly Regex TemplateParamRegex = new(@"\{([^/{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex BrunoPathParamRegex = new(@"(^|/):([A-Za-z0-9_.\-]+)", RegexOptions.Compiled);

    private readonly BrunoSyncSettings syncSettings;
    private readonly BrunoExampleSettings exampleSettings;
    private readonly ScenarioGenerator? generator;
    private readonly IReadOnlyList<BusinessScenario> businessScenarios;
    private readonly string toolVersion;
    private SentinelMetaStore store = null!;

    public BrunoSyncEngine(
        BrunoSyncSettings syncSettings,
        BrunoExampleSettings exampleSettings,
        string toolVersion,
        ScenarioGenerator? generator = null,
        IReadOnlyList<BusinessScenario>? businessScenarios = null)
    {
        this.syncSettings = syncSettings;
        this.exampleSettings = exampleSettings;
        this.toolVersion = toolVersion;
        this.generator = exampleSettings.Enabled ? generator : null;
        this.businessScenarios = businessScenarios ?? [];
    }

    /// <summary>Yields every HTTP-request item together with its current folder path.</summary>
    private static IEnumerable<(BrunoItem Item, IReadOnlyList<string> FolderSegments)> WalkRequestsWithFolders(
        BrunoCollectionDocument collection)
    {
        return Walk(collection.RootItems, []);

        static IEnumerable<(BrunoItem, IReadOnlyList<string>)> Walk(
            IEnumerable<BrunoItem> items, IReadOnlyList<string> path)
        {
            foreach (var item in items)
            {
                if (item.IsFolder)
                {
                    var name = item.Name ?? "";
                    var childPath = new List<string>(path) { name };
                    yield return (item, childPath);
                    foreach (var nested in Walk(item.Children, childPath))
                    {
                        yield return nested;
                    }
                }
                else
                {
                    yield return (item, path);
                }
            }
        }
    }

    /// <summary>Runs the sync against an existing collection (may be null for first run).
    /// When <paramref name="operationFilter"/> is set, only matching operations are processed
    /// and stale/removal detection is skipped entirely (a filtered view cannot judge deletion).</summary>
    public (BrunoCollectionDocument Collection, BrunoSyncOutcome Outcome) Sync(
        BrunoCollectionDocument? existing,
        ApiDocument document,
        Func<OperationIdentity, bool>? operationFilter = null)
    {
        globalSecurity = document.GlobalSecurity;
        var warnings = new List<string>();
        var conflicts = new List<SyncConflict>();
        var outcomes = new List<OperationSyncOutcome>();
        var filtering = operationFilter is not null;

        var baseUrl = ResolveBaseUrl(document);
        var collection = existing ?? BrunoCollectionDocument.CreateNew(CollectionNameFor(document));
        store = SentinelMetaStore.For(collection);

        // One-time migration: legacy generator versions stored metadata in an illegal
        // request-level x-sentinel block. Move it into the schema-legal collection-level
        // index before anything reads identity, so old files sync forward automatically.
        foreach (var note in store.MigrateLegacyItemBlocks(collection))
        {
            warnings.Add(note);
        }

        // ---- Build the operation inventory from the contract ----
        var operations = new List<(OperationIdentity Identity, Operation Operation, string ContentHash)>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pathItem in document.Paths.Values.OrderBy(p => p.Path, StringComparer.Ordinal))
        {
            foreach (var operation in pathItem.Operations.Values.OrderBy(o => o.Method))
            {
                var identity = OperationIdentity.From(operation);
                if (!seenKeys.Add(identity.StableKey))
                {
                    warnings.Add($"規格中存在重複的識別（operationId 或 method+path）：{identity.StableKey}；僅處理第一個。");
                    continue;
                }

                if (!filtering || operationFilter!(identity))
                {
                    operations.Add((identity, operation, ContractFingerprint.ComputeOperationHash(identity, operation)));
                }
            }
        }

        // ---- Index the collection ----
        var markedIndex = new Dictionary<string, BrunoItem>(StringComparer.Ordinal);
        // Secondary index: marked items by their stored method+path. Handles identity
        // transitions in BOTH directions (operationId added since last sync AND removed).
        var markedByMethodPath = new Dictionary<string, BrunoItem>(StringComparer.Ordinal);
        var unmarkedCandidates = new List<(BrunoItem Item, OperationIdentity Identity)>();
        var currentFolders = new Dictionary<BrunoItem, IReadOnlyList<string>>(ReferenceEqualityComparer.Instance);
        foreach (var (item, folderSegments) in WalkRequestsWithFolders(collection))
        {
            if (folderSegments.Count > 0)
            {
                currentFolders[item] = folderSegments;
            }

            // Sentinel-bound requests are indexed by their recorded identity regardless of
            // whether it is operationId- or method+path-based.
            var identity = item.IdentityOf(store);
            if (identity is not null)
            {
                if (!markedIndex.TryAdd(identity.StableKey, item))
                {
                    conflicts.Add(new SyncConflict(
                        "duplicate-identity",
                        $"集合中有多個請求對應同一識別 {identity.StableKey}；僅同步第一個。"));
                }

                if (!string.IsNullOrEmpty(identity.Method) && !string.IsNullOrEmpty(identity.NormalizedPath))
                {
                    markedByMethodPath.TryAdd($"m+p:{identity.Method} {identity.NormalizedPath}", item);
                }

                continue;
            }

            if (ParseUrlIdentity(item.Http) is { } candidate)
            {
                unmarkedCandidates.Add((item, candidate));
            }
        }

        var matchedItems = new HashSet<BrunoItem>(ReferenceEqualityComparer.Instance);
        var seqCounter = NextSeq(collection);

        foreach (var (identity, operation, contentHash) in operations)
        {
            var item = FindTarget(markedIndex, markedByMethodPath, unmarkedCandidates, identity, matchedItems, conflicts);

            if (item is null)
            {
                outcomes.Add(CreateRequest(collection, identity, operation, contentHash, baseUrl, ref seqCounter));
                continue;
            }

            outcomes.Add(UpdateRequest(item, identity, operation, contentHash, baseUrl, currentFolders));
        }

        // ---- Stale / removed handling (skipped for filtered runs) ----
        // An item counts as alive if it was MATCHED this run — regardless of whether its
        // stored StableKey still matches (identity may have transitioned opid ↔ m+p).
        var knownKeys = operations.Select(o => o.Identity.StableKey).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, item) in markedIndex.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (knownKeys.Contains(key) || filtering || matchedItems.Contains(item))
            {
                continue;
            }

            var identity = item.IdentityOf(store);
            var method = identity?.Method;
            var path = identity?.NormalizedPath;
            if (string.IsNullOrEmpty(method) || string.IsNullOrEmpty(path))
            {
                var derived = ParseUrlIdentity(item.Http);
                method = string.IsNullOrEmpty(method) ? derived?.Method ?? "" : method;
                path = string.IsNullOrEmpty(path) ? derived?.NormalizedPath ?? "" : path;
            }

            if (syncSettings.RemoveDeletedOperations)
            {
                RemoveItem(collection, item, key);
                outcomes.Add(new OperationSyncOutcome
                {
                    Action = OperationAction.Removed,
                    Method = method,
                    Path = path,
                    OperationId = identity?.OperationId,
                    RequestName = item.Name,
                });
            }
            else
            {
                BrunoYaml.SetBool(store.GetOrCreateEntry(key), "stale", true);
                outcomes.Add(new OperationSyncOutcome
                {
                    Action = OperationAction.Stale,
                    Method = method,
                    Path = path,
                    OperationId = identity?.OperationId,
                    RequestName = item.Name,
                    Warnings = ["此操作已從 OpenAPI 規格移除；依預設保留 Bruno Request（removeDeletedOperations=false）。"],
                });
            }
        }

        return (collection, new BrunoSyncOutcome
        {
            Operations = outcomes.OrderBy(o => o.Path, StringComparer.Ordinal)
                .ThenBy(o => o.Method, StringComparer.Ordinal).ToList(),
            Conflicts = conflicts,
            Warnings = warnings,
            CreatedCollection = existing is null,
        });
    }

    private OperationSyncOutcome UpdateRequest(
        BrunoItem item,
        OperationIdentity identity,
        Operation operation,
        string contentHash,
        string baseUrl,
        Dictionary<BrunoItem, IReadOnlyList<string>> currentFolders)
    {
        var request = new BrunoRequestView(item.Node);
        var changes = new List<string>();
        var preserved = new List<string>();
        var itemWarnings = new List<string>();

        // §4 folder/category mapping: report divergence between the contract's tag-derived
        // folder and the item's current location. Never move automatically — relocation is
        // a user decision (preservation-first).
        if (currentFolders.TryGetValue(item, out var current) &&
            !current.SequenceEqual(FolderSegments(operation), StringComparer.Ordinal))
        {
            var desired = string.Join("/", FolderSegments(operation));
            var actual = string.Join("/", current);
            itemWarnings.Add($"合約標籤對應的資料夾為「{desired}」；此 Request 目前位於「{actual}」。依保留原則不自動搬移，可自行調整標籤或移動 Request。");
        }
        var storedHash = item.StoredContentHash(store);
        var structuralChange = storedHash != contentHash;
        // An unmarked (user-created) request that we adopt counts as newly managed.
        var action = item.IsSentinelManaged ? OperationAction.Updated : OperationAction.Added;

        if (!structuralChange)
        {
            RefreshMeta(item, identity, contentHash, RequiresAuth(operation));
            SyncExamples(item, identity, operation, request, baseUrl, changes, preserved, itemWarnings);

            if (changes.Count == 0 && preserved.Count == 0)
            {
                return new OperationSyncOutcome
                {
                    Action = OperationAction.Unchanged,
                    Method = identity.Method,
                    Path = identity.NormalizedPath,
                    OperationId = identity.OperationId,
                    RequestName = item.Name,
                };
            }

            return new OperationSyncOutcome
            {
                Action = OperationAction.Updated,
                Method = identity.Method,
                Path = identity.NormalizedPath,
                OperationId = identity.OperationId,
                RequestName = item.Name,
                Changes = changes,
                Preserved = preserved,
            };
        }

        // ---- method / url ----
        var url = BuildUrl(baseUrl, identity.NormalizedPath);
        if (!string.Equals(request.Method, identity.Method, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"method：{request.Method ?? "(none)"} → {identity.Method}");
            request.Method = identity.Method;
        }

        if (!string.Equals(request.Url, url, StringComparison.Ordinal))
        {
            changes.Add($"url → {url}");
            request.Url = url;
        }

        // ---- display name hygiene ----
        // Names are FROZEN at creation (preservation-first: contract prose changes and user
        // renames never churn existing names). Only exception: a stored name violating the
        // concise-name policy (e.g. pre-fix HTML/multiline summaries leaking into names)
        // is sanitized in place.
        var sanitizedName = ConciseName(request.Name ?? "");
        if (!string.Equals(request.Name, sanitizedName, StringComparison.Ordinal))
        {
            request.SetName(sanitizedName);
            changes.Add($"名稱已整理為安全簡潔格式：{sanitizedName}");
        }

        // ---- params (query + path) ----
        MergeParams(item, request, operation, changes, preserved, identity: identity);

        // ---- headers ----
        MergeHeaderParams(item, request, operation, changes, preserved, identity);

        // ---- body ----
        MergeBody(item, request, operation, changes, preserved, itemWarnings, identity);

        // ---- docs (contract summary/description) ----
        if (!syncSettings.PreserveDocs)
        {
            var docs = BuildDocs(operation);
            if (docs is not null)
            {
                BrunoYaml.SetString(item.Node, "docs", docs);
                changes.Add("docs 已由合約重新產生");
            }
        }
        else
        {
            var contractDocs = BuildDocs(operation) ?? "";
            var currentDocs = request.Docs;
            if (string.IsNullOrWhiteSpace(currentDocs))
            {
                if (contractDocs.Length > 0)
                {
                    BrunoYaml.SetString(item.Node, "docs", contractDocs);
                }
            }
            else
            {
                preserved.Add("文件內容");
            }

            if (operation.Deprecated && !string.IsNullOrWhiteSpace(request.Docs) &&
                !request.Docs!.Contains("deprecated", StringComparison.OrdinalIgnoreCase))
            {
                const string marker = "> ⚠️ 此操作已在 OpenAPI 規格中標示為 deprecated。";
                BrunoYaml.SetString(item.Node, "docs", request.Docs + "\n\n" + marker);
                changes.Add("docs 加入 deprecated 警示");
            }
        }

        RefreshMeta(item, identity, contentHash, RequiresAuth(operation));
        SyncExamples(item, identity, operation, request, baseUrl, changes, preserved, itemWarnings);

        return new OperationSyncOutcome
        {
            Action = action,
            Method = identity.Method,
            Path = identity.NormalizedPath,
            OperationId = identity.OperationId,
            RequestName = item.Name,
            Changes = changes,
            Preserved = preserved,
            Warnings = itemWarnings,
        };
    }

    private bool RequiresAuth(Operation operation) =>
        (operation.Security ?? globalSecurity) is { Count: > 0 };

    private IReadOnlyList<SecurityRequirementInfo>? globalSecurity;

    private void RefreshMeta(BrunoItem item, OperationIdentity identity, string contentHash, bool requiresAuth)
    {
        var previousKey = item.SentinelTagKey;
        item.WriteIdentity(store, identity, contentHash);
        item.SetGeneratorVersion(store, toolVersion);
        var meta = store.GetOrCreateEntry(identity.StableKey);

        // Identity transition (operationId added/removed since the last sync): carry every
        // extra field from the old index entry onto the new one — nothing user-stored is
        // silently dropped — then retire the orphaned entry.
        if (previousKey is { } old && !string.Equals(old, identity.StableKey, StringComparison.Ordinal))
        {
            store.MergeAndRemoveEntry(old, identity.StableKey);
            meta = store.GetOrCreateEntry(identity.StableKey);
        }

        BrunoYaml.SetBool(meta, "requiresAuth", requiresAuth);
        BrunoYaml.RemoveKey(meta, "stale");
    }

    private void SyncExamples(
        BrunoItem item,
        OperationIdentity identity,
        Operation operation,
        BrunoRequestView request,
        string baseUrl,
        List<string> changes,
        List<string> preserved,
        List<string>? warnings = null)
    {
        if (generator is null)
        {
            RemoveRecordedExamples(item, request, changes);
            return;
        }

        var baseSnapshot = BuildScenarioBaseSnapshot(operation, baseUrl);
        if (baseSnapshot is null)
        {
            return;
        }

        var scenarios = generator.Generate(identity, operation, baseSnapshot).ToList();

        // Layer 2 — business scenarios declared in openapi-sentinel.scenarios.yml.
        scenarios.AddRange(BuildBusinessScenarios(identity, baseSnapshot));

        // Honest response-example reporting: when the contract documents no derivable
        // outcome for success/failure probes, say so instead of silently shipping
        // examples without expected responses.
        var (_, successDocumented) = ResponseExampleFactory.ForSuccess(operation);
        if (!successDocumented && warnings is not null)
        {
            warnings.Add($"此操作的「成功」情境未附預期回應：{ResponseExampleFactory.UnavailableNote}");
        }

        var examplesSeq = request.ExamplesSequence();
        var recorded = item.RecordedExamples(store);
        var nameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < examplesSeq.Children.Count; i++)
        {
            if (examplesSeq.Children[i] is YamlDotNet.RepresentationModel.YamlMappingNode map &&
                BrunoYaml.AsString(BrunoYaml.Get(map, "name")) is { } name)
            {
                nameToIndex.TryAdd(name, i);
            }
        }

        var registry = new List<(string Id, string Name)>();
        var desiredNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var scenario in scenarios)
        {
            desiredNames.Add(scenario.Name);
            registry.Add((scenario.Id, scenario.Name));

            if (nameToIndex.TryGetValue(scenario.Name, out var index) &&
                examplesSeq.Children[index] is YamlDotNet.RepresentationModel.YamlMappingNode existingExample)
            {
                RewriteExample(existingExample, scenario);
            }
            else
            {
                var node = BuildExampleNode(scenario);
                examplesSeq.Children.Add(node);
                nameToIndex[scenario.Name] = examplesSeq.Children.Count - 1;
                changes.Add($"新增範例：{scenario.Name}");
            }
        }

        // Drop sentinel-recorded examples whose scenarios no longer exist.
        var staleRecorded = recorded.Where(r => !desiredNames.Contains(r.Name)).ToList();
        if (staleRecorded.Count > 0)
        {
            for (var i = examplesSeq.Children.Count - 1; i >= 0; i--)
            {
                if (examplesSeq.Children[i] is YamlDotNet.RepresentationModel.YamlMappingNode map &&
                    BrunoYaml.AsString(BrunoYaml.Get(map, "name")) is { } name &&
                    staleRecorded.Any(r => r.Name == name))
                {
                    examplesSeq.Children.RemoveAt(i);
                    changes.Add($"移除過時範例：{name}");
                }
            }

            registry.RemoveAll(r => staleRecorded.Contains(r));
        }

        // User-authored examples are never touched.
        foreach (var name in nameToIndex.Keys.Order(StringComparer.Ordinal))
        {
            if (!desiredNames.Contains(name))
            {
                preserved.Add($"保留人工範例：{name}");
            }
        }

        item.WriteRecordedExamples(store, registry);
    }

    /// <summary>
    /// Disabling generated examples removes only entries recorded in Sentinel's per-request
    /// registry. An unmarked example is user content, including a legacy native-workspace
    /// example whose old <c>.bru</c> file predates the ownership sidecar.
    /// </summary>
    private void RemoveRecordedExamples(BrunoItem item, BrunoRequestView request, List<string> changes)
    {
        var recorded = item.RecordedExamples(store);
        if (recorded.Count == 0)
        {
            return;
        }

        var names = recorded.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        if (request.ExamplesOrNull is { } examples)
        {
            for (var i = examples.Children.Count - 1; i >= 0; i--)
            {
                if (examples.Children[i] is YamlDotNet.RepresentationModel.YamlMappingNode map &&
                    BrunoYaml.AsString(BrunoYaml.Get(map, "name")) is { } name && names.Contains(name))
                {
                    examples.Children.RemoveAt(i);
                    changes.Add($"停用 Sentinel 產生範例：{name}");
                }
            }
        }

        item.WriteRecordedExamples(store, []);
    }

    private IEnumerable<GeneratedScenario> BuildBusinessScenarios(OperationIdentity identity, ScenarioRequest baseSnapshot)
    {
        foreach (var scenario in businessScenarios)
        {
            if (!string.Equals(scenario.OperationId, identity.OperationId, StringComparison.Ordinal))
            {
                continue;
            }

            var mutated = baseSnapshot;
            if (scenario.Body is not null)
            {
                mutated = mutated with
                {
                    JsonBody = scenario.Body.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    }),
                };
            }

            yield return new GeneratedScenario(
                Id: StableBusinessId(identity.OperationId ?? identity.StableKey, scenario.Name),
                Name: scenario.Name,
                Description: scenario.Description ?? "此情境由 openapi-sentinel.scenarios.yml 明確定義。",
                Category: ScenarioCategory.SuccessFull,
                Request: mutated);
        }
    }

    internal static string StableBusinessId(string operationKey, string name) =>
        "biz-" + ContractFingerprint.Sha256Hex($"{operationKey}|{name}")[..16];

    private static YamlDotNet.RepresentationModel.YamlMappingNode BuildExampleNode(GeneratedScenario scenario)
    {
        var node = new YamlDotNet.RepresentationModel.YamlMappingNode();
        RewriteExample(node, scenario);
        return node;
    }

    private static void RewriteExample(YamlDotNet.RepresentationModel.YamlMappingNode node, GeneratedScenario scenario)
    {
        BrunoYaml.SetString(node, "name", scenario.Name);
        BrunoYaml.SetString(node, "description", scenario.Description);

        var request = BrunoYaml.GetOrCreateMapping(node, "request");
        BrunoYaml.SetString(request, "url", scenario.Request.Url);
        BrunoYaml.SetString(request, "method", scenario.Request.Method);

        if (scenario.Request.Headers.Count > 0)
        {
            var headers = new YamlDotNet.RepresentationModel.YamlSequenceNode();
            foreach (var header in scenario.Request.Headers)
            {
                var row = new YamlDotNet.RepresentationModel.YamlMappingNode();
                BrunoYaml.SetString(row, "name", header.Name);
                BrunoYaml.SetString(row, "value", header.Value);
                if (!header.Enabled)
                {
                    BrunoYaml.SetBool(row, "disabled", true);
                }

                headers.Children.Add(row);
            }

            BrunoYaml.Set(request, "headers", headers);
        }
        else
        {
            BrunoYaml.RemoveKey(request, "headers");
        }

        if (scenario.Request.Params.Count > 0)
        {
            var paramsSeq = new YamlDotNet.RepresentationModel.YamlSequenceNode();
            foreach (var param in scenario.Request.Params)
            {
                var row = new YamlDotNet.RepresentationModel.YamlMappingNode();
                BrunoYaml.SetString(row, "name", param.Name);
                BrunoYaml.SetString(row, "value", param.Value);
                // The official schema REQUIRES type on every param row.
                BrunoYaml.SetString(row, "type",
                    string.Equals(param.Type, "path", StringComparison.Ordinal) ? "path" : "query");

                if (!param.Enabled)
                {
                    BrunoYaml.SetBool(row, "disabled", true);
                }

                paramsSeq.Children.Add(row);
            }

            BrunoYaml.Set(request, "params", paramsSeq);
        }
        else
        {
            BrunoYaml.RemoveKey(request, "params");
        }

        if (scenario.Request.JsonBody is not null)
        {
            var body = new YamlDotNet.RepresentationModel.YamlMappingNode();
            BrunoYaml.SetString(body, "type", "json");
            BrunoYaml.Set(body, "data", new YamlDotNet.RepresentationModel.YamlScalarNode(scenario.Request.JsonBody)
            {
                Style = YamlDotNet.Core.ScalarStyle.Literal,
            });
            BrunoYaml.Set(request, "body", body);
        }
        else if (scenario.Request.FormRows is { Count: > 0 } formRows)
        {
            var body = new YamlDotNet.RepresentationModel.YamlMappingNode();
            BrunoYaml.SetString(body, "type",
                scenario.Request.FormKind == BodyKind.MultipartForm ? "multipart-form" : "form-urlencoded");
            var data = new YamlDotNet.RepresentationModel.YamlSequenceNode();
            foreach (var row in formRows)
            {
                var map = new YamlDotNet.RepresentationModel.YamlMappingNode();
                BrunoYaml.SetString(map, "name", row.Name);
                if (scenario.Request.FormKind == BodyKind.MultipartForm)
                {
                    BrunoYaml.SetString(map, "type", row.IsFile ? "file" : "text");
                }

                BrunoYaml.SetString(map, "value", row.Value);
                if (!row.Enabled)
                {
                    BrunoYaml.SetBool(map, "disabled", true);
                }

                data.Children.Add(map);
            }

            BrunoYaml.Set(body, "data", data);
            BrunoYaml.Set(request, "body", body);
        }
        else
        {
            BrunoYaml.RemoveKey(request, "body");
        }

        // ---- expected response (documented OpenAPI behavior only) ----
        if (scenario.Response is { } response)
        {
            var responseNode = new YamlDotNet.RepresentationModel.YamlMappingNode();
            if (response.Status is { } status)
            {
                BrunoYaml.SetInt(responseNode, "status", status);
            }

            if (!string.IsNullOrEmpty(response.StatusText))
            {
                BrunoYaml.SetString(responseNode, "statusText", response.StatusText);
            }

            if (response.Headers.Count > 0)
            {
                var responseHeaders = new YamlDotNet.RepresentationModel.YamlSequenceNode();
                foreach (var header in response.Headers)
                {
                    var row = new YamlDotNet.RepresentationModel.YamlMappingNode();
                    BrunoYaml.SetString(row, "name", header.Name);
                    BrunoYaml.SetString(row, "value", header.Value);
                    responseHeaders.Children.Add(row);
                }

                BrunoYaml.Set(responseNode, "headers", responseHeaders);
            }

            if (!string.IsNullOrEmpty(response.BodyData) || response.BodyType == "binary")
            {
                var responseBody = new YamlDotNet.RepresentationModel.YamlMappingNode();
                BrunoYaml.SetString(responseBody, "type", response.BodyType);
                BrunoYaml.Set(responseBody, "data",
                    new YamlDotNet.RepresentationModel.YamlScalarNode(response.BodyData ?? "")
                    {
                        Style = YamlDotNet.Core.ScalarStyle.Literal,
                    });
                BrunoYaml.Set(responseNode, "body", responseBody);
            }

            BrunoYaml.Set(node, "response", responseNode);
        }
        else
        {
            BrunoYaml.RemoveKey(node, "response");
        }
    }

    /// <summary>
    /// Builds the canonical base snapshot used for scenario generation (成功－完整參數 shape).
    /// Carries schema-declared query/path parameters AND header/cookie parameters so the
    /// generated examples are genuinely sendable without hand-editing. Public because the
    /// scenario tests exercise exactly this production plumbing.
    /// </summary>
    public static ScenarioRequest BuildScenarioBaseSnapshot(Operation operation, string baseUrl)
    {
        var identity = OperationIdentity.From(operation);
        var url = BuildUrl(baseUrl, identity.NormalizedPath);

        var parameters = new List<(string Name, string Value, string Type, bool Enabled)>();
        var headers = new List<(string Name, string Value, bool Enabled)>();
        var cookieParts = new List<string>();
        foreach (var parameter in operation.Parameters)
        {
            switch (parameter.In)
            {
                case ParameterLocation.Query:
                case ParameterLocation.Path:
                    parameters.Add((parameter.Name, SampleText(parameter),
                        parameter.In == ParameterLocation.Path ? "path" : "query", true));
                    break;
                case ParameterLocation.Header:
                    headers.Add((parameter.Name, HeaderSample(parameter), true));
                    break;
                case ParameterLocation.Cookie:
                    cookieParts.Add($"{parameter.Name}={SampleText(parameter)}");
                    break;
            }
        }

        string? body = null;
        BodyKind formKind = BodyKind.None;
        IReadOnlyList<FormRow>? formRows = null;
        if (operation.RequestBody is { } requestBody)
        {
            // ONE canonical producer feeds both the operation's default body and every
            // generated example — their structural shapes can never diverge.
            var canonical = CanonicalBodyFactory.Create(requestBody, SampleValueFactory.Depth.Full);
            body = canonical.Kind == BodyKind.Json ? canonical.Json : null;
            formKind = canonical.Kind is BodyKind.MultipartForm or BodyKind.FormUrlEncoded ? canonical.Kind : BodyKind.None;
            formRows = canonical.Rows;

            var contentType = canonical.ContentType;
            if (contentType is not null)
            {
                headers.Insert(0, ("Content-Type", contentType, true));
            }
        }

        if (cookieParts.Count > 0)
        {
            headers.Add(("Cookie", string.Join("; ", cookieParts.Order(StringComparer.Ordinal)), true));
        }

        return new ScenarioRequest(identity.Method, url, parameters, headers, body, formKind, formRows);
    }

    private static readonly JsonSerializerOptions JsonWriterOptionsIndented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static bool SchemaLooksLikeJson(RequestBodyInfo body) =>
        CanonicalBodyFactory.ResolveKind(body) == BodyKind.Json;

    private static string SampleText(ParameterInfo parameter) =>
        SampleValueFactory.ParameterSampleText(parameter);

    private OperationSyncOutcome CreateRequest(
        BrunoCollectionDocument collection,
        OperationIdentity identity,
        Operation operation,
        string contentHash,
        string baseUrl,
        ref int seqCounter)
    {
        var folderSegments = FolderSegments(operation);
        YamlDotNet.RepresentationModel.YamlMappingNode itemNode;
        if (folderSegments.Count == 0)
        {
            itemNode = new YamlDotNet.RepresentationModel.YamlMappingNode();
            collection.ItemsSequence().Children.Add(itemNode);
        }
        else
        {
            var folder = collection.GetOrCreateFolderPath(folderSegments);
            var nested = BrunoYaml.GetOrCreateSequence(folder.Node, "items");
            itemNode = new YamlDotNet.RepresentationModel.YamlMappingNode();
            nested.Children.Add(itemNode);
        }

        var item = new BrunoItem(itemNode);
        var request = new BrunoRequestView(itemNode);
        request.SetName(DefaultRequestName(identity, operation));
        request.SetSeq(seqCounter++);
        request.Method = identity.Method;
        request.Url = BuildUrl(baseUrl, identity.NormalizedPath);

        var changes = new List<string>();
        var preserved = new List<string>();
        var itemWarnings = new List<string>();

        MergeParams(item, request, operation, changes, preserved, creating: true, identity: identity);
        MergeHeaderParams(item, request, operation, changes, preserved, identity, creating: true);

        if (operation.RequestBody is { } requestBody)
        {
            // Same canonical factory as the generated examples: an array contract produces
            // an array default body, an object contract an object body — never {}.
            var canonical = CanonicalBodyFactory.Create(requestBody, SampleValueFactory.Depth.Full);
            switch (canonical.Kind)
            {
                case BodyKind.Json when canonical.Json is not null:
                    request.SetJsonBody(canonical.Json);
                    MarkBodyManaged(item, identity);
                    break;
                case BodyKind.MultipartForm or BodyKind.FormUrlEncoded when canonical.Rows is { Count: > 0 }:
                    request.SetFormBody(canonical.Kind == BodyKind.MultipartForm ? "multipart-form" : "form-urlencoded", canonical.Rows);
                    MarkBodyManaged(item, identity);
                    break;
                case BodyKind.Text when canonical.Text is not null:
                    request.SetTextBody(canonical.Text);
                    MarkBodyManaged(item, identity);
                    break;
                case BodyKind.Xml when canonical.Text is not null:
                    request.SetXmlBody(canonical.Text);
                    MarkBodyManaged(item, identity);
                    break;
            }
        }

        var docs = BuildDocs(operation);
        if (docs is not null)
        {
            BrunoYaml.SetString(itemNode, "docs", docs);
        }

        RefreshMeta(item, identity, contentHash, RequiresAuth(operation));
        SyncExamples(item, identity, operation, request, baseUrl, changes, preserved, itemWarnings);

        return new OperationSyncOutcome
        {
            Action = OperationAction.Added,
            Method = identity.Method,
            Path = identity.NormalizedPath,
            OperationId = identity.OperationId,
            RequestName = item.Name,
            Changes = changes,
        };
    }

    /// <summary>
    /// The index entry metadata writes go to: the item's currently bound entry when it has
    /// one (so a pre-RefreshMeta write survives identity transitions via the merge), or —
    /// for freshly created items that have no tag yet — the new operation's canonical entry.
    /// </summary>
    private YamlDotNet.RepresentationModel.YamlMappingNode MetaTarget(BrunoItem item, OperationIdentity identity) =>
        store.GetOrCreateEntry(item.SentinelTagKey ?? identity.StableKey);

    private void MarkBodyManaged(BrunoItem item, OperationIdentity identity) =>
        BrunoYaml.SetBool(MetaTarget(item, identity), "managedBody", true);

    /// <summary>
    /// Concise display/file name for a request: OpenAPI summary → operationId →
    /// "METHOD /path". Documentation prose NEVER becomes the name: HTML markup is stripped,
    /// newlines/control characters collapse to single spaces, and length is capped — while
    /// the full summary + description remain available verbatim for docs.
    /// </summary>
    public static string DisplayRequestName(OperationIdentity identity, Operation operation)
    {
        var candidate = !string.IsNullOrWhiteSpace(operation.Summary)
            ? operation.Summary
            : !string.IsNullOrWhiteSpace(identity.OperationId)
                ? identity.OperationId
                : $"{identity.Method} {identity.NormalizedPath}";
        return ConciseName(candidate!);
    }

    /// <summary>Cross-platform-safe concise naming: HTML stripped, whitespace collapsed,
    /// hard length cap. Useful zh-TW names survive untouched.</summary>
    public static string ConciseName(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var inTag = false;
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '<':
                    inTag = true;
                    sb.Append(' ');
                    break;
                case '>':
                    inTag = false;
                    break;
                case '\r' or '\n' or '\t' when !inTag:
                    sb.Append(' ');
                    break;
                default:
                    if (!inTag && !char.IsControl(ch))
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        var result = Regex.Replace(sb.ToString(), @" {2,}", " ").Trim();
        if (result.Length > 80)
        {
            result = result[..80].TrimEnd();
        }

        return result.Length == 0 ? "untitled" : result;
    }

    private static string DefaultRequestName(OperationIdentity identity, Operation operation) =>
        DisplayRequestName(identity, operation);

    internal static IReadOnlyList<string> FolderSegments(Operation operation)
    {
        var segments = new List<string>();
        foreach (var tag in operation.Tags)
        {
            foreach (var part in tag.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var sanitized = SanitizeFolderSegment(part);
                if (sanitized.Length > 0)
                {
                    segments.Add(sanitized);
                }
            }
        }

        return segments;
    }

    /// <summary>
    /// Folder names live only inside YAML values, but they must never look like relative
    /// path segments (".."), contain control characters, or grow unbounded — otherwise a
    /// crafted spec could produce confusing or hostile-looking collection structures.
    /// </summary>
    public static string SanitizeFolderSegment(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
        {
            return "";
        }

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(char.IsControl(ch) ? '_' : ch);
        }

        text = sb.ToString();

        // Collapse dot-runs that would read as traversal segments.
        while (text.Contains("..", StringComparison.Ordinal))
        {
            text = text.Replace("..", "_", StringComparison.Ordinal);
        }

        if (text.Length > 100)
        {
            text = text[..100];
        }

        return text.Trim().Length == 0 ? "untitled" : text.Trim();
    }

    /// <summary>Full documentation text: summary AND description preserved verbatim (the
    /// concise name is derived separately; backend wording is never "corrected").</summary>
    private static string? BuildDocs(Operation operation)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(operation.Summary))
        {
            parts.Add(operation.Summary!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(operation.Description))
        {
            parts.Add(operation.Description!.Trim());
        }

        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private void MergeParams(
        BrunoItem item,
        BrunoRequestView request,
        Operation operation,
        List<string> changes,
        List<string> preserved,
        bool creating = false,
        OperationIdentity? identity = null)
    {
        var existingRows = request.Params();
        var existingByName = new Dictionary<string, BrunoKeyValueRow>(StringComparer.Ordinal);
        foreach (var row in existingRows)
        {
            if (row.Name is { } name)
            {
                existingByName.TryAdd(name, row);
            }
        }

        var contractQueryNames = new HashSet<string>(StringComparer.Ordinal);
        var contractAllNames = new HashSet<string>(StringComparer.Ordinal);
        var resultRows = new List<(string Name, string Value, string Type, bool Enabled)>();

        // Existing rows keep their stored order; new contract rows append deterministically.
        foreach (var row in existingRows)
        {
            resultRows.Add((row.Name ?? "", row.Value ?? "", row.ParamType, row.Enabled));
        }

        foreach (var parameter in operation.Parameters)
        {
            if (parameter.In is not (ParameterLocation.Query or ParameterLocation.Path))
            {
                continue;
            }

            contractAllNames.Add(parameter.Name);
            var type = parameter.In == ParameterLocation.Path ? "path" : "query";
            if (type == "query")
            {
                contractQueryNames.Add(parameter.Name);
            }

            var sample = SampleText(parameter);

            var index = resultRows.FindIndex(r => r.Name == parameter.Name);
            if (index >= 0)
            {
                var current = resultRows[index];
                var newValue = syncSettings.PreserveManualValues
                    ? (current.Value.Length == 0 ? sample : current.Value)
                    : sample;
                if (current.Type != type)
                {
                    if (!creating)
                    {
                        changes.Add($"參數 {parameter.Name} 位置變更為 {type}");
                    }

                    resultRows[index] = (parameter.Name, newValue, type, current.Enabled);
                }
                else
                {
                    resultRows[index] = (parameter.Name, newValue, current.Type, current.Enabled);
                }
            }
            else
            {
                resultRows.Add((parameter.Name, sample, type, true));
                if (!creating)
                {
                    changes.Add($"新增參數：{parameter.Name}（{type}）");
                }
            }
        }

        // Sentinel-managed rows the contract no longer declares get dropped; purely manual
        // rows survive (they are user data).
        var managedNames = ReadManagedParams(item);
        var finalRows = new List<(string Name, string Value, string Type, bool Enabled)>();
        foreach (var row in resultRows)
        {
            var wasManaged = managedNames.Contains(row.Name);
            if (!contractAllNames.Contains(row.Name) && wasManaged)
            {
                if (!creating)
                {
                    changes.Add($"移除已從合約刪除的參數：{row.Name}");
                }

                continue;
            }

            finalRows.Add(row);
        }

        WriteManagedParams(item, finalRows.Where(r => contractAllNames.Contains(r.Name)).Select(r => r.Name),
            identity ?? throw new BrunoSyncException("internal: MergeParams requires an identity."));

        if (!RowsEqual(finalRows, existingRows))
        {
            request.SetParams(finalRows);
        }
    }

    private HashSet<string> ReadManagedParams(BrunoItem item)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (item.SentinelTagKey is not { } key || store.EntryOrNull(key) is not { } meta ||
            BrunoYaml.SequenceOf(meta, "managedParams") is not { } seq)
        {
            return names;
        }

        foreach (var child in seq.Children)
        {
            if (child is YamlDotNet.RepresentationModel.YamlScalarNode { Value: not null } scalar)
            {
                names.Add(scalar.Value);
            }
        }

        return names;
    }

    private void WriteManagedParams(BrunoItem item, IEnumerable<string> names, OperationIdentity identity)
    {
        var meta = MetaTarget(item, identity);
        var list = names.Order(StringComparer.Ordinal).ToList();
        if (list.Count == 0)
        {
            BrunoYaml.RemoveKey(meta, "managedParams");
            return;
        }

        var seq = new YamlDotNet.RepresentationModel.YamlSequenceNode();
        foreach (var name in list)
        {
            seq.Children.Add(BrunoYaml.Scalar(name));
        }

        BrunoYaml.Set(meta, "managedParams", seq);
    }

    private static bool RowsEqual(
        List<(string Name, string Value, string Type, bool Enabled)> rows,
        IReadOnlyList<BrunoKeyValueRow> existing)
    {
        if (rows.Count != existing.Count)
        {
            return false;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var e = existing[i];
            if (!string.Equals(rows[i].Name, e.Name, StringComparison.Ordinal) ||
                !string.Equals(rows[i].Value, e.Value, StringComparison.Ordinal) ||
                !string.Equals(rows[i].Type, e.ParamType, StringComparison.Ordinal) ||
                rows[i].Enabled != e.Enabled)
            {
                return false;
            }
        }

        return true;
    }

    private void MergeHeaderParams(
        BrunoItem item,
        BrunoRequestView request,
        Operation operation,
        List<string> changes,
        List<string> preserved,
        OperationIdentity identity,
        bool creating = false)
    {
        var contractHeaders = new Dictionary<string, string?>(StringComparer.Ordinal);
        var cookieParts = new List<string>();
        foreach (var parameter in operation.Parameters)
        {
            switch (parameter.In)
            {
                case ParameterLocation.Header:
                    contractHeaders[parameter.Name] = HeaderSample(parameter);
                    break;
                case ParameterLocation.Cookie:
                    cookieParts.Add($"{parameter.Name}={SampleText(parameter)}");
                    break;
            }
        }

        if (cookieParts.Count > 0)
        {
            contractHeaders["Cookie"] = string.Join("; ", cookieParts.Order(StringComparer.Ordinal));
        }

        var existingBefore = request.Headers().Select(h => (h.Name, h.Value, h.Enabled)).ToList();
        var managedBefore = ReadManagedHeaders(item);

        request.MergeHeaders(contractHeaders);

        // Sentinel manages exactly the union of previously- and currently-declared contract
        // headers so future removals drop them while purely manual headers survive forever.
        var managedAfter = managedBefore.Union(contractHeaders.Keys).ToHashSet(StringComparer.Ordinal);
        WriteManagedHeaders(item, managedAfter, identity);

        if (!creating)
        {
            var after = request.Headers().Select(h => (h.Name, h.Value, h.Enabled)).ToList();
            foreach (var name in contractHeaders.Keys.Order(StringComparer.Ordinal))
            {
                var existedBefore = existingBefore.Any(h => h.Name == name);
                var existsAfter = after.Any(h => h.Name == name);
                if (!existedBefore && existsAfter)
                {
                    changes.Add($"新增標頭：{name}");
                }
                else if (existedBefore && !existsAfter)
                {
                    changes.Add($"移除已從合約刪除的標頭：{name}");
                }
            }

            foreach (var header in existingBefore)
            {
                if (header.Name is { } name && !contractHeaders.ContainsKey(name) && !managedBefore.Contains(name))
                {
                    preserved.Add($"保留人工標頭：{name}");
                }
            }
        }
    }

    private static string HeaderSample(ParameterInfo parameter)
    {
        if (parameter.Schema is null)
        {
            return "sample";
        }

        if (SampleValueFactory.HasNonEmptyEnum(parameter.Schema) &&
            SampleValueFactory.FirstEnumValue(parameter.Schema) is { } enumValue &&
            enumValue is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s))
        {
            return s;
        }

        return SampleText(parameter);
    }

    private HashSet<string> ReadManagedHeaders(BrunoItem item)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (SentinelEntryOrNull(item) is not { } meta ||
            BrunoYaml.SequenceOf(meta, "managedHeaders") is not { } seq)
        {
            return names;
        }

        foreach (var child in seq.Children)
        {
            if (child is YamlDotNet.RepresentationModel.YamlScalarNode { Value: not null } scalar)
            {
                names.Add(scalar.Value);
            }
        }

        return names;
    }

    private void WriteManagedHeaders(BrunoItem item, HashSet<string> names, OperationIdentity identity)
    {
        var meta = MetaTarget(item, identity);
        if (names.Count == 0)
        {
            BrunoYaml.RemoveKey(meta, "managedHeaders");
            return;
        }

        var seq = new YamlDotNet.RepresentationModel.YamlSequenceNode();
        foreach (var name in names.Order(StringComparer.Ordinal))
        {
            seq.Children.Add(BrunoYaml.Scalar(name));
        }

        BrunoYaml.Set(meta, "managedHeaders", seq);
    }

    private void MergeBody(
        BrunoItem item,
        BrunoRequestView request,
        Operation operation,
        List<string> changes,
        List<string> preserved,
        List<string> itemWarnings,
        OperationIdentity identity)
    {
        var contractBody = operation.RequestBody;
        var contractKind = contractBody is null ? BodyKind.None : CanonicalBodyFactory.ResolveKind(contractBody);

        if (contractKind is BodyKind.None || contractBody is null)
        {
            if (request.BodyOrNull is not null)
            {
                if (IsBodyManaged(item))
                {
                    request.RemoveBody();
                    ClearBodyManaged(item, identity);
                    changes.Add("移除請求主體（合約已無 requestBody）");
                }
                else
                {
                    preserved.Add("保留人工請求主體（合約未宣告 requestBody）");
                }
            }

            return;
        }

        var contractMode = contractKind switch
        {
            BodyKind.Json => "json",
            BodyKind.MultipartForm => "multipart-form",
            BodyKind.FormUrlEncoded => "form-urlencoded",
            BodyKind.Xml => "xml",
            _ => "text",
        };

        var mode = request.BodyType;
        if (mode is not null && !string.Equals(mode, contractMode, StringComparison.Ordinal))
        {
            itemWarnings.Add($"請求主體模式為 '{mode}'，與合約宣告的 '{contractMode}' 不一致；已保留使用者內容，未自動改寫。");
            return;
        }

        switch (contractKind)
        {
            case BodyKind.Json:
                MergeJsonBody(item, request, contractBody, changes, preserved, itemWarnings, identity);
                break;
            case BodyKind.MultipartForm or BodyKind.FormUrlEncoded:
                MergeFormBody(item, request, contractBody, contractKind, changes, preserved, identity);
                break;
            case BodyKind.Text or BodyKind.Xml:
                MergeTextBody(item, request, contractBody, contractKind, changes, preserved, identity);
                break;
        }
    }

    private void MergeJsonBody(
        BrunoItem item,
        BrunoRequestView request,
        RequestBodyInfo contractBody,
        List<string> changes,
        List<string> preserved,
        List<string> itemWarnings,
        OperationIdentity identity)
    {
        var schema = ScenarioGenerator.PrimaryBodySchema(contractBody)!;
        var existingRaw = request.JsonBodyRaw;
        if (existingRaw is not null && !syncSettings.PreserveManualValues)
        {
            var regenerated = SampleValueFactory.Sample(schema, SampleValueFactory.Depth.Full, level: 2);
            request.SetJsonBody((regenerated as JsonObject ?? new JsonObject()).ToJsonString(JsonWriterOptionsIndented));
            MarkBodyManaged(item, identity);
            changes.Add("請求主體已由合約重新產生（preserveManualValues=false）");
            return;
        }

        if (existingRaw is not null)
        {
            try
            {
                _ = JsonNode.Parse(existingRaw);
            }
            catch (JsonException)
            {
                itemWarnings.Add("現有請求主體不是有效 JSON；為避免破壞人工內容，未進行合併。");
                return;
            }
        }

        var outcome = JsonBodyMerger.Merge(existingRaw, schema);
        var changed = !string.Equals(NormalizeJson(outcome.Json), NormalizeJson(existingRaw), StringComparison.Ordinal);
        if (changed)
        {
            request.SetJsonBody(outcome.Json);
            MarkBodyManaged(item, identity);
            foreach (var note in outcome.Notes)
            {
                if (note.StartsWith("added-optional:", StringComparison.Ordinal))
                {
                    changes.Add($"請求主體加入選填欄位：{note["added-optional:".Length..]}");
                }
                else if (note.StartsWith("removed-undeclared:", StringComparison.Ordinal))
                {
                    changes.Add($"請求主體移除規格外欄位：{note["removed-undeclared:".Length..]}");
                }
            }

            if (changes.Count == 0)
            {
                changes.Add("請求主體結構已依最新合約更新（既有值保留）");
            }

            preserved.Add("保留既有請求主體值");
        }
        else if (existingRaw is not null)
        {
            preserved.Add("保留既有請求主體值");
        }
    }

    private void MergeFormBody(
        BrunoItem item,
        BrunoRequestView request,
        RequestBodyInfo contractBody,
        BodyKind kind,
        List<string> changes,
        List<string> preserved,
        OperationIdentity identity)
    {
        var canonical = CanonicalBodyFactory.Create(contractBody, SampleValueFactory.Depth.Full);
        var contractRows = canonical.Rows ?? [];
        var contractNames = contractRows.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        var existingRows = request.FormRows();

        var managedBefore = ReadManagedFormFields(item);
        var result = new List<FormRow>();
        foreach (var existing in existingRows)
        {
            if (!contractNames.Contains(existing.Name))
            {
                if (managedBefore.Contains(existing.Name))
                {
                    changes.Add($"移除已從合約刪除的表單欄位：{existing.Name}");
                    continue;
                }

                preserved.Add($"保留人工表單欄位：{existing.Name}");
            }

            result.Add(existing);
        }

        var changed = false;
        foreach (var contractRow in contractRows)
        {
            var index = result.FindIndex(r => string.Equals(r.Name, contractRow.Name, StringComparison.Ordinal));
            if (index < 0)
            {
                result.Add(contractRow);
                changed = true;
                changes.Add(contractRow.IsFile
                    ? $"新增檔案欄位：{contractRow.Name}（請於送出前選擇實際檔案；佔位值不會讀取本機檔案）"
                    : $"新增表單欄位：{contractRow.Name}");
                continue;
            }

            var current = result[index];
            string nextValue;
            if (current.IsFile || contractRow.IsFile)
            {
                // Never overwrite a chosen file path; only an empty value takes the placeholder.
                nextValue = string.IsNullOrWhiteSpace(current.Value) ? contractRow.Value : current.Value;
            }
            else if (!syncSettings.PreserveManualValues || string.IsNullOrWhiteSpace(current.Value))
            {
                nextValue = contractRow.Value;
            }
            else
            {
                nextValue = current.Value;
            }

            var nextEnabled = current.Enabled && contractRow.Enabled;
            if (!string.Equals(current.Value, nextValue, StringComparison.Ordinal) || current.Enabled != nextEnabled)
            {
                result[index] = current with { Value = nextValue, Enabled = nextEnabled };
                changed = true;
            }
        }

        WriteManagedFormFields(item, contractNames, identity);

        if (changed || !FormRowsEqual(result, existingRows.ToList()))
        {
            request.SetFormBody(kind == BodyKind.MultipartForm ? "multipart-form" : "form-urlencoded", result);
            MarkBodyManaged(item, identity);
            if (!changed && existingRows.Count > 0)
            {
                preserved.Add("保留既有表單欄位值");
            }
        }
        else if (existingRows.Count > 0)
        {
            preserved.Add("保留既有表單欄位值");
        }
    }

    private void MergeTextBody(
        BrunoItem item,
        BrunoRequestView request,
        RequestBodyInfo contractBody,
        BodyKind kind,
        List<string> changes,
        List<string> preserved,
        OperationIdentity identity)
    {
        var existing = request.TextBodyRaw;
        if (!syncSettings.PreserveManualValues || string.IsNullOrEmpty(existing))
        {
            var canonical = CanonicalBodyFactory.Create(contractBody, SampleValueFactory.Depth.Full);
            if (canonical.Text is null)
            {
                return;
            }

            var changed = !string.Equals(existing, canonical.Text, StringComparison.Ordinal);
            if (changed || existing is null)
            {
                if (kind == BodyKind.Xml)
                {
                    request.SetXmlBody(canonical.Text);
                }
                else
                {
                    request.SetTextBody(canonical.Text);
                }
                MarkBodyManaged(item, identity);
                changes.Add(existing is null ? "新增文字請求主體" : "文字請求主體已由合約重新產生");
            }

            return;
        }

        preserved.Add("保留人工文字請求主體");
    }

    private static bool FormRowsEqual(List<FormRow> a, List<FormRow> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal) ||
                !string.Equals(a[i].Value, b[i].Value, StringComparison.Ordinal) ||
                a[i].IsFile != b[i].IsFile ||
                a[i].Enabled != b[i].Enabled)
            {
                return false;
            }
        }

        return true;
    }

    private HashSet<string> ReadManagedFormFields(BrunoItem item)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (SentinelEntryOrNull(item) is not { } meta ||
            BrunoYaml.SequenceOf(meta, "managedFormFields") is not { } seq)
        {
            return names;
        }

        foreach (var child in seq.Children)
        {
            if (child is YamlDotNet.RepresentationModel.YamlScalarNode { Value: not null } scalar)
            {
                names.Add(scalar.Value);
            }
        }

        return names;
    }

    private void WriteManagedFormFields(BrunoItem item, IEnumerable<string> names, OperationIdentity identity)
    {
        var meta = MetaTarget(item, identity);
        var list = names.Order(StringComparer.Ordinal).ToList();
        if (list.Count == 0)
        {
            BrunoYaml.RemoveKey(meta, "managedFormFields");
            return;
        }

        var seq = new YamlDotNet.RepresentationModel.YamlSequenceNode();
        foreach (var name in list)
        {
            seq.Children.Add(BrunoYaml.Scalar(name));
        }

        BrunoYaml.Set(meta, "managedFormFields", seq);
    }

    private YamlDotNet.RepresentationModel.YamlMappingNode? SentinelEntryOrNull(BrunoItem item) =>
        item.SentinelTagKey is { } key ? store.EntryOrNull(key) : null;

    private bool IsBodyManaged(BrunoItem item) =>
        BrunoYaml.AsBool(BrunoYaml.Get(SentinelEntryOrNull(item) ?? new YamlDotNet.RepresentationModel.YamlMappingNode(), "managedBody")) == true;

    private void ClearBodyManaged(BrunoItem item, OperationIdentity identity) =>
        BrunoYaml.RemoveKey(MetaTarget(item, identity), "managedBody");

    private static string? NormalizeJson(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json)?.ToJsonString();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    // ---- matching helpers ----

    private static BrunoItem? FindTarget(
        Dictionary<string, BrunoItem> markedIndex,
        Dictionary<string, BrunoItem> markedByMethodPath,
        List<(BrunoItem Item, OperationIdentity Identity)> unmarkedCandidates,
        OperationIdentity identity,
        HashSet<BrunoItem> matchedItems,
        List<SyncConflict> conflicts)
    {
        if (markedIndex.TryGetValue(identity.StableKey, out var direct) && !matchedItems.Contains(direct))
        {
            matchedItems.Add(direct);
            return direct;
        }

        // Identity-transition fallback: match a previously-marked item by its STORED
        // method+path. Covers operationId added since last sync (new opid:X vs stored
        // m+p key) AND operationId removed (new m+p key vs stored opid:X).
        var methodPathKey = $"m+p:{identity.Method} {identity.NormalizedPath}";
        if (!string.IsNullOrEmpty(identity.Method) &&
            markedByMethodPath.TryGetValue(methodPathKey, out var byMethodPath) &&
            !matchedItems.Contains(byMethodPath))
        {
            matchedItems.Add(byMethodPath);
            return byMethodPath;
        }

        // Adopt an unmarked (user-created) request that uniquely matches method+path.
        var candidates = unmarkedCandidates
            .Where(c => c.Identity.Method == identity.Method &&
                        string.Equals(c.Identity.NormalizedPath, identity.NormalizedPath, StringComparison.Ordinal) &&
                        !matchedItems.Contains(c.Item))
            .ToList();

        if (candidates.Count == 1)
        {
            var adopted = candidates[0].Item;
            matchedItems.Add(adopted);
            return adopted;
        }

        if (candidates.Count > 1)
        {
            conflicts.Add(new SyncConflict(
                "ambiguous-adoption",
                $"多個未標記請求符合 {identity.Method} {identity.NormalizedPath}；不採用任何一個，改為建立新 Request。"));
        }

        return null;
    }

    private static OperationIdentity? ParseUrlIdentity(YamlDotNet.RepresentationModel.YamlMappingNode? http)
    {
        if (http is null)
        {
            return null;
        }

        var url = BrunoYaml.AsString(BrunoYaml.Get(http, "url"));
        if (url is null)
        {
            return null;
        }

        var method = (BrunoYaml.AsString(BrunoYaml.Get(http, "method")) ?? "GET").ToUpperInvariant();
        var path = ExtractPathTemplate(url);
        if (path is null)
        {
            return null;
        }

        return new OperationIdentity(null, method, path);
    }

    /// <summary>Derives "/users/{id}" from "{{baseUrl}}/users/:id" or "https://host/api/users/:id".</summary>
    internal static string? ExtractPathTemplate(string url)
    {
        var text = url.Trim();
        while (text.StartsWith("{{", StringComparison.Ordinal))
        {
            var end = text.IndexOf("}}", StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            text = text[(end + 2)..].TrimStart('/');
        }

        string path;
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var schemeEnd = text.IndexOf("://", StringComparison.Ordinal) + 3;
            var slash = text.IndexOf('/', schemeEnd);
            path = slash >= 0 ? text[slash..] : "/";
        }
        else
        {
            path = "/" + text.TrimStart('/');
        }

        path = path.Split('?', '#')[0].TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }

        return BrunoPathParamRegex.Replace(path, "$1{$2}");
    }

    private static void RemoveItem(BrunoCollectionDocument collection, BrunoItem target, string stableKey)
    {
        _ = RemoveFromSequence(BrunoYaml.SequenceOf(collection.Root, "items"), target);
        SentinelMetaStoreExtensionsRemove(collection.Root, stableKey);
    }

    private static void SentinelMetaStoreExtensionsRemove(YamlDotNet.RepresentationModel.YamlMappingNode root, string stableKey) =>
        new SentinelMetaStore(root).RemoveEntry(stableKey);

    private static bool RemoveFromSequence(YamlDotNet.RepresentationModel.YamlSequenceNode? seq, BrunoItem target)
    {
        if (seq is null)
        {
            return false;
        }

        for (var i = 0; i < seq.Children.Count; i++)
        {
            if (ReferenceEquals(seq.Children[i], target.Node))
            {
                seq.Children.RemoveAt(i);
                return true;
            }

            if (seq.Children[i] is YamlDotNet.RepresentationModel.YamlMappingNode map &&
                BrunoYaml.SequenceOf(map, "items") is { } nested &&
                RemoveFromSequence(nested, target))
            {
                return true;
            }
        }

        return false;
    }

    private static int NextSeq(BrunoCollectionDocument collection)
    {
        var max = 0;
        foreach (var item in collection.AllRequests())
        {
            if (item.Seq is { } seq && seq > max)
            {
                max = seq;
            }
        }

        return max + 1;
    }

    public static string ResolveBaseUrl(ApiDocument document)
    {
        if (document.Servers.Count == 0 || string.IsNullOrWhiteSpace(document.Servers[0].Url))
        {
            return "{{baseUrl}}";
        }

        var serverUrl = document.Servers[0].Url.Trim();
        if (serverUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return TemplateParamRegex.Replace(serverUrl, match => "{{" + match.Groups[1].Value + "}}");
        }

        // Relative server URL ("/api/v1") → anchored at {{baseUrl}}.
        var relative = "/" + serverUrl.TrimStart('/');
        return "{{baseUrl}}" + TemplateParamRegex.Replace(relative, match => "{{" + match.Groups[1].Value + "}}");
    }

    public static string BuildUrl(string baseUrl, string normalizedPath)
    {
        var suffix = normalizedPath == "/" ? "" : normalizedPath;
        return baseUrl.TrimEnd('/') + TemplateParamRegex.Replace(suffix, match => ":" + match.Groups[1].Value);
    }

    private static string CollectionNameFor(ApiDocument document) =>
        string.IsNullOrWhiteSpace(document.Title) ? "API Collection" : document.Title.Trim();
}

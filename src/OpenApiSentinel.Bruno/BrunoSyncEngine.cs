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

    /// <summary>Runs the sync against an existing collection (may be null for first run).</summary>
    public (BrunoCollectionDocument Collection, BrunoSyncOutcome Outcome) Sync(
        BrunoCollectionDocument? existing,
        ApiDocument document)
    {
        globalSecurity = document.GlobalSecurity;
        globalSecurity = document.GlobalSecurity;
        var warnings = new List<string>();
        var conflicts = new List<SyncConflict>();
        var outcomes = new List<OperationSyncOutcome>();

        var baseUrl = ResolveBaseUrl(document);
        var collection = existing ?? BrunoCollectionDocument.CreateNew(CollectionNameFor(document));

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

                operations.Add((identity, operation, ContractFingerprint.ComputeOperationHash(identity, operation)));
            }
        }

        // ---- Index the collection ----
        var markedIndex = new Dictionary<string, BrunoItem>(StringComparer.Ordinal);
        var unmarkedCandidates = new List<(BrunoItem Item, OperationIdentity Identity)>();
        foreach (var item in collection.AllRequests().Where(i => i.IsHttpRequest).ToList())
        {
            var identity = item.IdentityOf();
            if (identity is not null && !string.IsNullOrEmpty(identity.Method))
            {
                if (!markedIndex.TryAdd(identity.StableKey, item))
                {
                    conflicts.Add(new SyncConflict(
                        "duplicate-identity",
                        $"集合中有多個請求對應同一識別 {identity.StableKey}；僅同步第一個。"));
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
            var item = FindTarget(markedIndex, unmarkedCandidates, identity, matchedItems, conflicts);

            if (item is null)
            {
                outcomes.Add(CreateRequest(collection, identity, operation, contentHash, baseUrl, ref seqCounter));
                continue;
            }

            outcomes.Add(UpdateRequest(item, identity, operation, contentHash, baseUrl));
        }

        // ---- Stale / removed handling ----
        var knownKeys = operations.Select(o => o.Identity.StableKey).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, item) in markedIndex.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (knownKeys.Contains(key))
            {
                continue;
            }

            var identity = item.IdentityOf()!;
            if (syncSettings.RemoveDeletedOperations)
            {
                RemoveItem(collection, item);
                outcomes.Add(new OperationSyncOutcome
                {
                    Action = OperationAction.Removed,
                    Method = identity.Method,
                    Path = identity.NormalizedPath,
                    OperationId = identity.OperationId,
                    RequestName = item.Name,
                });
            }
            else
            {
                BrunoYaml.SetBool(item.Sentinel, "stale", true);
                outcomes.Add(new OperationSyncOutcome
                {
                    Action = OperationAction.Stale,
                    Method = identity.Method,
                    Path = identity.NormalizedPath,
                    OperationId = identity.OperationId,
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
        string baseUrl)
    {
        var request = new BrunoRequestView(item.Node);
        var changes = new List<string>();
        var preserved = new List<string>();
        var itemWarnings = new List<string>();
        var storedHash = item.StoredContentHash();
        var structuralChange = storedHash != contentHash;

        if (!structuralChange)
        {
            RefreshMeta(item, identity, contentHash, RequiresAuth(operation));
            SyncExamples(item, identity, operation, request, baseUrl, changes, preserved);

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

        // ---- params (query + path) ----
        MergeParams(item, request, operation, changes, preserved);

        // ---- headers ----
        MergeHeaderParams(item, request, operation, changes, preserved);

        // ---- body ----
        MergeBody(item, request, operation, changes, preserved, itemWarnings);

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
        SyncExamples(item, identity, operation, request, baseUrl, changes, preserved);

        return new OperationSyncOutcome
        {
            Action = OperationAction.Updated,
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
        item.WriteIdentity(identity, contentHash);
        item.SetGeneratorVersion(toolVersion);
        var meta = item.Sentinel;
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
        List<string> preserved)
    {
        if (generator is null)
        {
            return;
        }

        var baseSnapshot = BuildBaseSnapshot(identity, operation, request, baseUrl);
        if (baseSnapshot is null)
        {
            return;
        }

        var scenarios = generator.Generate(identity, operation, baseSnapshot).ToList();

        // Layer 2 — business scenarios declared in openapi-sentinel.scenarios.yml.
        scenarios.AddRange(BuildBusinessScenarios(identity, baseSnapshot));

        var examplesSeq = request.ExamplesSequence();
        var recorded = item.RecordedExamples();
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

        item.WriteRecordedExamples(registry);
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
                if (!string.Equals(param.Type, "query", StringComparison.Ordinal))
                {
                    BrunoYaml.SetString(row, "type", param.Type);
                }

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
        else
        {
            BrunoYaml.RemoveKey(request, "body");
        }
    }

    /// <summary>
    /// Builds the canonical base snapshot used for scenario generation (成功－完整參數 shape).
    /// </summary>
    private static ScenarioRequest BuildBaseSnapshot(
        OperationIdentity identity,
        Operation operation,
        BrunoRequestView request,
        string baseUrl)
    {
        var url = BuildUrl(baseUrl, identity.NormalizedPath);

        var parameters = new List<(string Name, string Value, string Type, bool Enabled)>();
        foreach (var parameter in operation.Parameters)
        {
            switch (parameter.In)
            {
                case ParameterLocation.Query:
                case ParameterLocation.Path:
                    parameters.Add((parameter.Name, SampleText(parameter),
                        parameter.In == ParameterLocation.Path ? "path" : "query", true));
                    break;
            }
        }

        var headers = new List<(string Name, string Value, bool Enabled)>();
        string? body = null;
        if (operation.RequestBody is { } requestBody &&
            SchemaLooksLikeJson(requestBody) &&
            ScenarioGenerator.PrimaryBodySchema(requestBody) is { } schema)
        {
            headers.Add(("Content-Type", "application/json", true));
            var sample = SampleValueFactory.Sample(schema, SampleValueFactory.Depth.Full, level: 2);
            if (sample is JsonObject)
            {
                body = sample.ToJsonString(JsonWriterOptionsIndented);
            }
            else if (sample is not null)
            {
                body = sample.ToJsonString(JsonWriterOptionsIndented);
            }
        }

        return new ScenarioRequest(identity.Method, url, parameters, headers, body);
    }

    private static readonly JsonSerializerOptions JsonWriterOptionsIndented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static bool SchemaLooksLikeJson(RequestBodyInfo body)
    {
        foreach (var contentType in body.Content.Keys)
        {
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string SampleText(ParameterInfo parameter)
    {
        if (parameter.Schema is null)
        {
            return "sample";
        }

        var value = SampleValueFactory.Sample(parameter.Schema, SampleValueFactory.Depth.Full, level: 2);
        return value switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            null => "sample",
            _ => value.ToJsonString(),
        };
    }

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

        MergeParams(item, request, operation, changes, preserved, creating: true);
        MergeHeaderParams(item, request, operation, changes, preserved, creating: true);

        if (operation.RequestBody is { } requestBody && SchemaLooksLikeJson(requestBody) &&
            ScenarioGenerator.PrimaryBodySchema(requestBody) is { } bodySchema)
        {
            var merged = JsonBodyMerger.Merge(existingJson: null, bodySchema);
            request.SetJsonBody(merged.Json);
            MarkBodyManaged(item);
        }

        var docs = BuildDocs(operation);
        if (docs is not null)
        {
            BrunoYaml.SetString(itemNode, "docs", docs);
        }

        RefreshMeta(item, identity, contentHash, RequiresAuth(operation));
        SyncExamples(item, identity, operation, request, baseUrl, changes, preserved);

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

    private static void MarkBodyManaged(BrunoItem item) =>
        BrunoYaml.SetBool(item.Sentinel, "managedBody", true);

    private static string DefaultRequestName(OperationIdentity identity, Operation operation) =>
        string.IsNullOrWhiteSpace(operation.Summary)
            ? $"{identity.Method} {identity.NormalizedPath}"
            : operation.Summary!.Trim();

    internal static IReadOnlyList<string> FolderSegments(Operation operation)
    {
        var segments = new List<string>();
        foreach (var tag in operation.Tags)
        {
            foreach (var part in tag.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                segments.Add(part);
            }
        }

        return segments;
    }

    private static string? BuildDocs(Operation operation) =>
        string.IsNullOrWhiteSpace(operation.Summary) ? null : operation.Summary!.Trim();

    private void MergeParams(
        BrunoItem item,
        BrunoRequestView request,
        Operation operation,
        List<string> changes,
        List<string> preserved,
        bool creating = false)
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

        WriteManagedParams(item, finalRows.Where(r => contractAllNames.Contains(r.Name)).Select(r => r.Name));

        if (!RowsEqual(finalRows, existingRows))
        {
            request.SetParams(finalRows);
        }
    }

    private static HashSet<string> ReadManagedParams(BrunoItem item)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (item.SentinelOrNull is not { } meta ||
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

    private static void WriteManagedParams(BrunoItem item, IEnumerable<string> names)
    {
        var meta = item.Sentinel;
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

    private static void MergeHeaderParams(
        BrunoItem item,
        BrunoRequestView request,
        Operation operation,
        List<string> changes,
        List<string> preserved,
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
        WriteManagedHeaders(item, managedAfter);

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

    private static HashSet<string> ReadManagedHeaders(BrunoItem item)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (item.SentinelOrNull is not { } meta ||
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

    private static void WriteManagedHeaders(BrunoItem item, HashSet<string> names)
    {
        var meta = item.Sentinel;
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
        List<string> itemWarnings)
    {
        var contractBody = operation.RequestBody;
        var hasJsonContract = contractBody is not null && SchemaLooksLikeJson(contractBody) &&
                              ScenarioGenerator.PrimaryBodySchema(contractBody) is not null;

        if (!hasJsonContract)
        {
            if (request.BodyOrNull is not null)
            {
                if (IsBodyManaged(item))
                {
                    request.RemoveBody();
                    ClearBodyManaged(item);
                    changes.Add("移除請求主體（合約已無 requestBody）");
                }
                else
                {
                    preserved.Add("保留人工請求主體（合約未宣告 requestBody）");
                }
            }

            return;
        }

        var schema = ScenarioGenerator.PrimaryBodySchema(contractBody!)!;
        var mode = request.BodyType;
        if (mode is not null && !string.Equals(mode, "json", StringComparison.Ordinal))
        {
            itemWarnings.Add($"請求主體模式為 '{mode}'，與合約 JSON 內容不一致；已保留使用者內容，未自動改寫。");
            return;
        }

        var existingRaw = request.JsonBodyRaw;
        if (existingRaw is not null && !syncSettings.PreserveManualValues)
        {
            var regenerated = SampleValueFactory.Sample(schema, SampleValueFactory.Depth.Full, level: 2);
            request.SetJsonBody((regenerated as JsonObject ?? new JsonObject()).ToJsonString(JsonWriterOptionsIndented));
            MarkBodyManaged(item);
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
            MarkBodyManaged(item);
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
        }
        else if (existingRaw is not null)
        {
            preserved.Add("保留既有請求主體值");
        }
    }

    private static bool IsBodyManaged(BrunoItem item) =>
        BrunoYaml.AsBool(BrunoYaml.Get(item.SentinelOrNull ?? new YamlDotNet.RepresentationModel.YamlMappingNode(), "managedBody")) == true;

    private static void ClearBodyManaged(BrunoItem item) =>
        BrunoYaml.RemoveKey(item.Sentinel, "managedBody");

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

        // operationId added since last sync: fall back to method+path match against marked items.
        if (identity.OperationId is not null)
        {
            var methodPathKey = $"m+p:{identity.Method} {identity.NormalizedPath}";
            if (markedIndex.TryGetValue(methodPathKey, out var byMethodPath) && !matchedItems.Contains(byMethodPath))
            {
                matchedItems.Add(byMethodPath);
                return byMethodPath;
            }
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

    private static void RemoveItem(BrunoCollectionDocument collection, BrunoItem target)
    {
        _ = RemoveFromSequence(BrunoYaml.SequenceOf(collection.Root, "items"), target);
    }

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

    internal static string ResolveBaseUrl(ApiDocument document)
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

    internal static string BuildUrl(string baseUrl, string normalizedPath)
    {
        var suffix = normalizedPath == "/" ? "" : normalizedPath;
        return baseUrl.TrimEnd('/') + TemplateParamRegex.Replace(suffix, match => ":" + match.Groups[1].Value);
    }

    private static string CollectionNameFor(ApiDocument document) =>
        string.IsNullOrWhiteSpace(document.Title) ? "API Collection" : document.Title.Trim();
}

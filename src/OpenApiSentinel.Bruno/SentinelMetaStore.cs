using OpenApiSentinel.Bruno.Model;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno;

/// <summary>
/// Schema-legal home for ALL Sentinel-managed metadata: the collection-level
/// <c>extensions.x-sentinel.operations</c> map keyed by each request's stable identity key.
/// Request items themselves carry only a single binding tag (<c>x-sentinel:&lt;key&gt;</c> inside
/// the schema-legal <c>info.tags</c> list) — never a metadata block — because the official
/// OpenCollection HttpRequest schema sets <c>additionalProperties: false</c>.
/// </summary>
public sealed class SentinelMetaStore
{
    public const int CurrentVersion = 1;

    private readonly YamlMappingNode root;

    public SentinelMetaStore(YamlMappingNode root)
    {
        this.root = root;
    }

    public static SentinelMetaStore For(BrunoCollectionDocument collection) => new(collection.Root);

    /// <summary>The <c>extensions</c> mapping, creating it (appended last) when absent.</summary>
    public YamlMappingNode ExtensionsMap()
    {
        if (BrunoYaml.MappingOf(root, "extensions") is { } existing)
        {
            return existing;
        }

        if (BrunoYaml.HasKey(root, "extensions"))
        {
            BrunoYaml.RemoveKey(root, "extensions");
        }

        var created = new YamlMappingNode();
        root.Children.Add(BrunoYaml.Scalar("extensions"), created);
        return created;
    }

    /// <summary>The <c>extensions.x-sentinel</c> block, creating it when absent.</summary>
    public YamlMappingNode SentinelBlock()
    {
        var extensions = ExtensionsMap();
        if (BrunoYaml.MappingOf(extensions, "x-sentinel") is { } existing)
        {
            return existing;
        }

        if (BrunoYaml.HasKey(extensions, "x-sentinel"))
        {
            BrunoYaml.RemoveKey(extensions, "x-sentinel");
        }

        var created = new YamlMappingNode();
        extensions.Children.Add(BrunoYaml.Scalar("x-sentinel"), created);
        BrunoYaml.SetInt(created, "version", CurrentVersion);
        return created;
    }

    /// <summary>The operations map keyed by stable identity key, creating it when absent.</summary>
    public YamlMappingNode OperationsMap()
    {
        var block = SentinelBlock();
        if (BrunoYaml.MappingOf(block, "operations") is { } existing)
        {
            return existing;
        }

        if (BrunoYaml.HasKey(block, "operations"))
        {
            BrunoYaml.RemoveKey(block, "operations");
        }

        var created = new YamlMappingNode();
        block.Children.Add(BrunoYaml.Scalar("operations"), created);
        return created;
    }

    public YamlMappingNode? EntryOrNull(string stableKey) =>
        BrunoYaml.MappingOf(OperationsMap(), stableKey);

    public YamlMappingNode GetOrCreateEntry(string stableKey)
    {
        var operations = OperationsMap();
        if (BrunoYaml.MappingOf(operations, stableKey) is { } existing)
        {
            return existing;
        }

        if (BrunoYaml.HasKey(operations, stableKey))
        {
            BrunoYaml.RemoveKey(operations, stableKey);
        }

        var created = new YamlMappingNode();
        operations.Children.Add(BrunoYaml.Scalar(stableKey), created);
        return created;
    }

    public void RemoveEntry(string stableKey) => BrunoYaml.RemoveKey(OperationsMap(), stableKey);

    public List<(string Key, YamlMappingNode Entry)> EntriesWithKeys()
    {
        var result = new List<(string, YamlMappingNode)>();
        var operations = OperationsMap();
        foreach (var child in operations.Children)
        {
            if (child.Key is YamlScalarNode { Value: not null } key && child.Value is YamlMappingNode map)
            {
                result.Add((key.Value, map));
            }
        }

        return result;
    }

    /// <summary>
    /// Moves every unknown/extra key from <paramref name="oldKey"/>'s entry onto
    /// <paramref name="newKey"/>'s entry (without overwriting existing values) and drops the
    /// old entry. Used when an item's identity transitions so NO user-stored metadata is
    /// silently discarded.
    /// </summary>
    public void MergeAndRemoveEntry(string oldKey, string newKey)
    {
        if (string.Equals(oldKey, newKey, StringComparison.Ordinal) || EntryOrNull(oldKey) is not { } old)
        {
            return;
        }

        var target = GetOrCreateEntry(newKey);
        foreach (var field in old.Children)
        {
            if (field.Key is YamlScalarNode { Value: not null } fieldName && !BrunoYaml.HasKey(target, fieldName.Value))
            {
                target.Children.Add(BrunoYaml.DeepClone(field.Key), BrunoYaml.DeepClone(field.Value));
            }
        }

        RemoveEntry(oldKey);
    }

    /// <summary>
    /// One-time migration: moves LEGACY request-level <c>x-sentinel:</c> blocks (illegal under
    /// the official schema's additionalProperties:false) into the collection-level index,
    /// binds the item tag, and strips the illegal block. Unknown extra keys inside the legacy
    /// block are carried over verbatim — never silently dropped.
    /// </summary>
    public List<string> MigrateLegacyItemBlocks(BrunoCollectionDocument collection)
    {
        var notes = new List<string>();
        foreach (var item in collection.AllRequests())
        {
            if (BrunoYaml.MappingOf(item.Node, "x-sentinel") is not { } legacy)
            {
                continue;
            }

            var identity = LegacyIdentityOf(legacy);
            var key = identity?.StableKey ?? $"legacy:{ContractFingerprint.Sha256Hex(BrunoYaml.ToYaml(legacy))[..24]}";

            if (EntryOrNull(key) is not { } target)
            {
                target = GetOrCreateEntry(key);
                foreach (var field in legacy.Children)
                {
                    target.Children.Add(BrunoYaml.DeepClone(field.Key), BrunoYaml.DeepClone(field.Value));
                }
            }
            else
            {
                // Index already knows this operation (newer format wins); carry over only the
                // extra legacy fields so nothing the user stored disappears.
                MergeAndPreserveExtras(key, legacy);
            }

            item.BindSentinelTag(key);
            BrunoYaml.RemoveKey(item.Node, "x-sentinel");
            notes.Add($"已將舊版 x-sentinel 區塊遷移至集合層 extensions（{key}）。");
        }

        return notes;
    }

    private void MergeAndPreserveExtras(string key, YamlMappingNode legacy)
    {
        var target = GetOrCreateEntry(key);
        foreach (var field in legacy.Children)
        {
            if (field.Key is YamlScalarNode { Value: not null } fieldName && !BrunoYaml.HasKey(target, fieldName.Value))
            {
                target.Children.Add(BrunoYaml.DeepClone(field.Key), BrunoYaml.DeepClone(field.Value));
            }
        }
    }

    private static OperationIdentity? LegacyIdentityOf(YamlMappingNode legacy)
    {
        var opIdRaw = BrunoYaml.AsString(BrunoYaml.Get(legacy, SentinelMetaKeys.OperationId));
        var opId = string.IsNullOrWhiteSpace(opIdRaw) ? null : opIdRaw!.Trim();
        var method = BrunoYaml.AsString(BrunoYaml.Get(legacy, SentinelMetaKeys.Method));
        var path = BrunoYaml.AsString(BrunoYaml.Get(legacy, SentinelMetaKeys.Path));
        var identityKey = BrunoYaml.AsString(BrunoYaml.Get(legacy, SentinelMetaKeys.IdentityKey));

        if (!string.IsNullOrWhiteSpace(method) && !string.IsNullOrWhiteSpace(path))
        {
            return new OperationIdentity(opId, method!.ToUpperInvariant(), OperationIdentity.NormalizePath(path!));
        }

        if (!string.IsNullOrWhiteSpace(identityKey))
        {
            return identityKey!.StartsWith("opid:", StringComparison.Ordinal)
                ? new OperationIdentity(identityKey[5..], "", "")
                : FromMethodPlusKey(identityKey!, opId);
        }

        return null;
    }

    private static OperationIdentity? FromMethodPlusKey(string identityKey, string? opId)
    {
        if (!identityKey.StartsWith("m+p:", StringComparison.Ordinal))
        {
            return null;
        }

        var body = identityKey[4..];
        var space = body.IndexOf(' ');
        return space > 0
            ? new OperationIdentity(opId, body[..space].ToUpperInvariant(), OperationIdentity.NormalizePath(body[(space + 1)..]))
            : null;
    }
}

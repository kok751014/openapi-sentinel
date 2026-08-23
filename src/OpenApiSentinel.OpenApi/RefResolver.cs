using System.Text.Json.Nodes;

namespace OpenApiSentinel.OpenApi;

/// <summary>
/// Resolves <c>$ref</c> occurrences inside a parsed document:
/// - internal pointers (#/...)
/// - relative local file references (bounded to the root document directory)
/// - remote http(s) references (only when explicitly enabled)
/// Cyclic references are replaced with deterministic marker nodes instead of recursing forever,
/// and every loaded sub-document gets its own identity so nested fragment refs stay correct.
/// </summary>
public sealed class RefResolver
{
    private const string CyclicMarkerKey = "x-sentinel-cyclic-ref";

    /// <summary>Marks a subtree left un-materialized because a safety budget tripped
    /// (see Materialize). Comparison treats identical markers as equal and differing
    /// ones as a single Verify-level event instead of failing the whole run.</summary>
    public const string UnresolvedMarkerKey = "x-sentinel-unresolved-ref";

    private readonly ProcessingLimits _limits;
    private readonly RemoteFetchPolicy _remotePolicy;
    private readonly SpecLoader _loader;
    private long _clonedNodes;
    private int _documentCounter;

    public int ResolvedRefCount { get; private set; }

    public RefResolver(ProcessingLimits limits, RemoteFetchPolicy remotePolicy)
    {
        _limits = limits;
        _remotePolicy = remotePolicy;
        _loader = new SpecLoader(remotePolicy);
    }

    internal static JsonObject UnresolvedMarker(string reference) => new()
    {
        [UnresolvedMarkerKey] = reference,
    };

    public static bool IsUnresolvedMarker(JsonNode? node, out string? reference)
    {
        reference = null;
        if (node is not JsonObject obj ||
            !obj.TryGetPropertyValue(UnresolvedMarkerKey, out var v) ||
            v is not JsonValue val ||
            !val.TryGetValue<string>(out var s))
            return false;
        reference = s;
        return true;
    }

    /// <summary>Resolves all references in-place within the given root node.</summary>
    public void Resolve(JsonNode root, string? baseDirectory)
    {
        var ctx = new ResolutionContext();
        var mainDoc = new DocumentInstance(++_documentCounter, root, baseDirectory);
        ctx.Register(mainDoc);
        ResolveWithin(root, mainDoc, ctx, new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed class ResolutionContext
    {
        public Dictionary<string, DocumentInstance> RemoteCache { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Register(DocumentInstance doc) => RemoteCache[doc.CacheKey ?? $"__main_{doc.Id}"] = doc;
    }

    private sealed record DocumentInstance(int Id, JsonNode Root, string? BaseDirectory, string? CacheKey = null);

    private void ResolveWithin(JsonNode node, DocumentInstance doc, ResolutionContext ctx, HashSet<string> activeChain)
    {
        switch (node)
        {
            case JsonObject obj when obj.TryGetPropertyValue("$ref", out var refValue) &&
                                     refValue is JsonValue rv &&
                                     rv.TryGetValue<string>(out var reference) &&
                                     !string.IsNullOrWhiteSpace(reference):
                var replacement = ResolveReference(obj, reference, doc, ctx, activeChain);
                if (replacement is not null && !ReferenceEquals(replacement, obj))
                {
                    obj.ReplaceWith(replacement);
                    ResolveWithin(replacement, doc, ctx, activeChain);
                }
                break;

            case JsonObject obj:
                foreach (var prop in obj.ToList())
                    if (prop.Value is not null) ResolveWithin(prop.Value, doc, ctx, activeChain);
                break;

            case JsonArray arr:
                foreach (var item in arr.ToList())
                    if (item is not null) ResolveWithin(item, doc, ctx, activeChain);
                break;
        }
    }

    /// <summary>
    /// Builds a resolved replacement for the reference held by <paramref name="holder"/>.
    /// Returns null for cyclic references (holder becomes a cyclic marker by caller convention).
    /// </summary>
    private JsonNode? ResolveReference(
        JsonObject holder, string reference, DocumentInstance currentDoc, ResolutionContext ctx,
        HashSet<string> activeChain)
    {
        if (ResolvedRefCount >= _limits.MaxResolvedRefs)
        {
            // Budget tripped: degrade to an opaque marker instead of failing the run.
            // Identical markers compare equal; differing ones surface as one Verify finding.
            return UnresolvedMarker(reference);
        }

        var (targetDoc, targetNode, fragment) = LocateTarget(reference, currentDoc, ctx);

        // Chain identity must include the document so identical fragments in different documents do not collide.
        var chainId = $"{targetDoc.Id}#{fragment}";
        if (!activeChain.Add(chainId))
            return null; // cyclic

        try
        {
            ResolvedRefCount++;
            var materialized = Materialize(targetNode, targetDoc, ctx, activeChain, depth: 0);

            // Apply $ref siblings per JSON Schema 2020-12 semantics (they override resolved values).
            if (materialized is JsonObject matObj)
            {
                foreach (var (key, value) in holder)
                {
                    if (key == "$ref") continue;
                    matObj[key] = value?.DeepClone();
                }
            }
            return materialized;
        }
        finally
        {
            activeChain.Remove(chainId);
        }
    }

    private (DocumentInstance Doc, JsonNode Node, string Fragment) LocateTarget(
        string reference, DocumentInstance currentDoc, ResolutionContext ctx)
    {
        var hashIndex = reference.IndexOf('#');
        var docPart = hashIndex < 0 ? reference : reference[..hashIndex];
        var fragment = hashIndex < 0 ? "" : reference[(hashIndex + 1)..];

        DocumentInstance targetDoc;

        if (string.IsNullOrEmpty(docPart))
        {
            targetDoc = currentDoc;
        }
        else if (docPart.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 docPart.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!_remotePolicy.Enabled)
                throw new SpecParseException(
                    $"Refusing to resolve remote reference '{reference}': remote references are disabled. " +
                    "Enable 'remoteReferences.enabled' in configuration or pass --allow-remote.");
            var uri = new Uri(docPart, UriKind.Absolute);
            _loader.ValidateRemoteUri(uri);
            targetDoc = GetOrLoadRemote(uri.ToString(), uri.ToString(), null, ctx);
        }
        else if (docPart.Contains("://"))
        {
            throw new SpecParseException(
                $"Refusing to resolve reference '{reference}': unsupported or unsafe URI scheme " +
                "(file:// and other non-http schemes are blocked).");
        }
        else if (docPart.StartsWith("http%3A", StringComparison.OrdinalIgnoreCase) ||
                 docPart.StartsWith("https%3A", StringComparison.OrdinalIgnoreCase))
        {
            var decoded = Uri.UnescapeDataString(docPart);
            if (!_remotePolicy.Enabled)
                throw new SpecParseException(
                    $"Refusing to resolve remote reference '{reference}': remote references are disabled.");
            _loader.ValidateRemoteUri(new Uri(decoded, UriKind.Absolute));
            targetDoc = GetOrLoadRemote(decoded, decoded, null, ctx);
        }
        else
        {
            // Relative local file reference, resolved against the *containing* document's directory.
            if (currentDoc.BaseDirectory is null)
                throw new SpecParseException(
                    $"Cannot resolve external file reference '{reference}': the containing document was not loaded from disk " +
                    "(stdin/URL/git sources have no base directory).");
            if (_remotePolicy.Enabled == false && LooksLikeAbsoluteUnixPath(docPart) && !docPart.StartsWith(currentDoc.BaseDirectory))
            {
                // fallthrough: bounds check below rejects it anyway
            }

            var combined = Path.GetFullPath(Path.Combine(currentDoc.BaseDirectory, docPart));
            var normalizedBase = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDoc.BaseDirectory)) +
                                 Path.DirectorySeparatorChar;
            if (!combined.StartsWith(normalizedBase, StringComparison.Ordinal))
                throw new SpecParseException(
                    $"Refusing to resolve reference '{reference}': path escapes the base directory of the specification. " +
                    "External file references must stay within the document's directory tree.");

            var key = combined.ToLowerInvariant();
            targetDoc = ctx.RemoteCache.TryGetValue(key, out var cached)
                ? cached
                : LoadDocumentFromFile(combined, key, ctx);
        }

        // Walk the JSON pointer fragment.
        var node = targetDoc.Root;
        foreach (var segment in ParseFragment(fragment))
        {
            node = segment switch
            {
                _ when node is JsonObject o && o.TryGetPropertyValue(segment, out var next) && next is not null => next!,
                _ when node is JsonArray a && int.TryParse(segment, out var i) && i >= 0 && i < a.Count &&
                    a[i] is not null => a[i]!,
                _ => throw new SpecParseException(
                    $"Unresolvable reference '{reference}': segment '{segment}' not found."),
            };
        }

        return (targetDoc, node, fragment);
    }

    private static bool LooksLikeAbsoluteUnixPath(string p) => p.StartsWith('/') || p.Contains("..");

    private DocumentInstance LoadDocumentFromFile(string fullPath, string cacheKey, ResolutionContext ctx)
    {
        var loader = new SpecLoader(RemoteFetchPolicy.Disabled());
        var loaded = loader.LoadAsync(fullPath).GetAwaiter().GetResult();
        var root = DocumentParser.Parse(loaded.SourceName, loaded.Content, _limits);
        var doc = new DocumentInstance(++_documentCounter, root,
            Path.GetDirectoryName(Path.GetFullPath(fullPath)), cacheKey);
        ctx.RemoteCache[cacheKey] = doc;
        return doc;
    }

    private DocumentInstance GetOrLoadRemote(string cacheKey, string url, string? _, ResolutionContext ctx)
    {
        if (ctx.RemoteCache.TryGetValue(cacheKey.ToLowerInvariant(), out var cached)) return cached;
        var loaded = _loader.LoadAsync(url).GetAwaiter().GetResult();
        var root = DocumentParser.Parse(url, loaded.Content, _limits);
        var doc = new DocumentInstance(++_documentCounter, root, null, cacheKey.ToLowerInvariant());
        ctx.RemoteCache[cacheKey.ToLowerInvariant()] = doc;
        return doc;
    }

    private JsonNode Materialize(
        JsonNode node, DocumentInstance doc, ResolutionContext ctx, HashSet<string> activeChain,
        int depth)
    {
        if (depth > _limits.MaxRefChainDepth)
            throw new SpecParseException($"Schema nesting deeper than {_limits.MaxRefChainDepth} levels during reference resolution.");
        if (++_clonedNodes > _limits.MaxNodes)
        {
            // Node budget exhausted: leave an opaque marker rather than aborting. Real
            // mega-specs (e.g. Stripe) legitimately exceed fixed budgets; adversarial
            // bombs stay bounded by the parser's raw-node cap and produce noisy-but-
            // bounded Verify findings instead of silent success.
            return UnresolvedMarker("(node-budget)");
        }

        switch (node)
        {
            case JsonObject srcObj when srcObj.TryGetPropertyValue("$ref", out var innerRefValue) &&
                                        innerRefValue is JsonValue iv &&
                                        iv.TryGetValue<string>(out var innerRef) &&
                                        !string.IsNullOrWhiteSpace(innerRef):
            {
                var replacement = ResolveReference(srcObj, innerRef, doc, ctx, activeChain)
                                  ?? new JsonObject { [CyclicMarkerKey] = innerRef };
                return replacement;
            }
            case JsonObject srcObj:
            {
                var copy = new JsonObject();
                foreach (var (key, value) in srcObj)
                {
                    copy[key] = value is null
                        ? null
                        : Materialize(value, doc, ctx, activeChain, depth + 1);
                }
                return copy;
            }
            case JsonArray srcArr:
            {
                var copy = new JsonArray();
                foreach (var item in srcArr)
                {
                    copy.Add(item is null ? null : Materialize(item, doc, ctx, activeChain, depth + 1));
                }
                return copy;
            }
            default:
                return node.DeepClone();
        }
    }

    /// <summary>Splits a JSON-pointer fragment into decoded segments.</summary>
    private static List<string> ParseFragment(string fragment)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(fragment)) return result;
        if (fragment.StartsWith('/')) fragment = fragment[1..];
        foreach (var raw in fragment.Split('/'))
        {
            result.Add(Uri.UnescapeDataString(raw.Replace("~1", "/").Replace("~0", "~")));
        }
        return result;
    }
}

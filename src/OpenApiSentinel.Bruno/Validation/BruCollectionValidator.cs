using System.Text.Json;

namespace OpenApiSentinel.Bruno.Validation;

/// <summary>One structural validation failure of the native .bru collection.</summary>
public sealed record BruValidationError(string Source, string Detail)
{
    public string Format() => $"{Source}: {Detail}";
}

/// <summary>
/// Deterministic structural validation of the Bruno-native (.bru) collection, based on the
/// official bruno.json manifest schema (packages/bruno-electron collections.js) and the
/// official bru grammar (packages/bruno-lang/v2). Every file we generate is re-parsed with
/// our strict parser; a file that cannot round-trip through the official block structure
/// fails the gate BEFORE anything is written.
/// </summary>
public static class BruCollectionValidator
{
    private static readonly HashSet<string> KnownMethodBlocks = new(StringComparer.Ordinal)
    { "get", "post", "put", "delete", "patch", "options", "head", "connect", "trace" };

    public static (bool Valid, IReadOnlyList<BruValidationError> Errors) Validate(
        string workspaceDir,
        string manifestJson,
        IReadOnlyList<(string RelativePath, string Content)> requestFiles)
    {
        var errors = new List<BruValidationError>();

        // ---- manifest: official Yup contract (name ≤256 non-empty, type == collection) ----
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("type", out var type) || type.GetString() != "collection")
            {
                errors.Add(new("bruno.json", "manifest 'type' must be \"collection\"."));
            }

            if (!doc.RootElement.TryGetProperty("name", out var name) ||
                name.GetString() is not { Length: > 0 } nameValue)
            {
                errors.Add(new("bruno.json", "manifest 'name' must be a non-empty string."));
            }
            else if (nameValue.Length > 256)
            {
                errors.Add(new("bruno.json", "manifest 'name' must be 256 characters or less."));
            }

            _ = doc.RootElement.TryGetProperty("version", out _);
        }
        catch (JsonException ex)
        {
            errors.Add(new("bruno.json", $"manifest is not valid JSON: {ex.Message}"));
        }

        // ---- every emitted request/folder file re-parses and carries required fields ----
        foreach (var (relativePath, content) in requestFiles)
        {
            try
            {
                var file = BruMarkup.Parse(content, relativePath);

                var meta = file.First("meta");
                if (meta is null)
                {
                    errors.Add(new(relativePath, "missing meta block."));
                    continue;
                }

                var metaName = meta.Pairs.FirstOrDefault(p => p.RawKey == "name");
                if (string.IsNullOrWhiteSpace(metaName?.Value))
                {
                    errors.Add(new(relativePath, "meta.name must be a non-empty string."));
                }

                var methodBlock = file.Blocks.FirstOrDefault(b => KnownMethodBlocks.Contains(b.Name));
                if (methodBlock is null && meta.Pairs.Any(p => p.RawKey == "type" && p.Value != "folder"))
                {
                    errors.Add(new(relativePath, "request file has no HTTP method block."));
                    continue;
                }

                if (methodBlock is not null &&
                    !methodBlock.Pairs.Any(p => p.RawKey == "url"))
                {
                    errors.Add(new(relativePath, $"{methodBlock.Name} block requires a url."));
                }
            }
            catch (BrunoFormatException ex)
            {
                errors.Add(new(relativePath, ex.Message));
            }
        }

        return (errors.Count == 0, errors);
    }
}

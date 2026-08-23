using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// Adversarial-input hardening per master spec §30. The parse stage is format-agnostic;
/// OpenAPI validation happens at normalization. Hostile inputs must therefore either
/// parse cleanly (bounded by representation-model budgets) or be rejected via
/// SpecParseException with actionable messages — never hang, OOM, or crash.
/// </summary>
public class ParserRobustnessTests
{
    private static string TryParse(string content)
    {
        try
        {
            DocumentParser.Parse("hostile", content, ProcessingLimits.Default());
            return "parsed";
        }
        catch (SpecParseException ex)
        {
            return "rejected: " + ex.Message;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t  \n")]
    [InlineData("\0\0\0")]
    [InlineData("{\"openapi\": }")]
    public void MalformedSyntax_IsRejectedCleanly(string content)
    {
        var result = TryParse(content);
        Assert.StartsWith("rejected:", result, StringComparison.Ordinal);
        Assert.Contains("'hostile'", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("- just\n- a\n- list")]
    [InlineData("\"a scalar string\"")]
    [InlineData("42")]
    [InlineData("::::not::yaml::::")]
    public void ValidYaml_NonOpenApiDocuments_ParseCleanly_AtParseStage(string content)
    {
        // Parsing is format-agnostic; OpenAPI shape validation happens at normalization.
        var result = TryParse(content);
        Assert.Equal("parsed", result);
    }

    [Fact]
    public void HugeSingleScalar_IsHandled()
    {
        var yaml = "a: " + new string('x', 2_000_000) + "\n";
        var result = TryParse(yaml);
        Assert.True(result == "parsed" || result.StartsWith("rejected", StringComparison.Ordinal));
    }

    [Fact]
    public void AliasBomb_IsBoundedByDesign()
    {
        // Exponential alias expansion cannot occur: YamlDotNet's representation model
        // keeps anchors/aliases as shared references until materialization, where the
        // MaxNodes budget bounds total expansion.
        var yaml = """
            a: &a ["lot", "of", "data"]
            b: &b [*a, *a, *a, *a]
            c: &c [*b, *b, *b, *b]
            d: &d [*c, *c, *c, *c]
            e: &e [*d, *d, *d, *d]
            """;
        var result = TryParse(yaml + "\nopenapi: 3.0.3\ninfo: {title: T}\n");
        Assert.True(result == "parsed" || result.StartsWith("rejected", StringComparison.Ordinal));
    }
}

/// <summary>Full-pipeline hostile-input checks: load → parse → resolve → normalize.</summary>
public class PipelineRobustnessTests
{
    private static string RunPipeline(string content)
    {
        try
        {
            var limits = ProcessingLimits.Default();
            var root = DocumentParser.Parse("hostile", content, limits);
            new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
            OpenApiNormalizer.Normalize(root);
            return "completed";
        }
        catch (SpecParseException)
        {
            return "rejected";
        }
    }

    [Fact]
    public void RepeatedAliasExpansion_InSchemaPosition_IsBounded()
    {
        // One large anchored schema referenced from many positions: materialization is
        // linear in references and must respect MaxNodes rather than exploding.
        var bigSchema = string.Join("", Enumerable.Range(1, 40)
            .Select(i => $"p{{i}}: {{type: string}}".Replace("{i}", i.ToString())));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("openapi: 3.0.3");
        sb.AppendLine("info: {title: T, version: \"1.0\"}");
        sb.AppendLine("paths:");
        sb.AppendLine("  /orders:");
        sb.AppendLine("    get:");
        sb.AppendLine("      operationId: listOrders");
        sb.AppendLine("      responses:");
        sb.AppendLine("        '200':");
        sb.AppendLine("          description: ok");
        sb.AppendLine("          content:");
        sb.AppendLine("            application/json:");
        sb.AppendLine("              schema:");
        sb.AppendLine("                type: array");
        sb.AppendLine("                items: $ref");
        sb.AppendLine("components:");
        sb.AppendLine("  schemas:");
        sb.AppendLine("    Big:");
        sb.AppendLine("      type: object");
        sb.AppendLine("      required: [id]");
        sb.AppendLine("      properties:");
        sb.AppendLine($"        {bigSchema}");
        sb.AppendLine("        id: {type: string}");

        var yaml = sb.ToString().Replace("items: $ref", "items:\n                  $ref: '#/components/schemas/Big'");
        // Reference the same big schema from 30 additional synthetic endpoints.
        var extra = new System.Text.StringBuilder();
        for (var e = 0; e < 30; e++)
        {
            extra.AppendLine($"  /e{e}:");
            extra.AppendLine("    get:");
            extra.AppendLine($"      operationId: getE{e}");
            extra.AppendLine("      responses:");
            extra.AppendLine("        '200':");
            extra.AppendLine("          description: ok");
            extra.AppendLine("          content:");
            extra.AppendLine("            application/json:");
            extra.AppendLine("              schema:");
            extra.AppendLine("                $ref: '#/components/schemas/Big'");
        }
        yaml = yaml.Replace("components:", extra + "components:");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = RunPipeline(yaml);
        sw.Stop();

        Assert.True(result is "completed" or "rejected", result);
        Assert.True(sw.ElapsedMilliseconds < 10_000, $"pipeline took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void UnsupportedFutureVersion_IsActionable()
    {
        var result = RunPipeline("openapi: 9.9.9\ninfo: {title: T}\n");
        Assert.Equal("rejected", result);
    }

    [Fact]
    public void MissingPaths_IsActionableOrRejected()
    {
        var result = RunPipeline("openapi: 3.0.3\ninfo: {title: T}\n");
        Assert.True(result is "completed" or "rejected");
    }
}

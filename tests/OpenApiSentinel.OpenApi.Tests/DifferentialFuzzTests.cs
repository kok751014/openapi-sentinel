using System.Text;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// Seeded differential fuzzing (engineering report improvement #3): generate
/// pseudo-random but fully reproducible specification pairs and assert the two core
/// engine invariants hold across structure space no hand-written fixture reaches:
///   1. self-compare yields ZERO changes (false-positive invariant);
///   2. comparing A vs B is deterministic — two runs serialize identically;
///   3. no input crashes the pipeline (robustness).
/// Fixed seeds keep CI stable; raise SeedCount locally for deeper sweeps.
/// </summary>
public class DifferentialFuzzTests
{
    private const int SeedCount = 40;

    private static readonly string[] Types = ["string", "integer", "number", "boolean"];
    private static readonly string[] Formats = ["", "uuid", "int32", "date-time"];
    private static readonly string[] ParamLocations = ["query", "header"];
    private static readonly string[] Methods = ["get", "post"];

    /// <summary>Builds a small valid spec from the seed; same seed ⇒ byte-identical YAML.</summary>
    internal static string Generate(int seed)
    {
        var rng = new Random(seed);
        var sb = new StringBuilder();
        var is31 = rng.Next(3) == 0; // some seeds exercise 3.1 (webhooks, type arrays)
        sb.Append(is31 ? "openapi: 3.1.0\n" : "openapi: 3.0.3\n");
        sb.Append("""
            info:
              title: Fuzz
              version: "1.0"
            paths:
            """);

        var pathCount = rng.Next(1, 4);
        for (var p = 0; p < pathCount; p++)
        {
            sb.Append($"\n  /p{p}{(rng.Next(4) == 0 ? $"/{{id{rng.Next(2)}}}" : "")}:");
            var method = Methods[rng.Next(Methods.Length)];
            sb.Append($"\n    {method}:");
            if (rng.Next(2) == 0) sb.Append($"\n      operationId: op{p}");
            if (rng.Next(4) == 0)
            {
                // Exercise effective-security diffing (operation-level requirement).
                sb.Append("\n      security:");
                sb.Append("\n        - bearerAuth: []");
            }
            sb.Append("\n      responses:");
            var code = rng.Next(2) == 0 ? 200 : 204;
            sb.Append($"\n        '{code}':");
            sb.Append("\n          description: ok");
            if (code == 200 && rng.Next(2) == 0)
            {
                sb.Append("\n          content:");
                sb.Append("\n            application/json:");
                AppendSchema(sb, rng, 14, depth: 0);
            }
            if (rng.Next(3) == 0)
            {
                // Exercise response-header diffing.
                sb.Append("\n          headers:");
                sb.Append($"\n            X-Trace-{p}:");
                sb.Append("\n              schema:");
                AppendSchema(sb, rng, 16, depth: 1);
            }
            if (method == "post" && rng.Next(3) == 0)
            {
                sb.Append("\n      requestBody:");
                sb.Append("\n        content:");
                sb.Append("\n          application/json:");
                AppendSchema(sb, rng, 12, depth: 0);
            }
            if (method == "post" && rng.Next(3) == 0)
            {
                // Exercise the newest diff surface: operation-level callbacks.
                sb.Append($"\n      callbacks:");
                sb.Append($"\n        event{p}:");
                sb.Append("\n          '{$request.body#/callbackUrl}':");
                sb.Append("\n            post:");
                sb.Append("\n              responses:");
                sb.Append("\n                '200':");
                sb.Append("\n                  description: received");
                if (rng.Next(2) == 0)
                {
                    sb.Append("\n              requestBody:");
                    sb.Append("\n                content:");
                    sb.Append("\n                  application/json:");
                    AppendSchema(sb, rng, 18, depth: 0);
                }
            }

            var paramCount = rng.Next(0, 3);
            if (paramCount > 0)
            {
                sb.Append("\n      parameters:");
                for (var q = 0; q < paramCount; q++)
                {
                    var loc = ParamLocations[rng.Next(ParamLocations.Length)];
                    sb.Append($"\n        - name: q{q}");
                    sb.Append($"\n          in: {loc}");
                    sb.Append("\n          schema:");
                    AppendSchema(sb, rng, 10, depth: 0);
                }
            }
        }

        if (is31 && rng.Next(2) == 0)
        {
            // Exercise webhook diffing on 3.1 seeds.
            sb.Append("\nwebhooks:");
            sb.Append("\n  hookA:");
            sb.Append("\n    post:");
            sb.Append("\n      responses:");
            sb.Append("\n        '200':");
            sb.Append("\n          description: ok");
        }

        sb.Append("\ncomponents:");
        sb.Append("\n  securitySchemes:");
        sb.Append("\n    bearerAuth:");
        sb.Append("\n      type: http");
        sb.Append("\n      scheme: bearer");

        return sb.ToString();
    }

    private static void AppendSchema(StringBuilder sb, Random rng, int indent, int depth)
    {
        var pad = new string(' ', indent);
        var kind = rng.Next(depth < 2 ? 6 : 4); // cap nesting
        switch (kind)
        {
            case 0: // plain typed scalar with optional constraints
            {
                var type = Types[rng.Next(Types.Length)];
                sb.Append($"\n{pad}type: {type}");
                if (rng.Next(2) == 0 && Formats[rng.Next(Formats.Length)] is { Length: > 0 } f)
                    sb.Append($"\n{pad}format: {f}");
                if (type == "string" && rng.Next(3) == 0)
                    sb.Append($"\n{pad}maxLength: {rng.Next(1, 64)}");
                if (type != "string" && rng.Next(3) == 0)
                    sb.Append($"\n{pad}minimum: {rng.Next(0, 10)}");
                break;
            }
            case 1: // enum
                sb.Append($"\n{pad}type: string");
                sb.Append($"\n{pad}enum: [a{(rng.Next(2) == 0 ? ", b" : "")}]");
                break;
            case 2: // object with properties
                sb.Append($"\n{pad}type: object");
                sb.Append($"\n{pad}properties:");
                var propCount = rng.Next(1, 3);
                for (var i = 0; i < propCount; i++)
                {
                    sb.Append($"\n{pad}  k{i}:");
                    AppendSchema(sb, rng, indent + 4, depth + 1);
                }
                break;
            case 3: // array
                sb.Append($"\n{pad}type: array");
                sb.Append($"\n{pad}items:");
                AppendSchema(sb, rng, indent + 2, depth + 1);
                break;
            case 4: // nullable flip material
                sb.Append($"\n{pad}type: string");
                sb.Append($"\n{pad}nullable: true");
                break;
            default: // additionalProperties toggle
                sb.Append($"\n{pad}type: object");
                sb.Append($"\n{pad}additionalProperties: {(rng.Next(2) == 0 ? "true" : "false")}");
                break;
        }
    }

    public static IReadOnlyList<SemanticChange> CompareDocs(string oldYaml, string newYaml)
    {
        ApiDocument Normalize(string yaml)
        {
            var limits = ProcessingLimits.Default();
            var root = DocumentParser.Parse("fuzz", yaml, limits);
            new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
            return OpenApiNormalizer.Normalize(root);
        }

        return new ComparisonEngine().Compare(Normalize(oldYaml), Normalize(newYaml));
    }

    public static IEnumerable<object[]> Seeds()
    {
        for (var s = 1; s <= SeedCount; s++) yield return [s];
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void SelfCompare_Yields_Zero_Changes(int seed)
    {
        var doc = Generate(seed);
        Assert.Empty(CompareDocs(doc, doc));
    }

    [Fact]
    public void Mutated_Pairs_Are_Deterministic_And_Never_Crash()
    {
        for (var seed = 1; seed <= SeedCount; seed++)
        {
            // Rotate through several deterministic single-token mutation operators so
            // the differential invariants are exercised across different rule families.
            var oldSpec = Generate(seed);
            var newSpec = (seed % 4) switch
            {
                0 when oldSpec.Contains("maxLength") => ReplaceFirst(oldSpec, "maxLength:", "minLength:"),
                1 when oldSpec.Contains("enum: [a") => ReplaceFirst(oldSpec, "enum: [a", "enum: [z"),
                2 when oldSpec.Contains("bearerAuth: []") => ReplaceFirst(oldSpec, "        - bearerAuth: []\n", ""),
                _ => oldSpec.Replace("description: ok", "description: ok #"),
            };

            var first = CompareDocs(oldSpec, newSpec);
            var second = CompareDocs(oldSpec, newSpec);

            var a = System.Text.Json.JsonSerializer.Serialize(
                first.Select(c => new { c.Kind, c.Path, c.Location, c.OldValue, c.NewValue }));
            var b = System.Text.Json.JsonSerializer.Serialize(
                second.Select(c => new { c.Kind, c.Path, c.Location, c.OldValue, c.NewValue }));
            Assert.True(a == b, $"seed {seed}: engine output was not deterministic");
        }
    }

    private static string ReplaceFirst(string s, string find, string replacement)
    {
        var idx = s.IndexOf(find, StringComparison.Ordinal);
        return idx < 0 ? s : s[..idx] + replacement + s[(idx + find.Length)..];
    }
}

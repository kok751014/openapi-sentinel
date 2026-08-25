using System.Diagnostics;
using OpenApiSentinel.Bruno;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;
using OpenApiSentinel.Reporting;
using OpenApiSentinel.Rules;

namespace OpenApiSentinel.Bench;

/// <summary>
/// Honest stopwatch-based benchmarks on generated contracts (small/medium/large).
/// Measures parse, normalize, compare, rules and report phases plus allocated bytes.
/// Results land in docs/benchmarks.md — keep that file in sync with actual runs.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        foreach (var size in new[] { SpecSize.Small, SpecSize.Medium, SpecSize.Large })
        {
            Bench(size, args.Contains("--save"));
        }

        return 0;
    }

    private enum SpecSize { Small, Medium, Large }

    private static void Bench(SpecSize size, bool save)
    {
        var (oldYaml, newYaml) = Generate(size);
        var label = size.ToString();
        Console.WriteLine($"═══ {label}: old {oldYaml.Length / 1024.0:F0} KB, new {newYaml.Length / 1024.0:F0} KB ═══");

        var limits = ProcessingLimits.Default();

        // Warmup + measured runs; report the median of N runs.
        const int runs = 5;
        var parseMs = new List<double>();
        var normalizeMs = new List<double>();
        var compareMs = new List<double>();
        var rulesMs = new List<double>();
        var reportMs = new List<double>();
        var brunoFullMs = new List<double>();
        var brunoResyncMs = new List<double>();

        ApiDocument? oldDoc = null, newDoc = null;
        IReadOnlyList<SemanticChange>? changes = null;
        EngineEvaluation? evaluation = null;

        for (var run = 0; run < runs; run++)
        {
            var beforeAlloc = GC.GetTotalAllocatedBytes(precise: true);

            var sw = Stopwatch.StartNew();
            var oldRoot = DocumentParser.Parse("old.yaml", oldYaml, limits);
            var newRoot = DocumentParser.Parse("new.yaml", newYaml, limits);
            sw.Stop();
            parseMs.Add(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            var resolver = new RefResolver(limits, RemoteFetchPolicy.Disabled());
            resolver.Resolve(oldRoot, null);
            resolver.Resolve(newRoot, null);
            oldDoc = OpenApiNormalizer.Normalize(oldRoot);
            newDoc = OpenApiNormalizer.Normalize(newRoot);
            sw.Stop();
            normalizeMs.Add(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            changes = new ComparisonEngine().Compare(oldDoc, newDoc);
            sw.Stop();
            compareMs.Add(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            evaluation = new RulesEngine(RuleRegistry.CreateDefault()).Evaluate(changes, new EngineOptions
            {
                OldContractName = "old.yaml",
                NewContractName = "new.yaml",
            });
            sw.Stop();
            rulesMs.Add(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            _ = new HtmlReporter().Render(evaluation.Result, ReporterOptions.Default);
            _ = new JsonReporter().Render(evaluation.Result, ReporterOptions.Default);
            sw.Stop();
            reportMs.Add(sw.Elapsed.TotalMilliseconds);

            // Bruno sync: full creation (sync + serialize) vs incremental unchanged skip.
            var syncEngine = new BrunoSyncEngine(
                new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = false }, "bench");
            sw.Restart();
            var (collection, _) = syncEngine.Sync(null, newDoc!);
            _ = collection.ToYaml();
            sw.Stop();
            brunoFullMs.Add(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            _ = syncEngine.Sync(collection, newDoc!);
            sw.Stop();
            brunoResyncMs.Add(sw.Elapsed.TotalMilliseconds);

            if (run == runs - 1)
            {
                var allocated = GC.GetTotalAllocatedBytes(precise: true) - beforeAlloc;
                Console.WriteLine($"  allocations ≈ {allocated / 1024.0 / 1024.0:F1} MB");
            }
        }

        double Median(List<double> values) => values.OrderBy(v => v).ElementAt(values.Count / 2);

        Console.WriteLine($"  parse      median {Median(parseMs):F1} ms");
        Console.WriteLine($"  resolve+normalize median {Median(normalizeMs):F1} ms");
        Console.WriteLine($"  compare    median {Median(compareMs):F1} ms   ({changes!.Count} changes)");
        Console.WriteLine($"  rules      median {Median(rulesMs):F1} ms   ({evaluation!.Result.Findings.Count} findings)");
        Console.WriteLine($"  reports    median {Median(reportMs):F1} ms   (html+json)");
        Console.WriteLine($"  bruno full median {Median(brunoFullMs):F1} ms   (create + serialize)");
        Console.WriteLine($"  bruno skip median {Median(brunoResyncMs):F1} ms   (unchanged resync)");

        if (save)
        {
            var line = $"| {label} | {Median(parseMs):F1} | {Median(normalizeMs):F1} | {Median(compareMs):F1} | {Median(rulesMs):F1} | {Median(reportMs):F1} | {Median(brunoFullMs):F1} | {Median(brunoResyncMs):F1} | {changes.Count} | {evaluation.Result.Findings.Count} |";
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "bench-results.md"), line + Environment.NewLine);
        }
    }

    private static (string OldYaml, string NewYaml) Generate(SpecSize size)
    {
        var pathCount = size switch
        {
            SpecSize.Small => 10,
            SpecSize.Medium => 120,
            SpecSize.Large => 600,
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };
        var schemaPerPath = size switch
        {
            SpecSize.Small => 2,
            SpecSize.Medium => 4,
            SpecSize.Large => 6,
            _ => 6,
        };
        _ = schemaPerPath; // schemas are generated 1:1 with paths

        static string PathsSection(int paths, int version)
        {
            var w = new System.Text.StringBuilder();
            for (var p = 0; p < paths; p++)
            {
                w.AppendLine($"/resource{p}:");
                w.AppendLine("  get:");
                w.AppendLine($"    operationId: listResource{p}");
                w.AppendLine("    parameters:");
                w.AppendLine("      - name: page");
                w.AppendLine("        in: query");
                w.AppendLine("        schema: {type: integer, minimum: 1}");
                w.AppendLine("      - name: filter");
                w.AppendLine("        in: query");
                w.AppendLine("        required: false");
                w.AppendLine("        schema: {type: string, enum: [all, active, archived]}");
                w.AppendLine("    responses:");
                w.AppendLine("      '200':");
                w.AppendLine("        description: ok");
                w.AppendLine("        content:");
                w.AppendLine("          application/json:");
                w.AppendLine("            schema:");
                w.AppendLine("              type: array");
                w.AppendLine("              items:");
                w.AppendLine($"                $ref: '#/components/schemas/Model{p}'");

                if (version == 2 && p % 9 == 0)
                {
                    w.AppendLine($"/resource{p}/{{id}}:");
                    w.AppendLine("  get:");
                    w.AppendLine($"    operationId: getResource{p}");
                    w.AppendLine("    parameters:");
                    w.AppendLine("      - name: id");
                    w.AppendLine("        in: path");
                    w.AppendLine("        required: true");
                    w.AppendLine("        schema: {type: integer}");
                    w.AppendLine("    responses:");
                    w.AppendLine("      '200':");
                    w.AppendLine("        description: ok");
                }
            }
            // Indent every line two spaces so entries live *under* the `paths:` key.
            var lines = w.ToString().Replace("\r\n", "\n").Split('\n');
            return string.Join("\n", lines.Select(l => l.Length > 0 ? "  " + l : l));
        }

        static string Components(int count, int version)
        {
            var w = new System.Text.StringBuilder();
            w.AppendLine("components:");
            w.AppendLine("  securitySchemes:");
            w.AppendLine("    bearerAuth:");
            w.AppendLine("      type: http");
            w.AppendLine("      scheme: bearer");
            w.AppendLine("  schemas:");
            for (var i = 0; i < count; i++)
            {
                var idType = version == 2 && i % 7 == 0 ? "integer" : "string";
                var maxLen = version == 2 && i % 5 == 0 ? 50 : 100;
                var minScore = version == 2 && i % 11 == 0 ? 5 : 0;
                w.AppendLine($"    Model{i}:");
                w.AppendLine("      type: object");
                w.AppendLine($"      required: [id{i}, name]");
                w.AppendLine("      properties:");
                w.AppendLine($"        id{i}:");
                w.AppendLine($"          type: {idType}");
                w.AppendLine("        name:");
                w.AppendLine("          type: string");
                w.AppendLine($"          maxLength: {maxLen}");
                w.AppendLine($"        score{i}:");
                w.AppendLine("          type: number");
                w.AppendLine($"          minimum: {minScore}");
            }
            return w.ToString();
        }

        var oldYaml = "openapi: 3.0.3\n" +
                      "info:\n  title: Bench API\n  version: \"1.0\"\n" +
                      "paths:\n" + PathsSection(pathCount, 1) +
                      Components(pathCount, 1);

        var newYaml = "openapi: 3.0.3\n" +
                      "info:\n  title: Bench API\n  version: \"2.0\"\n" +
                      "paths:\n" + PathsSection(pathCount, 2) +
                      Components(pathCount, 2);

        return (oldYaml, newYaml);
    }

    private static class CultureInfo
    {
        public static System.Globalization.CultureInfo InvariantCulture => System.Globalization.CultureInfo.InvariantCulture;
    }
}

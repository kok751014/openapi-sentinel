using System.Text;
using OpenApiSentinel.Bruno.Model;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>
/// Seeded differential fuzzing for the Bruno sync engine, mirroring the philosophy of
/// OpenApi.Tests/DifferentialFuzzTests: randomized contract shapes must uphold the
/// engine's core invariants —
///   I1  syncing the same document twice leaves the second run entirely Unchanged and
///       byte-stable on disk;
///   I2  any evolution produces a crash-free, DETERMINISTIC result (two independent
///       runs from an identical starting point serialize identically);
///   I3  user content inside x-sentinel survives every evolution.
/// Committed seed count keeps CI cost sub-second; raise locally for deeper sweeps.
/// </summary>
public class BrunoSyncFuzzTests
{
    internal const int SeedCount = 40;

    private static readonly string[] Methods = ["get", "post", "put", "delete"];
    private static readonly string[] ParamLocations = ["query", "header"];
    private static readonly string[] ScalarTypes = ["string", "integer"];

    internal static string GenerateSpec(int seed)
    {
        var rng = new Random(seed);
        var sb = new StringBuilder();
        sb.Append("openapi: 3.0.3\n");
        sb.Append("info:\n  title: Fuzz ").Append(seed).Append("\n  version: \"1\"\n");
        if (rng.Next(2) == 0)
        {
            sb.Append("servers:\n  - url: https://api").Append(seed % 97).Append(".example.com/v\n");
        }

        sb.Append("paths:\n");
        var opCount = 1 + rng.Next(6);
        // Distinct paths only — YAML mapping keys must be unique; multiple methods per
        // path would require grouped emission which the identity matrix already covers.
        var usedPaths = new HashSet<int>();
        for (var i = 0; i < opCount; i++)
        {
            int pathIndex;
            do
            {
                pathIndex = rng.Next(8);
            }
            while (!usedPaths.Add(pathIndex));

            var path = $"/r{pathIndex}";
            var method = Methods[rng.Next(Methods.Length)];
            sb.Append("  ").Append(path).Append(":\n");
            sb.Append("    ").Append(method).Append(":\n");

            // operationId present most of the time; absence exercises m+p identity.
            if (rng.Next(5) != 0)
            {
                sb.Append("      operationId: op").Append(i).Append('\n');
            }

            if (rng.Next(2) == 0)
            {
                sb.Append("      tags: [G").Append(rng.Next(3)).Append("]\n");
            }

            var paramCount = rng.Next(3);
            if (paramCount > 0)
            {
                sb.Append("      parameters:\n");
                for (var p = 0; p < paramCount; p++)
                {
                    sb.Append("        - name: p").Append(p).Append('\n')
                      .Append("          in: ").Append(ParamLocations[rng.Next(ParamLocations.Length)]).Append('\n')
                      .Append("          schema:\n")
                      .Append("            type: ").Append(ScalarTypes[rng.Next(ScalarTypes.Length)]).Append('\n');
                    if (rng.Next(3) == 0)
                    {
                        sb.Append("            minimum: ").Append(rng.Next(5)).Append('\n')
                          .Append("            maximum: ").Append(5 + rng.Next(90)).Append('\n');
                    }
                }
            }

            if (method is "post" or "put")
            {
                sb.Append("      requestBody:\n        required: true\n        content:\n")
                  .Append("          application/json:\n            schema:\n")
                  .Append("              type: object\n              properties:\n");
                var propCount = 1 + rng.Next(4);
                for (var pr = 0; pr < propCount; pr++)
                {
                    sb.Append("                f").Append(pr).Append(":\n");
                    switch (rng.Next(4))
                    {
                        case 0:
                            sb.Append("                  type: string\n");
                            if (rng.Next(2) == 0)
                            {
                                sb.Append("                  enum: [A, B]\n");
                            }

                            break;
                        case 1:
                            sb.Append("                  type: integer\n")
                              .Append("                  minimum: ").Append(rng.Next(3)).Append('\n');
                            break;
                        case 2:
                            sb.Append("                  type: array\n")
                              .Append("                  items:\n")
                              .Append("                    type: string\n");
                            break;
                        default:
                            sb.Append("                  type: boolean\n");
                            break;
                    }
                }
            }

            sb.Append("      responses:\n        '200':\n          description: OK\n");
        }

        return sb.ToString();
    }

    [Fact]
    public void Forty_Random_Contracts_Uphold_Sync_Invariants()
    {
        var totalSeeds = Environment.GetEnvironmentVariable("SENTINEL_BRUNO_FUZZ_SEEDS") is { } env ? int.Parse(env) : SeedCount;
        for (var seed = 1; seed <= totalSeeds; seed++)
        {
            var docA = TestSupport.NormalizeSpec(GenerateSpec(seed));
            var docB = TestSupport.NormalizeSpec(GenerateSpec(seed + SeedCount));
            var engine = new BrunoSyncEngine(
                new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = false }, $"fuzz-{seed}");

            // I1 — first sync adds everything; identical re-sync is unchanged + byte-stable.
            var (collection, first) = engine.Sync(null, docA);
            Assert.True(first.AddedCount == first.Operations.Count,
                $"seed {seed}: first sync not all-added ({first.AddedCount}/{first.Operations.Count})");

            var yamlAfterFirst = collection.ToYaml();
            var (_, second) = engine.Sync(collection, docA);
            Assert.All(second.Operations, o => Assert.Equal(OperationAction.Unchanged, o.Action));
            Assert.Equal(yamlAfterFirst, collection.ToYaml());

            // I2+I3 — evolving to B is crash-free, deterministic across independent runs,
            // and preserves user content planted inside the meta index on PRE-EXISTING
            // requests (newly added requests legitimately have none).
            var yamlBeforeEvolution = collection.ToYaml();

            static (BrunoCollectionDocument Clone, HashSet<BrunoItem> Annotated) PrepareClone(string yaml)
            {
                var clone = BrunoCollectionDocument.Parse(yaml, "clone.yml");
                var store = SentinelMetaStore.For(clone);
                var annotated = new HashSet<BrunoItem>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
                foreach (var item in clone.AllRequests())
                {
                    if (item.SentinelTagKey is not { } key)
                    {
                        continue;
                    }

                    BrunoYaml.SetString(store.GetOrCreateEntry(key), "userNote", "keep-me");
                    annotated.Add(item);
                }

                return (clone, annotated);
            }

            var (clone1, annotated1) = PrepareClone(yamlBeforeEvolution);
            var (clone2, _) = PrepareClone(yamlBeforeEvolution);
            var yamlRun1 = engine.Sync(clone1, docB).Collection.ToYaml();
            var yamlRun2 = engine.Sync(clone2, docB).Collection.ToYaml();
            Assert.Equal(yamlRun1, yamlRun2);

            foreach (var item in clone1.AllRequests())
            {
                if (!annotated1.Contains(item))
                {
                    continue;
                }

                var entry = item.SentinelEntry(clone1);
                Assert.NotNull(entry);
                Assert.Equal("keep-me", BrunoYaml.AsString(BrunoYaml.Get(entry!, "userNote")));
            }
        }
    }
}

# Benchmarks

Honest, reproducible stopwatch benchmarks on generated contracts.
Run them yourself:

```bash
dotnet run -c Release --project bench/OpenApiSentinel.Bench
```

- Median of 5 runs, Release build, .NET 10.0.400, Linux x64 (CI container class hardware may differ).
- "changes" counts raw semantic changes detected between the two generated contracts;
  "findings" is what survives rule evaluation into the report.
- The generated evolution includes type changes, constraint tightening and added endpoints,
  so the diff is realistic rather than an empty no-op comparison.

## Results

```
═══ Small: old 8 KB, new 8 KB ═══
  allocations ≈ 1.6 MB
  parse      median 7.2 ms
  resolve+normalize median 0.5 ms
  compare    median 1.3 ms   (11 changes)
  rules      median 0.3 ms   (11 findings)
  reports    median 0.5 ms   (html+json)
═══ Medium: old 90 KB, new 94 KB ═══
  allocations ≈ 16.9 MB
  parse      median 76.9 ms
  resolve+normalize median 5.6 ms
  compare    median 20.1 ms   (113 changes)
  rules      median 0.8 ms   (113 findings)
  reports    median 1.4 ms   (html+json)
═══ Large: old 455 KB, new 470 KB ═══
  allocations ≈ 82.5 MB
  parse      median 173.2 ms
  resolve+normalize median 39.8 ms
  compare    median 74.9 ms   (555 changes)
  rules      median 3.2 ms   (555 findings)
  reports    median 10.7 ms   (html+json)
```

## Interpretation

- Parsing dominates end-to-end time; YAML→JSON conversion is the main cost.
- Comparison scales linearly with operation count on this workload.
- Even the large contract (~450 KB, 600+ operations) completes the full pipeline in well under a second,
  which keeps CI usage effectively free.

These are not BenchmarkDotNet microbenchmarks; treat absolute numbers as indicative and rerun on your hardware.

## Real-world spot checks

Measured on the same machine as the synthetic benches above (defaults, no config tweaks):

| Spec pair | Size | Wall time | Peak RSS | Outcome |
|-----------|------|----------:|---------:|---------|
| GitHub REST API (validate) | ~10 MB JSON, 809 paths / 1221 ops | 4.9 s | — | OK |
| GitHub REST API (self-compare) | 2 × ~10 MB fetched remotely | 13.6 s | — | passed, 0 findings |
| Stripe spec3.yaml (validate) | ~6 MB YAML, 416 paths / 589 ops | 3.8 s | — | OK |
| **Stripe v2300 → v2404 diff** (~100 weekly releases) | 2 × ~6 MB YAML | **4.3 s** | **0.68 GB** | 174 findings (73 additive properties, 59 response enum additions, 1 unresolved-reference Verify), MINOR recommendation |
| DigitalOcean public v2 (validate) | multi-file tree, ~2851 YAML files, 445 paths / 659 ops | 1.5 s | 0.13 GB | OK, 0 unresolved refs |
| DigitalOcean public v2 (self-compare) | same tree resolved twice from disk | 3.4 s total | — | passed, 0 findings |

The Stripe pair exercises the safety-budget degradation path: exactly one `CG5022` Verify
finding marks the single subtree that could not be resolved within default budgets;
everything else is fully classified. Absolute numbers are indicative — rerun on your hardware.

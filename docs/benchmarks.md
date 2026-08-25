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
  bruno full median 4.3 ms   (create + serialize)
  bruno skip median 1.1 ms   (unchanged resync)
═══ Medium: old 90 KB, new 94 KB ═══
  allocations ≈ 23.1 MB
  parse      median 74.2 ms
  resolve+normalize median 6.2 ms
  compare    median 24.0 ms   (113 changes)
  rules      median 1.0 ms   (113 findings)
  reports    median 2.1 ms   (html+json)
  bruno full median 38.8 ms   (create + serialize)
  bruno skip median 6.6 ms   (unchanged resync)
═══ Large: old 455 KB, new 470 KB ═══
  allocations ≈ 110.5 MB
  parse      median 329.6 ms
  resolve+normalize median 32.3 ms
  compare    median 97.3 ms   (555 changes)
  rules      median 3.2 ms   (555 findings)
  reports    median 8.2 ms   (html+json)
  bruno full median 46.0 ms   (create + serialize)
  bruno skip median 34.4 ms   (unchanged resync)
```

The `bruno` rows measure the sync engine directly against the already-normalized NEW
contract: "full" = create the collection from scratch plus YAML serialization; "skip" =
re-syncing an unchanged contract WITHOUT the state-file fast path (the engine still walks
every operation and refreshes metadata). The real CLI with a present state file skips the
engine entirely on unchanged contracts, so end-to-end no-op runs are faster than these
numbers.

## Interpretation

- Parsing dominates end-to-end time; YAML→JSON conversion is the main cost.
- Comparison scales linearly with operation count on this workload.
- Even the large contract (~450 KB, 600+ operations) completes the full pipeline in well under a second,
  which keeps CI usage effectively free.
- Bruno sync is comfortably interactive at every size: full creation of a large collection
  costs ~46 ms, and unchanged re-syncs stay in tens of milliseconds even without the
  state fast path — Visual Studio build hooks are effectively free.

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
| Datadog API v2 (validate) | ~7.7 MB generated YAML, oneOf-heavy, 966 paths / 1523 ops | 2.8 s | 0.34 GB | OK |
| Datadog API v2 (self-compare) | same file twice from disk | 6.1 s total | — | passed, 0 findings |
| Kubernetes API swagger.json (validate) | ~4.3 MB JSON (OpenAPI 2.0), 602 paths / 1202 ops / 862 defs | 1.9 s | 0.36 GB | OK |
| **Kubernetes v1.30.0 → v1.31.0 diff** | 2 × ~3.3 MB JSON (OpenAPI 2.0) | **7.0 s** | — | 3710 findings (1478 additive properties, 1740 array-item notes, 166 response property removals, 51 endpoint removals + additions), MAJOR recommendation |

The Stripe pair exercises the safety-budget degradation path: exactly one `CG5022` Verify
finding marks the single subtree that could not be resolved within default budgets;
everything else is fully classified. Absolute numbers are indicative — rerun on your hardware.

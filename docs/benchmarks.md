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
  parse      median 6.6 ms
  resolve+normalize median 0.8 ms
  compare    median 1.3 ms   (11 changes)
  rules      median 0.3 ms   (11 findings)
  reports    median 0.4 ms   (html+json)
═══ Medium: old 90 KB, new 94 KB ═══
  allocations ≈ 16.7 MB
  parse      median 67.3 ms
  resolve+normalize median 4.2 ms
  compare    median 18.3 ms   (113 changes)
  rules      median 0.6 ms   (113 findings)
  reports    median 1.4 ms   (html+json)
═══ Large: old 455 KB, new 470 KB ═══
  allocations ≈ 82.8 MB
  parse      median 179.0 ms
  resolve+normalize median 42.2 ms
  compare    median 73.2 ms   (555 changes)
  rules      median 2.7 ms   (555 findings)
  reports    median 21.2 ms   (html+json)
```

## Interpretation

- Parsing dominates end-to-end time; YAML→JSON conversion is the main cost.
- Comparison scales linearly with operation count on this workload.
- Even the large contract (~450 KB, 600+ operations) completes the full pipeline in well under a second,
  which keeps CI usage effectively free.

These are not BenchmarkDotNet microbenchmarks; treat absolute numbers as indicative and rerun on your hardware.

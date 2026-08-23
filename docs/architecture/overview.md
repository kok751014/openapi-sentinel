# Architecture

## Pipeline

```
Load (file | URL | stdin | git:<rev>:<path>)
  ↓  SpecLoader (+ RemoteFetchPolicy, EnvHeader auth)
Parse (JSON via System.Text.Json · YAML via YamlDotNet representation model → JsonNode)
  ↓  DocumentParser — depth/node budgets, alias-bomb pre-scan
Resolve $ref (internal · relative-file subtree-bounded · remote policy-gated; cycles → markers)
  ↓  RefResolver
Normalize → ApiDocument (single semantic model, version-aware: Swagger2.0 / 3.0 / 3.1 / 3.2)
  ↓  OpenApiNormalizer
Compare → SemanticChange stream (70+ stable kinds)
  ↓  ComparisonEngine + SchemaComparer (direction-aware recursion)
Evaluate rules → Findings
  ↓  RulesEngine + RuleRegistry (73 built-in CGxxxx rules; kind→rule mapping)
Configure pipeline (overrides · ignore globs · suppressions · baseline fingerprints · policy)
  ↓
ComparisonResult (versioned output contract)
  ↓
Policy evaluation (FailurePolicy → exit code 1)
  ↓
Reporters: Console · JSON · Markdown(+PR summary) · HTML · SARIF · JUnit · Changelog
```

## Project boundaries

| Project | Responsibility | Depends on |
|---------|----------------|------------|
| `Core` | Domain types (Finding, SemanticChange, ComparisonResult, Policy), GlobMatcher. No I/O. | — |
| `OpenApi` | Loading, parsing, $ref resolution, normalization, comparison. | Core |
| `Rules` | Rule definitions/registry, engine configuration pipeline. | Core, OpenApi |
| `Reporting` | Renderers consuming **only** `ComparisonResult`. | Core |
| `Cli` | Arg parsing, config loading, orchestration, exit codes. Thin `Program.cs`. | all |

Extension points kept open by design:

- **New rule** = one `RuleDefinition` with descriptor metadata + a pure change→finding function,
  registered in a family's `All()`; docs regenerate from the registry.
- **New report format** = one `IResultReporter` implementation.
- **Custom/pack rules** = feed another registry into `RulesEngine` (no comparer changes).

## Key invariants

1. **Determinism**: identical inputs + config + tool version ⇒ byte-identical reports.
   Sorted iterations everywhere; injectable clock (`ISentinelClock`) for golden tests.
2. **Reporters are pure**: they never mutate or recompute compatibility decisions.
3. **Safety bounds**: every untrusted-input path is budgeted (bytes, nodes, depth, refs,
   redirect hops) — see [security](../security.md).
4. **Direction-awareness lives in rules**, not in a post-hoc classifier, so each rule can
   encode domain nuance (see [compatibility model](../compatibility-model.md)).

## Notable mechanisms

- **Cyclic `$ref`s**: replaced by `x-sentinel-cyclic-ref` marker objects during resolution;
  comparison normalizes them so equal cycles compare equal.
- **Fingerprints**: SHA-256 over identifying fields only (prose excluded) → baselines survive
  wording changes.
- **Engine meta-findings** (`CG900x`) surface process problems (expired suppressions, stale
  baselines, unevaluated change kinds) instead of hiding them.

See [DECISIONS.md](../DECISIONS.md) for the reasoning behind each choice.

# AGENTS.md

## Project

`openapi-sentinel` — catch breaking API contract changes before they reach production.
A .NET 10 CLI + rule engine that compares two OpenAPI contracts (2.0–3.2, JSON/YAML),
classifies changes direction-aware, reports in console/JSON/Markdown/HTML/SARIF/JUnit,
and enforces policy thresholds in CI.

## Stack

- C# / .NET 10 (`net10.0`), nullable enabled, `TreatWarningsAsErrors=true`,
  analyzers `latest-recommended`. Solution: `OpenApiSentinel.slnx`.
- Projects: src/{Core,OpenApi,Rules,Reporting,Bruno,Cli}; tests/{Core,OpenApi,Rules,Reporting,Bruno,Cli}.Tests, IntegrationTests; bench/OpenApiSentinel.Bench.
- Only runtime dependency: YamlDotNet (MIT). Test stack: xunit 2.9.3 + Microsoft.NET.Test.Sdk 17.14.1
  (do NOT migrate test projects to xunit v3 / MSTest.Sdk 18 — breaks on .NET 10 SDK's `dotnet test`).

## Verified commands (keep current)

Every shell needs:
```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
```

```bash
dotnet build -c Release                 # 0 errors 0 warnings expected
dotnet test -c Release                  # all projects; ~458 tests green
dotnet test -c Release tests/OpenApiSentinel.Rules.Tests          # single project
dotnet test -c Release --filter "FullyQualifiedName~GlobMatcher"  # single test class

# Run the CLI without installing:
dotnet src/OpenApiSentinel.Cli/bin/Release/net10.0/OpenApiSentinel.Cli.dll compare samples/basic/v1.yaml samples/basic/v2-breaking.yaml --fail-on high --no-color   # expect exit 1
dotnet ... compare samples/basic/v1.yaml samples/basic/v2-safe.yaml --fail-on high --no-color      # expect exit 0
dotnet ... bruno sync  --config samples/bruno-sync/openapi-sentinel.yml --no-color                 # expect exit 0 (creates collection under samples/bruno-sync/api-contract — use a scratch copy)

# Benchmarks (small/medium/large generated specs):
dotnet run -c Release --project bench/OpenApiSentinel.Bench

# Regenerate the rule catalog doc after adding/changing rules:
python3 build/gen-rules-doc.py
```

## Workflow

- Direct commits to `main` are fine during bootstrap; no PR gate yet.

## Keep this file current

- When commands change (packaging, new projects, test layout), update the block above.

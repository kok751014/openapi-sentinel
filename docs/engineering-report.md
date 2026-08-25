# OpenAPI Sentinel — Engineering Report

Snapshot date: **2026-08-24** · Tool version: **1.0.0** · Git: `main`

Consolidated status per MASTER_PROMPT §60. Historical evidence lives in `progress.md`;
this page states the verified current state only.

## Completed

- **Engine**: direction-aware comparison of Swagger 2.0 / OpenAPI 3.0 / 3.1 / 3.2 (JSON & YAML),
  semantic normalization, `$ref` resolution (internal / sibling files / remote-with-policy),
  cycle markers, bounded processing with graceful degradation to `CG5022` Verify events.
- **Multi-file specifications**: relative external references resolve against the main
  document's directory tree; URL-loaded documents resolve siblings against their own URL
  under the full fetch policy; opt-in `allowAboveDocumentTree` for Azure-style shared trees;
  missing/unfetchable references degrade instead of aborting.
- **85 rules + 3 engine meta-checks** (`CGxxxx`), stable IDs, human explanations,
  impact and remediation text; aspect-aware discriminator classification; direction-aware
  formats, numeric types/unions, nullability, requiredness, additionalProperties,
  readOnly/writeOnly; callback diffing (CG1011–13); response-header payload diffing;
  OAuth flow add/remove; Swagger 2.0 `collectionFormat` wire-format detection;
  per-media-type content parameters (CG2017/18).
- **False-positive controls**: documentation-only drift ignored everywhere including
  container-level gates (`items.changed`, composition members), wildcard status-code masking,
  rename heuristics with confidence, prose-insensitive composition comparison.
- **Reporting**: console (+verbose), JSON (documented stable contract), Markdown,
  PR summary, changelog, single-file interactive HTML (offline), SARIF 2.1.0, JUnit XML.
- **Policy & governance**: severity thresholds incl. Verify semantics, rule overrides,
  path ignores, suppressions with expiry warnings, baseline adoption workflow.
- **CLI**: compare / validate / rules / explain / init / version / help; subcommand help;
  documented exit codes 0/1/2/3/4/5; config file with actionable validation.
- **Security**: YAML bomb & alias caps, node/depth/ref budgets with graceful degradation,
  SSRF guards on URL + remote refs + every sibling fetch, path-traversal rejection,
  HTML/markdown/terminal output-injection hardening, env-var header auth (never logged).
- **Delivery**: .NET global tool, 6 self-contained RID archives with checksums,
  Dockerfile, GitHub composite action (+ step summaries & annotations), CI examples for
  GitHub Actions / Azure DevOps / GitLab / generic shell+PowerShell, automated release
  pipeline on tags.

## Architecture

```
Load (file | stdin | http(s) | git-rev) → Parse → Resolve $refs → Normalize
    → Compare (semantic change stream) → Rules engine (kind→rule routing + guards)
    → Policy evaluation → Reporters (console/json/markdown/html/sarif/junit/changelog/pr-summary)
```

Projects: `Core` (model/policy/glob) · `OpenApi` (parse/resolve/normalize/compare) ·
`Rules` (catalog + engine) · `Reporting` (7 formats) · `Bruno` (collection sync +
scenario generator) · `Cli` (workflows/config).
Only runtime dependency: YamlDotNet (MIT). Fully offline core; no telemetry.

Bruno integration (2026-08-25): `openapi-sentinel bruno sync|examples` syncs contracts
into single-file OpenCollection YAML collections with preservation-first incremental
merging, zh-TW auto scenarios, business-scenario file support, HTML/JSON sync reports,
and an incremental hash-state fast path. See DECISIONS 32 and docs/bruno-sync.md.

See [architecture/overview.md](architecture/overview.md), [DECISIONS.md](DECISIONS.md)
(32 entries), [compatibility-model.md](compatibility-model.md).

## Commands

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

dotnet build -c Release                                   # build (0 warnings expected)
dotnet test -c Release                                    # full test suite
dotnet run --project src/OpenApiSentinel.Cli -- --version # run without installing

# Compare samples (exit codes drive CI):
dotnet src/OpenApiSentinel.Cli/bin/Release/net10.0/OpenApiSentinel.Cli.dll \
  compare samples/basic/v1.yaml samples/basic/v2-breaking.yaml --fail-on high   # exit 1

openapi-sentinel compare old.yaml new.yaml --format html --html report.html
openapi-sentinel compare old.yaml new.yaml --format json  --json api-diff.json
openapi-sentinel compare old.yaml new.yaml --markdown     api-diff.md --pr-summary pr.md

bash build/release.sh            # multi-RID release artifacts into dist/<version>/
python3 build/gen-rules-doc.py   # regenerate docs/rules.md from the registry
```

## Test Results

`dotnet test -c Release` → **359 passed / 0 failed** (2026-08-24):

| Project | Tests |
|---------|------:|
| Core | 21 |
| Reporting (incl. golden snapshots, injection hardening) | 48 |
| OpenApi (pipeline, robustness, callbacks, degradation, multi-file & remote siblings, seeded differential fuzzing) | 171 |
| Rules (engine, FP/FN suites, coverage & catalog guards) | 91 |
| Cli (runner, config loader, workflows) | 50 |
| Integration (end-to-end scenarios) | 3 |

Remote CI: green across ubuntu-latest / windows-latest / macos-latest.
Seeded differential fuzzing: self-compare false-positive invariant + determinism invariant,
500-seed deep sweeps clean (single-file and multi-file grammars).
Real-world validation (7 ecosystems, every major spec style):

| Vendor / pair | Outcome |
|---------------|---------|
| GitHub REST API (~10 MB) | validate OK; remote self-compare 0 findings |
| Stripe v2300 → v2404 (~6 MB ×2) | 174 findings, MINOR — stable baseline across refactors |
| DigitalOcean public v2 (multi-file tree, ~2851 files) | validate 1.5 s, 0 unresolved; self-compare clean |
| Kubernetes swagger.json + v1.30→v1.31 diff (OpenAPI 2.0) | validate OK; 3,710 substantive findings after prose-noise fix |
| Twilio 2.7.0 → 2.7.1 (generated oneOf-heavy) | exactly the 3 real endpoint removals, MAJOR |
| Datadog API v2 (~7.7 MB generated) + evolution diff | validate OK; 1,280 findings, MAJOR |
| Azure Compute virtualMachineScaleSet 2023-07-01 → 2024-07-01 | 289 findings, MAJOR (with `allowAboveDocumentTree`) |

## Product Demo

Fastest buyer demo (<2 minutes):

```bash
git clone https://github.com/kok751014/openapi-sentinel && cd openapi-sentinel
dotnet run --project src/OpenApiSentinel.Cli -- \
  compare samples/basic/v1.yaml samples/basic/v2-breaking.yaml \
  --fail-on high --html report.html
start report.html        # interactive offline report: filters, search, verdicts
```

Then open `docs/examples/api-diff.html` for a pre-rendered example, or
`docs/examples/callbacks-api-diff.md` for callback-aware findings.

## Release Artifacts

- Source archives & installers: `dist/1.0.0/` (regenerate via `bash build/release.sh`)
  — 6 RID packages + `SHA256SUMS` (verified OK ×6) + `RELEASE_NOTES.md`
- NuGet global tool: `dist/1.0.0/pkg/OpenApiSentinel.1.0.0.nupkg`
  (also `artifacts/packages/` from `dotnet pack`)
- Published tag: `v0.9.0` on GitHub Releases; future releases publish automatically on
  `v*` tags via `.github/workflows/ci.yml`

## Remaining Risks

1. **Publication pending credentials**: NuGet.org push needs the `NUGET_API_KEY` secret;
   Docker image push needs registry credentials. Everything in-repo is ready and gated.
2. **Budget degradation granularity**: extremely reference-heavy specs may produce
   CG5022 Verify events where detailed diffing was skipped (raise `limits:` if needed).
3. **Path renames** are reported as remove + add (no path-level rename heuristic).
4. Single-maintainer bus factor.
5. URL-based multi-file validation fetches siblings sequentially (~82 s on Azure-scale
   trees); parallel prefetch deferred until demonstrated demand.

## Commercial Readiness

| Area | Rating | Reason |
|------|-------:|--------|
| Core engine | 9 | Direction-aware, validated against 7 real ecosystems across every major spec style, deterministic |
| CLI | 9 | Stable exit codes, subcommand help, actionable errors, config file |
| Testing | 9 | 359 tests incl. drift guards, golden snapshots, injection & degradation regressions, seeded multi-file fuzzing (500-seed sweeps clean) |
| Security | 8 | SSRF/traversal/bomb guards (incl. per-sibling-fetch revalidation), output-injection hardening, bounded processing; untrusted-input posture documented |
| Documentation | 9 | Complete set incl. compatibility model, security/privacy, examples, DECISIONS (31 entries) |
| Reporting | 9 | 7 formats, offline flagship HTML, injection-safe output |
| CI/CD | 9 | 3-OS matrix green, sample-scenario assertions, automated tagged releases |
| Distribution | 8 | Global tool + 6 RID archives verified; NuGet/Docker publication awaits credentials |
| Commercial readiness | 8 | Positioning/matrix/pricing/checklist present; two-edition split designed, not enforced |

## Next Highest-Value Improvements

1. Publish: configure `NUGET_API_KEY` secret and push a `v*` tag (release pipeline is armed).
2. Optional: parallel prefetch for remote sibling files on URL-based multi-file specs.

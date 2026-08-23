# OpenAPI Sentinel — Engineering Report

Snapshot date: **2026-08-23** · Tool version: **1.0.0** · Git: `main`

Consolidated status per MASTER_PROMPT §60. Historical evidence lives in `progress.md`;
this page states the verified current state only.

## Completed

- **Engine**: direction-aware comparison of Swagger 2.0 / OpenAPI 3.0 / 3.1 / 3.2 (JSON & YAML),
  semantic normalization, `$ref` resolution (internal / relative files / remote-with-policy),
  cycle markers, bounded processing with graceful degradation.
- **80 rules + 3 engine meta-checks** (`CGxxxx`), stable IDs, human explanations,
  impact and remediation text; aspect-aware discriminator classification; direction-aware
  formats, numeric types/unions, nullability, requiredness, additionalProperties,
  readOnly/writeOnly; callback diffing (CG1011–13); response-header payload diffing;
  OAuth flow add/remove; Swagger 2.0 `collectionFormat` wire-format detection.
- **Reporting**: console (+verbose), JSON (documented stable contract), Markdown,
  PR summary, changelog, single-file interactive HTML (offline), SARIF 2.1.0, JUnit XML.
- **Policy & governance**: severity thresholds incl. Verify semantics, rule overrides,
  path ignores, suppressions with expiry warnings, baseline adoption workflow.
- **CLI**: compare / validate / rules / explain / init / version / help; subcommand help;
  documented exit codes 0/1/2/3/4/5; config file with actionable validation.
- **Security**: YAML bomb & alias caps, node/depth/ref budgets with graceful degradation,
  SSRF guards on URL + remote refs, path-traversal rejection, HTML/markdown/terminal
  output-injection hardening, env-var header auth (never logged).
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
`Rules` (catalog + engine) · `Reporting` (7 formats) · `Cli` (workflows/config).
Only runtime dependency: YamlDotNet (MIT). Fully offline core; no telemetry.

See [architecture/overview.md](architecture/overview.md), [DECISIONS.md](DECISIONS.md)
(27 entries), [compatibility-model.md](compatibility-model.md).

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

`dotnet test -c Release` → **257 passed / 0 failed** (2026-08-23):

| Project | Tests |
|---------|------:|
| Core | 21 |
| Reporting (incl. golden snapshots, injection hardening) | 48 |
| OpenApi (pipeline, robustness, callbacks, degradation) | 62 |
| Rules (engine, FP/FN suites, coverage & catalog guards) | 85 |
| Cli (runner, config loader, workflows) | 38 |
| Integration (end-to-end scenarios) | 3 |

Remote CI: green across ubuntu-latest / windows-latest / macos-latest.
Real-world checks: GitHub REST API (~10 MB) validate 4.9 s; Stripe v2300→v2404 cross-version
diff: 174 findings in 4.3 s at 683 MB peak RSS with default budgets.

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
4. **Path renames** are reported as remove + add (no path-level rename heuristic).
5. Single-maintainer bus factor; test suite is strong but fuzz coverage is ad-hoc.

## Commercial Readiness

| Area | Rating | Reason |
|------|-------:|--------|
| Core engine | 9 | Direction-aware, real-spec validated (GitHub/Stripe), deterministic; content-param limitation noted |
| CLI | 9 | Stable exit codes, subcommand help, actionable errors, config file |
| Testing | 8 | 257 tests incl. drift guards, golden snapshots, injection & degradation regressions; fuzzing ad-hoc |
| Security | 8 | SSRF/traversal/bomb guards, output-injection hardening, bounded processing; untrusted-input posture documented |
| Documentation | 9 | Complete set incl. compatibility model, security/privacy, examples, DECISIONS |
| Reporting | 9 | 7 formats, offline flagship HTML, injection-safe output |
| CI/CD | 9 | 3-OS matrix green, sample-scenario assertions, automated tagged releases |
| Distribution | 8 | Global tool + 6 RID archives verified; NuGet/Docker publication awaits credentials |
| Commercial readiness | 8 | Positioning/matrix/pricing/checklist present; two-edition split designed, not enforced |

## Next Highest-Value Improvements

1. Publish: configure `NUGET_API_KEY` secret and push a `v*` tag (release pipeline is armed).
2. Content-parameter multi-media-type support (removes the last known silent spot).
3. Optional: schema-fuzz differential harness for continuous FP/FN discovery.

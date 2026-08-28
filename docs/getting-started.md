# Getting started

## Read an HTML compatibility report

Generate a portable HTML report with Traditional-Chinese UI (the default):

```powershell
openapi-sentinel compare old.yaml new.yaml --html api-diff.html
```

Use English when sharing the report with an English-speaking team:

```powershell
openapi-sentinel compare old.yaml new.yaml --html api-diff.html --html-lang en
```

The HTML page uses two primary sections, displays deterministic Taipei UTC+8 generation time, and starts
all change cards collapsed. Its semantic presentation deduplication retains concrete property/parameter
targets while grouping identical logical events only; CI policy and every machine-readable report continue
to use the complete finding stream.

This guide takes you from install to first CI verdict in five minutes.

## 1. Install

**Option A — .NET global tool** (requires the .NET SDK/runtime you build against):

```bash
dotnet pack src/OpenApiSentinel.Cli -c Release -o pkg
dotnet tool install --global --add-source ./pkg OpenApiSentinel
openapi-sentinel version
```

**Option B — self-contained binary** (no .NET required):

```bash
./build/release.sh          # produces dist/<version>/ archives for 6 platforms
```

## 2. Prepare two contracts

Any of: local files, URLs, stdin (`-`), or git revisions (`git:<rev>:<path>`).

```bash
openapi-sentinel validate old.yaml     # sanity-check a single contract
openapi-sentinel compare old.yaml new.yaml
```

To keep a local baseline file current only when comparison policy passes, use:

```bash
openapi-sentinel compare ./swagger.json https://trusted.example/swagger.json --allow-remote --update-snapshot --fail-on high
```

`--update-snapshot` accepts only a local OLD file, writes the raw NEW entry document atomically,
and never advances the snapshot on fetch/parse/report/policy failure. Use `--fail-on none` only
when intentionally accepting all findings before advancing it.

## 3. Understand the output

The default console report shows:

- **Result** — PASSED / FAILED according to your policy.
- **Summary** — counts by severity and compatibility class, plus a semver recommendation.
- **Findings** — `[SEVERITY] RULE-ID location` with an explanation line.
- **Suppressed** — acknowledged findings with their reasons (visible, not hidden).

Add `--verbose` to include impact and suggested action per finding.
Add `--format json|markdown|html` or `--html report.html` for artifacts.

## 4. Set a policy

```bash
openapi-sentinel compare old.yaml new.yaml --fail-on high   # CLI override
```

or commit `openapi-sentinel.yml`:

```yaml
failOn: high        # critical|high|medium|low|verify|info|all|none
```

`failOn` accepts a severity threshold, `all` (any finding fails), or `none` (report only).
The exit code is what CI should act on:

| Code | Meaning |
|------|---------|
| 0 | passed |
| 1 | policy violated |
| 2 | invalid spec/input |
| 3 | invalid configuration |
| 4 | network failure |
| 5 | internal error |

## 5. Adopt gradually

Real repositories have history. Use baselines and suppressions so only *new* breakage blocks CI:

```bash
# Adopt today's findings as the baseline:
openapi-sentinel compare v1.yaml v2.yaml --update-baseline sentinel-baseline.json --fail-on none

# From now on, baseline entries are acknowledged; new findings still fail:
openapi-sentinel compare v1.yaml v3.yaml --baseline sentinel-baseline.json
```

Suppressions in `openapi-sentinel.yml` require a reason and can carry an expiry date;
expired suppressions resurface their findings plus a visible warning.

## 6. Wire into CI

See [GitHub Actions](github-actions.md), [Azure DevOps](azure-devops.md) or [GitLab](gitlab.md).
The typical flow:

1. Generate/checkout the current contract on your branch.
2. Load the base contract from `main`.
3. Run `openapi-sentinel compare base current --fail-on high`.
4. Publish the HTML report as a build artifact and/or PR comment.

## Next steps

- Browse the [rule catalog](rules.md) and tune severities via config overrides.
- Read the [compatibility model](compatibility-model.md) to understand direction-aware classification.
- Skim [configuration](configuration.md) for remote-reference security settings.
- **Frontend/testing teams?** Sync the contract into a Bruno collection with
  auto-generated Traditional-Chinese test scenarios — [Bruno sync](bruno-sync.md) and
  [scenarios](bruno-scenarios.md); wire it to build via
  [Visual Studio integration](visual-studio-integration.md).

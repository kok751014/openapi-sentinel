# Configuration

OpenAPI Sentinel reads `openapi-sentinel.yml` (or `.yaml`) from the working directory,
or the path given via `--config`. Unknown keys produce **warnings** (never silent);
semantic errors exit with code 3.

## Full reference

```yaml
# ── Policy ────────────────────────────────────────────────────────────────
# Minimum severity that fails CI. One of:
#   critical | high | medium | low | verify | info   — threshold (inclusive of
#                                                      higher severities; see banding note)
#   all                                              — any finding fails
#   none                                             — never fail (report only)
failOn: high
#
# Threshold banding:
#   Severities rank info < low < medium < high < critical, with `verify` ranked above
#   critical as its own "needs human review" class. A threshold matches that severity
#   and everything ABOVE it — so `--fail-on high` fails on high, critical AND verify;
#   `--fail-on verify` matches only Verify-class findings; use `all` to fail on every
#   finding including informational ones. The default policy (`breaking`) ignores this
#   ladder entirely and fails on Breaking/PotentiallyBreaking findings at medium+. 

# ── Rule overrides ────────────────────────────────────────────────────────
rules:
  CG1003: info          # remap severity (critical|high|medium|low|info|verify)
  CG5019: off           # disable a rule entirely

# ── Ignore paths (glob) ───────────────────────────────────────────────────
ignore:
  paths:
    - /internal/**      # findings under these paths are excluded entirely
    - "*/beta/*"

# ── Remote references & URL fetching ──────────────────────────────────────
remoteReferences:
  enabled: false              # master switch; also --allow-remote on the CLI
  allowHttp: false            # plain HTTP opt-in (HTTPS enforced by default)
  allowPrivateNetwork: false  # localhost/link-local/private ranges (SSRF guard)
  allowedHosts: []            # e.g. [specs.example.com]; empty = any public host
  timeoutSeconds: 30
  maxResponseBytes: 20971520  # 20 MB

# ── URL authentication via environment variables ──────────────────────────
headerEnv: {}
  # Authorization: SENTINEL_SWAGGER_TOKEN
  # X-Api-Key:     SENTINEL_API_KEY
  # Values are read from the environment at fetch time, never logged.

# ── Suppressions (acknowledged findings) ──────────────────────────────────
suppressions:
  - rule: CG1001            # a single rule id or "*"
    path: "/legacy/**"      # optional glob
    reason: "Legacy mobile client migration"   # REQUIRED
    expires: 2026-12-31     # optional; after this date findings resurface + warning

# ── Unknown change kinds ─────────────────────────────────────────────────
# Emit an aggregated Verify finding (CG9003) when the comparer detects a
# structural change no registered rule evaluates.
reportUnknownKinds: false

# ── Processing safety budgets (rarely needed) ─────────────────────────────
# Hard bounds against malicious/degenerate inputs. When a budget trips during
# $ref resolution the run now DEGRADES instead of failing: affected subtrees
# become opaque markers; identical markers compare silently, differing ones
# surface as a single Verify finding (CG5022). Raise these only for very
# large, legitimately reference-heavy specifications.
limits:
  maxDocumentBytes: 20971520   # raw document size cap (bytes)
  maxDepth: 128                # nesting depth cap
  maxNodes: 2000000            # parsed-node cap
  maxResolvedRefs: 200000      # total $ref resolutions cap
  maxRefChainDepth: 64         # $ref chain depth cap

# ── Baseline ──────────────────────────────────────────────────────────────
baseline: sentinel-baseline.json   # CLI --baseline/--update-baseline override this

# ── Output artifacts ──────────────────────────────────────────────────────
output:
  json: artifacts/api-diff.json
  markdown: artifacts/api-diff.md
  html: artifacts/api-diff.html
  sarif: artifacts/api-diff.sarif
  junit: artifacts/api-diff.junit.xml
  changelog: artifacts/CHANGELOG.md
  pr-summary: artifacts/pr-summary.md   # compact PR-comment markdown
```

## Semantics

### `rules` overrides

- `off` disables the rule; its changes are skipped (counted in engine statistics).
- A severity name remaps the finding's severity before policy evaluation.
- IDs must match `CG\d{4}`; see `openapi-sentinel rules`.

### `ignore.paths`

Glob semantics: ordinal case-sensitive matching, `*` matches within one path segment,
`**` crosses segments (`/internal/**` also matches `/internal` itself), `?` is a single
non-slash character. Ignored findings are excluded from reports and policy.

### Suppressions vs baseline

| Mechanism | Scope | Best for |
|-----------|-------|----------|
| Suppression | rule + path glob, with reason and optional expiry | deliberate, reviewable acknowledgements in config |
| Baseline | exact finding fingerprints | bulk adoption of pre-existing debt |

Both keep findings **visible** in the `Suppressed` section of every report — nothing is
silently hidden. Expired suppressions resurface their findings plus a `CG9001` warning.
Stale baseline entries produce an aggregated `CG9002` informational finding so baselines
stay meaningful.

### Engine meta-findings

| ID | Meaning | Default severity |
|----|---------|------------------|
| `CG9001` | An expired suppression matched this finding | low |
| `CG9002` | Baseline entries no longer match anything | info |
| `CG9003` | Change kinds no registered rule evaluates (opt-in via `reportUnknownKinds` / `--report-unknown-kinds`) | verify |

## Validation behaviour

- Unknown top-level keys and unknown keys inside `ignore`, `remoteReferences`, `output`
  produce warnings on stderr.
- Unknown suppression keys warn; missing `reason` fails with exit code 3.
- Invalid severity names, malformed dates and bad rule ids fail with actionable messages.

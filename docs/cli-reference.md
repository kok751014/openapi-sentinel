# CLI reference

`openapi-sentinel <command> [arguments] [options]`

## Commands

### `compare <OLD-SPEC> <NEW-SPEC>`

Full compatibility comparison. This is the command CI uses.

| Option | Description |
|--------|-------------|
| `--format console\|json\|markdown\|html` | Primary output format (default: `console`). Goes to stdout unless a file target for the same format is given. |
| `-o, --output <path>` | Write the primary format to a file instead of stdout. |
| `--json`, `--markdown`, `--html`, `--sarif`, `--junit`, `--changelog`, `--pr-summary <path>` | Additional artifacts; combinable. Paths override the config `output:` section. |
| `--fail-on <severity\|all\|none>` | Override the config's `failOn` for this run. Thresholds are inclusive of higher severities (`verify` outranks `critical`, so `high` and above also match Verify findings); `--fail-on verify` alone matches only Verify-class findings — use `all` to fail on every finding. |
| `--no-color` | Disable ANSI colors. Colors are also auto-disabled when output is redirected. |
| `--verbose` | Include impact + suggested action per finding in console/markdown. |
| `--config <path>` | Explicit configuration file (default: `openapi-sentinel.yml[.yaml]` in the working directory). |
| `--allow-remote` | Enable remote references/URL fetching for this run (equivalent to `remoteReferences.enabled`). |
| `--baseline <file>` | Suppress findings whose fingerprints appear in the baseline file. |
| `--report-unknown-kinds` | Emit an aggregated VERIFY finding (CG9003) for changes no rule evaluates. |
| `--no-suppressed` | Omit the suppressed-findings section from reports (default: shown with reasons). |
| `--update-baseline <file>` | Write current findings as a new baseline file (existing entries are kept). |
| `--no-color`, `--verbose`, `--debug` | Output/diagnostics controls (`--debug` shows stack traces for internal errors). |

**Input sources** for both specs:

- local file path
- `http://…` / `https://…` URL (subject to remote-fetch policy)
- `-` or `stdin`
- `git:<revision>:<path>` — reads the file from a git revision (e.g. `git:origin/main:openapi.yaml`)

Authentication for URL sources uses environment variables via the `headerEnv` config section;
secrets never appear on the command line and are never logged.

**External `$ref` resolution**: internal pointers, sibling files and remote http(s) references
are supported. Relative file references may roam anywhere inside the main document's directory
tree; layouts that climb above it (e.g. Azure resource-manager `common-types`) opt in via
`externalReferences.allowAboveDocumentTree: true`. Documents loaded from a URL resolve
sibling files against their own URL under the remote-fetch policy. Missing or unfetchable
references degrade to Verify-level findings (`CG5022`) instead of aborting — see
[configuration](configuration.md) and [security](security.md).

### `validate <SPEC>`

Parse, resolve references and normalize one contract; prints a summary (version, paths,
operations, security schemes) or an actionable error.

### `rules [--format json]`

List every built-in rule (ID, title, default severity). `--format json` emits machine-readable
output used to generate [docs/rules.md](rules.md).

### `explain <RULE-ID>`

Full documentation of one rule: description, impact, remediation, breaking/non-breaking
examples and limitations — the same source of truth as [docs/rules.md](rules.md).

### `init [--force]`

Write a commented starter `openapi-sentinel.yml` in the current directory.
Refuses to overwrite without `--force`.

### `bruno <sync|examples> [options]`

Sync an OpenAPI contract into a single-file Bruno OpenCollection YAML collection,
preserving user-maintained values/scripts/tests/docs/auth, and generate
Traditional-Chinese test scenarios. Full guides: docs/bruno-sync.md and
docs/bruno-scenarios.md.

```
openapi-sentinel bruno sync [options]
openapi-sentinel bruno examples [options]
```

| Option | Applies to | Meaning |
|--------|------------|---------|
| `--source <file-or-url>` | both | OpenAPI/Swagger source (overrides `bruno.source`) |
| `--collection <path.yml>` | both | Collection target; must be `.yml`/`.yaml` (directory targets are refused) |
| `--report-dir <dir>` | sync | Report output directory (default `./api-contract/reports`) |
| `--config <path>` | both | Explicit configuration file |
| `--dry-run` | sync | Analyze and print planned changes without writing anything |
| `--remove-deleted` | sync | Delete requests whose operations were removed (default: mark stale) |
| `--no-examples` | sync | Skip scenario generation for this run |
| `--operation <operationId>` | examples | Process only this operation |
| `--path <api-path>` | examples | Process only operations under this API path prefix |
| `--verbose` | both | Per-operation change/preservation details |
| `--no-color` | both | Plain text output |

Behavior notes:

- Incremental skip: when the contract fingerprint, bruno config, example config and
  business-scenario file are unchanged since the last successful sync, the run prints
  `No API contract changes detected.` / `Nothing to update.` and exits 0 immediately.
- Reports `bruno-sync-report.html` (zh-TW, offline) and `bruno-sync-report.json`
  (schema `bruno-sync/1.0`) are written after every non-skipped sync.
- Exit codes follow the shared contract below; conflicts surface as report entries and
  warnings without changing the exit code.

### `version`

Print the tool version.

## Exit codes

| Code | Meaning |
|------|---------|
| `0` | Comparison completed and policy passed. |
| `1` | Policy threshold violated (see `failOn`). |
| `2` | Invalid input/specification — parse errors, unresolvable references, missing files. |
| `3` | Invalid configuration — unknown keys produce warnings; semantic errors fail here. |
| `4` | Network/reference retrieval failure — unreachable host, policy rejection (SSRF guard), size/timeout limits. |
| `5` | Internal application failure. Rerun with `--debug` for diagnostics and report it. |

These codes are stable and safe to script against; do not parse console text for verdicts.

## JSON output contract

`schemaVersion: "1.0"` fields are additive-only within the same major schema version:
removals or renames bump `schemaVersion`. Top-level structure:

```json
{
  "schemaVersion": "1.0",
  "toolVersion": "1.0.0",
  "result": "passed",
  "oldContractName": "old.yaml",
  "newContractName": "new.yaml",
  "oldOpenApiVersion": "3.0.3",
  "newOpenApiVersion": "3.0.3",
  "oldInfoVersion": "1.4.0",
  "newInfoVersion": "1.5.0",
  "generatedAtUtc": "2026-03-01T10:30:00Z",
  "summary": { "total": 0, "breaking": 0, "...": 0 },
  "findings": [],
  "suppressed": [],
  "recommendation": { "bump": "none", "reason": "No findings." },
  "policyViolated": false,
  "endpoints": [
    { "path": "/orders", "httpMethod": "get", "state": "changed", "findingCount": 3 }
  ]
}
```

`endpoints` lists every endpoint touched by changes (added/removed/changed) with its
finding count — the blast-radius view. `oldInfoVersion`/`newInfoVersion` carry the
contracts' declared `info.version` (distinct from the raw spec version).

The optional `processing` object reports how the engine handled the change stream:
total changes seen, findings emitted, unmatched changes, rule overrides that disabled
rules, ignore-path filtering, suppression and baseline counts. Use it to answer
"why didn't X appear?" programmatically.
```

Each finding carries `ruleId`, `title`, `severity`, `category`, `changeType`,
`compatibility`, `confidence`, `direction`, `path`, `httpMethod`, `location`,
`oldValue`, `newValue`, `explanation`, `impact`, `suggestedAction` plus a stable
`fingerprint` used by baselines.

## SARIF & JUnit

- **SARIF 2.1.0** — one result per finding with `ruleId`, level mapping
  (Critical/High → `error`, Medium/Low → `warning`, Info/Verify → `note`) and location data;
  suitable for GitHub code scanning ingestion pipelines.
- **JUnit XML** — `<testsuites>` with one failing testcase per policy-violating finding;
  a single green testcase when the policy passes. CI systems can render it natively.

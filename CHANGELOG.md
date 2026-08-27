# Changelog

## [1.3.0] - 2026-08-27

### Added

- Traditional-Chinese HTML compatibility reports by default with `--html-lang en` opt-in.
- Action-oriented change summary, needs-attention section, related changes and snapshot status.

### Changed

- Exact duplicate findings are collapsed in HTML presentation only; policy and machine outputs remain raw.
- Breaking and review-required changes are shown before safe additions; non-breaking details start collapsed.

### Fixed

- Prevent duplicated `v` prefix in displayed API versions such as `api vv1`.
- Improved HTML filtering, responsive layout and print readability.

### Compatibility

- Comparison engine, policy evaluation, JSON/Markdown/SARIF/JUnit outputs, baselines and rule semantics remain unchanged.

All notable changes to OpenAPI Sentinel are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/); versioning follows
[SemVer](https://semver.org/). Release artifacts are produced from the tagged commit by
`build/release.sh` (see docs/commercial/release-checklist.md).

## [Unreleased]

## [1.2.0] - 2026-08-27

### Added

- Explicit `--insecure-tls` / `remoteReferences.insecureTls` opt-in for trusted internal or test
  HTTPS OpenAPI sources.
- `compare --update-snapshot`, which atomically advances a local raw-contract snapshot only after
  every requested report is written and the comparison policy passes.

### Fixed

- `validate`, `compare`, and Bruno commands now apply the same independent remote, private-network,
  and TLS CLI security overrides.
- Boolean CLI flags no longer consume positional specification arguments.
- Windows release automation now publishes each RID with its own restore/build assets and emits the
  same `SHA256SUMS` artifact contract as the shell release script.

### Security

- Insecure TLS remains default-off and is independent from remote-loading and private-network opt-ins;
  SSRF checks, host allowlists, and redirect validation remain active.

## [1.1.0] - 2026-08-27

### Fixed — Bruno OpenCollection import compatibility (release-blocker for 1.1.0)

Real-world Bruno imports of the generated single-file collection failed with
`Import OpenCollection failed`. Root cause: the generator emitted shapes the official
OpenCollection v1.0 schema rejects. Generated collections are now validated against the
official OpenCollection v1.0 schema before being written.

- **Official schema gate** — a pinned copy of the official JSON Schema
  (`opencollection.schema.json`, upstream commit recorded in the embedded resource's
  README) ships as an EmbeddedResource; every sync/examples run validates the serialized
  output BEFORE any write. Failure blocks the collection, workspace, reports AND the
  incremental state file, and prints precise per-path diagnostics instead of a bare
  "invalid YAML". Dry-run validates too (non-zero exit on failure, zero writes).
- **Illegal request-level `x-sentinel` removed** — `HttpRequest` sets
  `additionalProperties: false`; all Sentinel metadata now lives in the schema-legal
  collection-level `extensions.x-sentinel.operations` index keyed by stable identity,
  with each request bound via an `info.tags` entry (`x-sentinel:<key>`). Identity
  transitions merge old entries into new ones so user-stored fields are never dropped.
- **Backward migration** — legacy v1.1.0 files carrying request-level `x-sentinel`
  blocks migrate automatically on the next sync: metadata moves into the index, the
  illegal block is stripped, unknown extra keys are carried over verbatim.
- **Params carry explicit `type`** — every generated param row (request + example level)
  now emits `type: query` / `type: path` as the schema requires; loaded rows missing it
  default to `query` with a visible diagnostic.
- **All param values serialize as strings** — values that would re-read as YAML
  numbers/bools/null are quoted (`value: "12345"`), matching the schema's string-typed
  slots; JSON body content keeps its native types.
- **Full-shape audit beyond the three known bugs** — legacy illegal `enabled:` row flags
  map to `disabled:` semantics, numeric-looking string slots are coerced safely, root
  gains the recommended `bundled` marker, and folder-level request defaults normalize.
- **Reports** — HTML shows `OpenCollection Schema：1.0.0` / `Schema Validation：PASS`;
  JSON adds `openCollectionSchemaVersion` and `schemaValidation.status` (additive within
  `bruno-sync/1.0`).
- Schema compatibility is verified automatically by tests against the pinned schema;
  Bruno Desktop import should still be manually smoke-tested before release.

### Added — one-click bootstrap & Bruno folder workspace

- **Auto bootstrap directories** — `bruno sync` creates every missing OUTPUT directory
  (collection parent, report dir, state dir); a missing local SOURCE fails clearly
  (exit 2) and never fabricates input files.
- **Config bootstrap** — first run without `openapi-sentinel.yml` but WITH `--source`
  generates a production-ready config (quoting-safe, secrets-free, directly reusable);
  existing configs are never overwritten. Running with neither config nor source prints
  an actionable zh-TW hint and exits 2.
- **Full layout bootstrap** — `api-contract/openapi/`, `bruno/`, `bruno-workspace/`,
  `scenarios/` (with an editable empty zh-TW template), and `reports/` are created on
  first sync; remote sources additionally snapshot the raw entry document to
  `api-contract/openapi/source-snapshot.json` (never auth/header content; local sources
  are never copied or modified).
- **Bruno folder workspace (`workspacePath`)** — new first-class output in the official
  filesystem format (`opencollection.yml` + `folder.yml` + one `.yml` per request,
  `bundled: false`), so users open the collection in Bruno directly — no repeated
  Import. Workspace and bundled collection come from ONE canonical model; when both
  exist the workspace is the canonical source for preservation. Manual edits inside
  workspace files survive subsequent syncs; unbound user files are never deleted;
  operations removed under `removeDeletedOperations: true` have their orphaned files
  cleaned up. Filenames derive from request names through Windows/Linux/macOS-safe
  sanitization (traversal-proof, reserved device names defused).
- **Dry-run bootstrap preview** — `--dry-run` lists every planned directory/file
  (`[預計建立資料夾] …`) while leaving the filesystem byte-identical.

### Added — Bruno collection sync + scenario generator

New product capability, fully backward-compatible: no existing command, flag, config key,
report format or exit code changed behavior.

- **`openapi-sentinel bruno sync`** — incrementally syncs an OpenAPI/Swagger contract into a
  single-file Bruno OpenCollection YAML collection. Preservation-first merging: contract
  owns structure (method/URL/parameter names/body schema), users own values, scripts,
  tests, docs and auth. Deleted operations are reported stale by default;
  `removeDeletedOperations` is the only deletion path.
- **`openapi-sentinel bruno examples`** — regenerates test scenarios on demand with
  `--operation` / `--path` filters; overrides config `examples.enabled: false`.
- **Scenario generator** — deterministic Traditional-Chinese (zh-TW) Layer-1 cases derived
  only from machine-checkable schema facts (required/optional, enum, min/max incl.
  exclusive bounds, length bounds, provably-violable patterns, formats, wrong-type,
  array item counts, boundaries), hard-capped by `maxPerOperation`. Authentication is
  never turned into scenarios.
- **Business scenarios (Layer 2)** — `openapi-sentinel.scenarios.yml` for contract-
  inexpressible logic, validated at load time (duplicates, unknown operationIds,
  status ranges) and merged into examples.
- **Sync reports** — `bruno-sync-report.html` (self-contained, zh-TW UI) and
  `bruno-sync-report.json` (schema `bruno-sync/1.0`, deterministic ordering, portable
  as-configured paths).
- **Incremental state** — hash-based fast path (`No API contract changes detected.`)
  backed by a schema-versioned state file containing hashes/timestamps only.
- **Config** — new optional top-level `bruno:` section; relative paths resolve against the
  config file's directory so MSBuild/CI runs behave identically from any working directory.
- **MSBuild integration sample** — `samples/bruno-sync/OpenApiSentinel.targets`
  (design-time-build safe, single-project import recommended).
- New project `OpenApiSentinel.Bruno`; YamlDotNet remains the only runtime dependency.
- Tag/folder divergence reporting: when a contract's tags now map an operation to a
  different folder, sync warns with both paths instead of silently (or automatically)
  relocating the request.
- Malformed-collection hardening: list slots (`items`, `params`, `headers`, `examples`)
  holding non-list values fail at parse time with item-level context instead of being
  silently replaced during a merge.
- Symlink-safe writes: a collection reached through a symbolic link is updated at its
  resolved target atomically; the link itself is never replaced.
- Bruno commands honor the same remote-fetch security model as compare: `--allow-remote`
  or a fully opted-in `remoteReferences:` config is required for URL sources.

Quality evidence at time of writing: 551/551 tests green across 7 test projects
(incl. a seeded differential fuzz harness for sync invariants, validated clean at 500
seeds; official-schema conformance suites for bundled and workspace outputs); fidelity-
preserving YAML round-trip tests; hostile-metadata security tests;
1000-operation scale probes.

### Upgrade notes

- No action required for existing users: the `bruno:` section is optional and every
  pre-existing workflow is unchanged. Collections produced by earlier 1.1.0-dev builds
  (request-level `x-sentinel`) migrate automatically on the next `bruno sync`.
- Directory-based `.bru` collections are intentionally NOT writable (clear refusal, exit 3);
  convert via Bruno's OpenCollection export first. See docs/bruno-sync.md.

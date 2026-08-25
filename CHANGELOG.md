# Changelog

All notable changes to OpenAPI Sentinel are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/); versioning follows
[SemVer](https://semver.org/). Release artifacts are produced from the tagged commit by
`build/release.sh` (see docs/commercial/release-checklist.md).

## [Unreleased]

### Added — Bruno collection sync + scenario generator (recommendation: release as 1.1.0)

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
- Symlink-safe writes: a collection reached through a symbolic link is updated at its
  resolved target atomically; the link itself is never replaced.
- Bruno commands honor the same remote-fetch security model as compare: `--allow-remote`
  or a fully opted-in `remoteReferences:` config is required for URL sources.

Quality evidence at time of writing: 450/450 tests green across 7 test projects
(incl. a seeded differential fuzz harness for sync invariants, validated clean at 500
seeds); fidelity-preserving YAML round-trip tests; hostile-metadata security tests;
1000-operation scale probes.

### Upgrade notes

- No action required for existing users: the `bruno:` section is optional and every
  pre-existing workflow is unchanged.
- Directory-based `.bru` collections are intentionally NOT writable (clear refusal, exit 3);
  convert via Bruno's OpenCollection export first. See docs/bruno-sync.md.

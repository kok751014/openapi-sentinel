# Troubleshooting

## Exit codes

Start from the exit code — they are stable (see [cli-reference](cli-reference.md#exit-codes)).

### Exit 2: invalid spec

- *"Unresolvable reference …"* — a `$ref` target does not exist. Check fragment spelling
  and that referenced files sit inside the main document's directory tree.
- Deep-nesting errors indicate the depth limit was hit (defaults: 128 parse depth,
  64 `$ref` chain depth).
- **Very large, reference-heavy specs no longer fail outright**: when the node/ref budgets
  trip during `$ref` resolution the comparison degrades — affected subtrees become opaque
  markers and any non-identical pair surfaces as a single `CG5022` Verify finding. To get
  full detail instead, raise the budgets via the `limits:` config section
  (see [configuration](configuration.md)); e.g. Stripe-scale specs compare cleanly with
  defaults today, at reduced granularity only if they grow further.
- **Missing external reference files** (dead `$ref` targets, unreachable URLs) also degrade:
  `validate` reports `Warning: N external reference(s) could not be resolved` and still
  exits 0; comparisons surface differing unresolved references as `CG5022` Verify findings.
  Multi-file layouts may reach shared files via `../` anywhere inside the main document's
  directory tree; layouts that also climb ABOVE it (e.g. Azure resource-manager common-types)
  need `externalReferences.allowAboveDocumentTree: true`. Documents loaded from URLs resolve
  sibling files relative to their own URL.
- Swagger 2.0 documents missing `info`/`paths` fail version detection with an actionable
  message.

### Exit 3: invalid configuration

- Unknown keys warn but do not fail; semantic problems do (bad severity, missing
  suppression `reason`, malformed `expires`, unknown rule id). Messages name the exact key.
- A stale `openapi-sentinel.yml` in the working directory applies automatically — pass an
  explicit `--config` if another file was intended.

### Exit 4: network failure

- Remote fetching is **disabled by default**: URL specs or remote `$ref` produce this code
  until `remoteReferences.enabled: true` or `--allow-remote`.
- Private-network addresses are blocked by the SSRF guard; allow-list hosts or set
  `allowPrivateNetwork: true` consciously.
- Redirects are followed manually and each hop is revalidated (max 5).

### Exit 5: internal error

Rerun with `--debug` to see the stack trace, then report the spec snippet that triggered it.

## Findings look wrong

- **Duplicate-looking findings at different locations** — shared components (`$ref`
  parameters/schemas) expand per operation; the same underlying change legitimately appears
  once per affected endpoint to show blast radius.
- **Missing a change you expected?** Run `openapi-sentinel rules --format json` and
  `explain <RULE-ID>` to check coverage; file an issue with both specs if a rule should fire.
- **Suspicious that some changes go unclassified?** Enable `--report-unknown-kinds`
  (or `reportUnknownKinds: true` in config): structural changes no registered rule
  evaluates are surfaced as an aggregated CG9003 Verify finding instead of being silent.
- **Too noisy?** Tune severities via `rules:` overrides, silence paths via `ignore.paths`,
  acknowledge known debt with [suppressions/baselines](configuration.md).

## Performance

Parsing dominates; see [benchmarks.md](benchmarks.md). If a huge spec is slow, check that
references resolve locally rather than over the network, and consider splitting the contract.

## Bruno sync

- **"collection 目標是一個目錄" / exit 3** — Sentinel syncs single-file OpenCollection YAML
  only. Point `bruno.collectionPath` at a `.yml` file; convert a `.bru` directory via
  Bruno's OpenCollection export first (see [bruno-sync](bruno-sync.md#支援格式)).
- **`No API contract changes detected.` but you expected changes** — the incremental state
  hashes the contract surface sync derives from; documentation-only edits intentionally do
  not trigger churn. Delete the hidden `<collection>.yml.sentinel-state.json` to force a
  full rebuild (it is safe: state corruption always degrades to a full sync).
- **Examples missing** — check `bruno.examples.enabled` is not false. Note that an explicit
  `openapi-sentinel bruno examples` command overrides `enabled: false`, while plain
  `sync` respects it (`--no-examples` suppresses everywhere).
- **Manual values reverted?** They should not be — preservation is tested end-to-end.
  Verify you are editing the same file the config resolves to (paths resolve against the
  CONFIG FILE's directory, not your shell's cwd) and that `preserveManualValues` is true.
- **Header/cookie parameters in examples** — declared header params and a composite Cookie
  row are included automatically; if they are absent, the examples predate this feature —
  re-run `bruno examples`.

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

- **`Import OpenCollection failed` in Bruno (older versions)** — fixed: generated
  collections are now validated against the official OpenCollection v1.0 schema before
  being written, so illegal shapes (request-level metadata blocks, params without `type`,
  numeric-typed values) can no longer be emitted. Re-run `bruno sync`; legacy files are
  migrated automatically. Prefer opening the folder workspace directly
  (`api-contract/bruno-workspace/<project>`) instead of importing the single file.
- **"collection 目標是一個目錄" / exit 3** — Sentinel syncs OpenCollection YAML only.
  Point `bruno.collectionPath` at a `.yml` file; convert a `.bru` directory via
  Bruno's OpenCollection export first (see [bruno-sync](bruno-sync.md#支援格式)).
- **`OpenCollection validation failed: … is not an allowed property` / exit 2** — the
  gate refused to write because something (usually a hand-added key on a request root)
  violates the official schema. The message names the exact path. Move custom data into
  `extensions.x-sentinel.operations[<key>]` (collection level) or an `info.tags` entry;
  nothing was written, so your files are untouched.
- **Workspace and bundled collection disagree** — when both exist the WORKSPACE is the
  canonical source; bundled `collection.yml` is regenerated from it. Do not hand-edit the
  bundled file expecting changes to survive (see [bruno-sync](bruno-sync.md#workspace-與-bundled-collection-的角色)).
- **`No API contract changes detected.` but you expected changes** — the incremental state
  hashes the contract surface sync derives from; documentation-only edits intentionally do
  not trigger churn. Delete the hidden `<collection>.yml.sentinel-state.json` to force a
  full rebuild (it is safe: state corruption always degrades to a full sync).
- **Examples missing** — check `bruno.examples.enabled` is not false. Note that an explicit
  `openapi-sentinel bruno examples` command overrides `enabled: false`, while plain
  `sync` respects it (`--no-examples` suppresses everywhere).
- **Manual values reverted?** They should not be — preservation is tested end-to-end,
  including edits made inside workspace request files. Verify you are editing the file
  Sentinel actually reads (workspace first, else the config-resolved collection) and that
  `preserveManualValues` is true.
- **Header/cookie parameters in examples** — declared header params and a composite Cookie
  row are included automatically; if they are absent, the examples predate this feature —
  re-run `bruno examples`.

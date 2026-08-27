# OpenAPI Sentinel v1.3.0 release notes

## Highlights

- HTML compatibility reports now default to Traditional Chinese. Use `--html-lang en` for English.
- HTML presents distinct human changes: exact duplicate rule events are collapsed only in the view.
  The comparison engine, policy evaluation, JSON, Markdown, SARIF, JUnit, baseline fingerprints and
  suppression semantics continue to use the complete raw finding stream.
- A breaking-first summary, needs-attention area, concrete endpoint descriptions and conservative
  related-change groups make the report easier to triage without guessing renames or API moves.
- Compare HTML reports show whether `--update-snapshot` was disabled, blocked by policy, or completed.
  Snapshot advancement remains last and atomic; HTML is finalized atomically after it succeeds.
- API versions already prefixed with `v` now render once (for example `API v1`, not `api vv1`).

## Upgrade notes

No configuration migration is required. The new `--html-lang <zh-TW|en>` option affects HTML only;
console and all machine-readable report contracts stay unchanged.

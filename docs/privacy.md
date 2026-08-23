# Privacy

OpenAPI Sentinel works **entirely on your machine**.

- **No telemetry.** No metrics, crash reporting, phone-home or update pings.
- **No uploads.** Contracts, diffs and reports never leave your filesystem.
- **No accounts.** Nothing to sign up for; no license servers are contacted.
- **No AI services.** All classification is deterministic, rule-based and auditable.
- **No source-code access.** Only the two contract files you point it at are read.

### Network access

By default the tool performs **zero network I/O**. Network features exist only when you
explicitly enable them:

- comparing `http(s)://` URLs,
- resolving remote `$ref` targets.

When enabled, requests go only to hosts your configuration permits (HTTPS by default),
with authentication headers sourced from environment variables — never logged, never
written to reports.

### Determinism

Given identical inputs, configuration and tool version, output — including report bytes —
is deterministic. This makes reports diff-friendly and safe to commit as artifacts.

See also [security.md](security.md) for how untrusted specifications are handled.

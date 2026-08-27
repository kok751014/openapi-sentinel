# OpenAPI Sentinel v1.1.0 release notes

This document describes the v1.1.0 release content.

## Highlights

- **Bruno OpenCollection sync.** `openapi-sentinel bruno sync` turns an OpenAPI/Swagger
  contract into a Bruno collection and incrementally synchronizes later contract changes.
- **Native Bruno folder workspace and bootstrap.** A first sync can create the configuration
  and output layout, including a folder-based Bruno workspace that Bruno can open directly,
  alongside the bundled OpenCollection artifact.
- **Preservation-first updates.** Contract-owned request structure is refreshed while manual
  values, scripts, tests, documentation, authentication, user examples, and unbound workspace
  files remain under user control. Removed operations are reported as stale unless explicit
  removal is configured.
- **Better request generation.** POST/PUT request bodies are generated and merged more
  reliably, and multipart/form-data requests now preserve the required multipart shape.
- **OpenCollection validation before writes.** Generated bundled collections are validated
  against the pinned official OpenCollection v1.0 schema before collection, workspace, report,
  or incremental-state output is written.
- **Useful, quiet sync reporting.** Meaningful sync changes create timestamped HTML and JSON
  report pairs. Unchanged operation details are filtered from reports, and no report is created
  when there are no meaningful changes.

## Generated Bruno examples

The generated Bruno example/scenario feature remains implemented but is experimental and
disabled by default in v1.1.0. Normal request, body, parameter, header, collection, and
workspace generation remains available without it. Enable generated examples explicitly with
`bruno.examples.enabled: true`, or run the explicit `bruno examples` command when appropriate.

## Remote-source security

Remote URL sources and remote references remain opt-in. Use `--allow-remote` (or the matching
configuration) for remote access; localhost and private-network targets additionally require
the explicit private-network opt-in. HTTPS is required by default, with HTTP requiring its own
explicit configuration.

## Known limitation

HTTPS endpoints with untrusted, self-signed, or hostname-mismatched certificates are not
automatically bypassed. Use a certificate that the runtime trusts and whose hostname matches the
endpoint.

# OpenAPI Sentinel v1.2.0 release notes

## Highlights

- **Safe remote snapshot advancement.** `compare <local-old> <new> --update-snapshot` compares and
  reports first, then atomically replaces the local OLD contract with the raw NEW entry document
  only when the policy passes. Fetch, parse, reference, report, and policy failures leave the
  snapshot byte-safe. `--fail-on none` is the explicit way to accept findings before advancing.
- **Explicit insecure TLS opt-in.** Trusted internal/test endpoints with self-signed or
  hostname-mismatched certificates can use `--insecure-tls` or
  `remoteReferences.insecureTls: true`.
- **Consistent remote policy flags.** `validate`, `compare`, and Bruno support the same independent
  `--allow-remote`, `--allow-private-network`, and `--insecure-tls` controls.
- **Reliable Windows packaging.** `build/release.ps1 -Version 1.2.0` now produces all six
  self-contained RID archives, a NuGet tool package, and one `SHA256SUMS` file.

## Remote-source security

TLS certificate verification remains enabled by default. Insecure TLS requires explicit opt-in and
is appropriate only for trusted internal/test endpoints; trusted certificates remain recommended for
production. It does not enable remote access, private-network access, HTTP, host-allowlist bypasses,
or redirect bypasses. Redirects and remote sibling `$ref` documents remain guarded by the existing
SSRF and host policy.

For a private HTTPS endpoint using a self-signed certificate, provide all required independent flags:

```text
--allow-remote --allow-private-network --insecure-tls
```

## Upgrade notes

No existing remote policy is relaxed. `insecureTls` defaults to `false`, and Bruno bootstrap writes
`insecureTls: true` only when the user supplied `--insecure-tls` explicitly on that first run.

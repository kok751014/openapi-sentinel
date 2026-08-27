# Security

OpenAPI specification files are **untrusted input**. The tool is designed so that analyzing a
hostile document cannot harm the machine running it, and so that network features cannot be
abused to reach places you did not intend.

## Threat model & mitigations

| Threat | Mitigation |
|--------|------------|
| YAML bombs / alias explosions | alias-use caps and anchor limits during parse; node-count budget |
| Excessive nesting | JSON `MaxDepth` enforced; depth guards in schema comparison (48) and ref chains (64) |
| Reference bombs ($ref expansion) | hard cap on resolved references (200k) plus node budget during materialization |
| Billion-laughs style entity tricks | YamlDotNet representation model without custom type resolution; no runtime polymorphic deserialization |
| Path traversal via external $ref (`../../etc/passwd`) | external file references must stay inside the root document's directory tree by default; absolute-path escapes rejected even when `allowAboveDocumentTree` is enabled |
| Remote sibling `$ref`s of URL-loaded documents | resolved against the document's own URL; each fetch re-passes the scheme gate, host allowlist, SSRF DNS guard, size and timeout caps |
| Unsafe URI schemes (`file://`) in remote docs | only http(s) allowed for remote documents; everything else rejected explicitly |
| SSRF via remote refs/URL specs | disabled by default. When enabled: HTTPS enforced (HTTP opt-in), DNS-resolved private/loopback/link-local/CGNAT ranges blocked unless explicitly permitted, per-hop revalidation across redirects (≤5), host allowlist support, timeout + response-size caps |
| Oversized inputs | 20 MB default input cap, bounded read loops on network streams |
| Code execution from spec content | the tool never evaluates expressions, scripts or custom tags; all parsing is data-only |
| Secret leakage | URL auth headers resolve from environment variables at fetch time and are never logged |

## Remote references policy

```yaml
remoteReferences:
  enabled: false            # off by default — local analysis stays fully offline
  allowHttp: false          # HTTPS only unless explicitly relaxed
  allowPrivateNetwork: false
  insecureTls: false          # dangerous explicit opt-in; default .NET TLS validation otherwise applies
  allowedHosts: []          # empty = any public host; list hostnames for strict control
  timeoutSeconds: 30
  maxResponseBytes: 20971520
```

`--allow-remote` on the CLI is equivalent to `enabled: true`. Cyclic reference graphs are
detected and replaced with deterministic marker nodes instead of recursing forever.

`--insecure-tls` (or `insecureTls: true`) is only for explicitly trusted internal/test HTTPS
endpoints with self-signed or mismatched certificates. It is default-off and does not enable
remote loading, private-network access, HTTP, host allowlist bypasses, or redirect bypasses.
Every redirect and remote sibling `$ref` remains subject to the same SSRF and host policy.

## Dependency posture

The entire product depends on exactly one third-party runtime library: **YamlDotNet (MIT)**.
Everything else is the BCL. See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

Dependency review (2026-08-24, `dotnet list package --vulnerable/--deprecated
--include-transitive` against the NuGet.org advisory database): **zero vulnerable and zero
deprecated packages** across all projects; YamlDotNet 18.1.0 is the current upstream release.
Re-run periodically as part of release checks.

## Reporting vulnerabilities

See SECURITY contact section in the repository root when published; until then, open a
private advisory with reproduction steps.

# Security Policy

OpenAPI Sentinel treats specification files as untrusted input. See
[docs/security.md](docs/security.md) for the threat model and mitigations
(YAML bombs, reference bombs, path traversal, SSRF guards, processing limits).

## Supported versions

| Version | Supported |
|---------|-----------|
| 0.9.x   | ✅ |

## Reporting a vulnerability

Please open a [private security advisory](https://github.com/kok751014/openapi-sentinel/security/advisories/new)
rather than a public issue. Include:

- the spec input (or minimal reproduction) that triggers the problem,
- the command line used,
- observed vs expected behavior.

We aim to acknowledge reports within 7 days and will credit reporters in the
release notes unless anonymity is requested.

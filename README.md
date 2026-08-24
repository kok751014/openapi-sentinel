# OpenAPI Sentinel

[![CI](https://github.com/kok751014/openapi-sentinel/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/kok751014/openapi-sentinel/actions/workflows/ci.yml)

**Catch breaking API contract changes before they reach production.**

OpenAPI Sentinel compares two OpenAPI contracts, understands the *semantics* of what changed,
classifies each change as breaking or safe with a direction-aware model (requests ≠ responses),
explains why it matters, generates professional reports, and fails CI when your policy threshold
is exceeded.

```
$ openapi-sentinel compare v1.yaml v2-breaking.yaml --fail-on high

────────────────────────────────────────────────────────────────────────
OpenAPI Sentinel  1.0.0
  samples/basic/v1.yaml  →  samples/basic/v2-breaking.yaml   (generated 2026-08-23 14:30 UTC)
  versions: 3.0.3  →  3.0.3
────────────────────────────────────────────────────────────────────────

Result: FAILED


Summary
  Findings: 33
  Severity: critical: 1 high: 6 medium: 9 low: 10 info: 7 verify: 0
  Compatibility: 7 breaking, 9 potentially breaking, 15 non-breaking, 2 informational, 0 unknown
  Recommended version bump: MAJOR — 7 breaking finding(s) at high or critical severity (first: CG2002) require a major version.

Findings
[HIGH] CG2002  GET /orders → parameters.channel
        Parameter 'channel' was added to GET /orders as required.
```

## Why not just diff the JSON?

A text diff tells you `required: false` became `required: true`. OpenAPI Sentinel tells you:

- **What changed** — request parameter `customerId` is now mandatory.
- **Why it matters** — existing clients omitting it will receive validation errors.
- **Who is affected** — every caller of `POST /orders`.
- **What to do** — introduce it as optional first, migrate consumers, enforce later.
- **Which release** — MAJOR, MINOR, PATCH or NONE.

## Highlights

| | |
|---|---|
| **80 rules + 3 engine checks** | Endpoints, parameters, request bodies, responses (incl. header payloads), schemas, security, callbacks & webhooks, metadata (`CGxxxx` IDs) |
| **Direction-aware** | Request tightening breaks clients; response widening is classified differently — see [compatibility model](docs/compatibility-model.md) |
| **2.0 → 3.2** | Swagger/OpenAPI 2.0, 3.0.x, 3.1.x and 3.2.x, JSON & YAML |
| **Multi-file specs** | Internal, sibling-file and remote references resolved safely (validated against GitHub-, Stripe-, DigitalOcean-, Kubernetes- and Azure-scale specifications) |
| **Offline & private** | No telemetry, no uploads, no AI, no cloud. Contracts never leave your machine |
| **CI-native** | Stable exit codes, SARIF/JUnit output, GitHub annotations, baselines and expiring suppressions |
| **Professional reports** | Console, JSON (versioned schema), Markdown (+PR summary), single-file HTML with filters & search |

## Quick start

```bash
# Install as a .NET global tool (after publishing/packing, see Installation)
dotnet tool install --global OpenApiSentinel

openapi-sentinel compare old.yaml new.yaml
openapi-sentinel compare git:origin/main:openapi.yaml openapi.yaml --fail-on high
openapi-sentinel explain CG2003
openapi-sentinel rules
```

Inputs can be local files, `http(s)` URLs, `-` (stdin), or `git:<rev>:<path>`.

### Configuration

`openapi-sentinel.yml` next to your project:

```yaml
failOn: high

rules:
  CG1003: info          # endpoint added → informational only

ignore:
  paths:
    - /internal/**

suppressions:
  - rule: CG1001
    path: "/legacy/**"
    reason: "Legacy mobile client migration"
    expires: 2026-12-31

remoteReferences:
  enabled: false        # remote $ref fetching stays off unless you opt in
```

See [docs/configuration.md](docs/configuration.md).

## Example output

Real generated artifacts from the sample contracts live in [docs/examples/](docs/examples/):
the full Markdown report, the compact PR summary, and a generated API changelog.

## Example: HTML report

The HTML report is a single portable file — embedded CSS/JS, no CDN, works offline.
It includes severity/compatibility filters, free-text search, collapsible details,
suppressed findings and the recommended version bump.

```bash
openapi-sentinel compare old.yaml new.yaml --html report.html
```

## CI example (GitHub Actions)

```yaml
- uses: ./action                      # in-repo composite (Marketplace listing pending)
  with:
    base: git:origin/main:openapi.yaml
    head: openapi.yaml
    fail-on: high
```

Outputs `breaking-count`, `high-count`, `medium-count`, `recommended-version`, `report-path`,
plus `::error::` annotations for breaking findings. Azure DevOps, GitLab and generic shell
examples live in [samples/](samples/) and [docs/](docs/).

Exit codes: `0` passed · `1` policy violated · `2` invalid spec · `3` invalid config ·
`4` network failure · `5` internal error.

## Privacy & offline operation

OpenAPI Sentinel processes contracts **locally**. There is no telemetry, no analytics,
no account, no API upload, no source-code upload and no hosted component. Remote fetches
(URL specs / remote `$ref`) happen only when you explicitly enable them, are HTTPS-only by
default, and are guarded against SSRF (private networks blocked, host allowlists, size/time
limits). See [docs/security.md](docs/security.md) and [docs/privacy.md](docs/privacy.md).

## Supported versions

| Contract version | JSON | YAML | Notes |
|------------------|------|------|-------|
| Swagger 2.0      | ✅   | ✅   | body/formData normalized to 3.x requestBody |
| OpenAPI 3.0.x    | ✅   | ✅   | `nullable` sibling semantics |
| OpenAPI 3.1.x    | ✅   | ✅   | JSON Schema `type: [t, "null"]` dialect |
| OpenAPI 3.2.x    | ✅   | ✅   | treated with 3.1+ semantics |

## Installation

```bash
# .NET global tool
dotnet tool install --global OpenApiSentinel

# From a release archive (self-contained, no .NET needed)
# see dist/<version>/ in a release or build one:
./build/release.sh 1.0.0

# Docker
docker run --rm -v "$PWD:/workspace" openapi-sentinel \
  compare /workspace/old.yaml /workspace/new.yaml
```

## Documentation

- [Getting started](docs/getting-started.md)
- [CLI reference](docs/cli-reference.md) · exit codes
- [Configuration](docs/configuration.md)
- [Rule catalog](docs/rules.md) — all rules, generated from the registry
- [Compatibility model](docs/compatibility-model.md)
- [Architecture overview](docs/architecture/overview.md)
- [Example outputs](docs/examples/)
- [Benchmarks](docs/benchmarks.md)
- Security: [security](docs/security.md) · [privacy](docs/privacy.md)
- [Troubleshooting](docs/troubleshooting.md)
- CI: [GitHub Actions](docs/github-actions.md) · [Azure DevOps](docs/azure-devops.md) · [GitLab](docs/gitlab.md)
- Commercial: [positioning](docs/commercial/product-positioning.md) · [feature matrix](docs/commercial/feature-matrix.md) · [pricing input](docs/commercial/pricing-strategy.md) · [release checklist](docs/commercial/release-checklist.md)
- [Engineering report](docs/engineering-report.md) — consolidated status snapshot
- [Architecture decisions](docs/DECISIONS.md) · [third-party notices](THIRD-PARTY-NOTICES.md)

## Status

Production-track v1 under active development. The engine, CLI and reporters are complete and
covered by 100+ tests; packaging and docs are being polished. See [progress notes](progress.md).

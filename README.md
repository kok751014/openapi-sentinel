# OpenAPI Sentinel

[![CI](https://github.com/kok751014/openapi-sentinel/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/kok751014/openapi-sentinel/actions/workflows/ci.yml)
[![Latest Release](https://img.shields.io/github/v/release/kok751014/openapi-sentinel?display_name=tag)](https://github.com/kok751014/openapi-sentinel/releases/latest)

**Catch breaking API contract changes before they reach production.**

OpenAPI Sentinel compares two OpenAPI contracts, understands the **semantics** of what changed,
classifies each change as breaking or safe with a direction-aware model
(**requests are not responses**), explains why it matters, generates professional reports,
and fails CI when your policy threshold is exceeded.

```text
$ openapi-sentinel compare v1.yaml v2-breaking.yaml --fail-on high

────────────────────────────────────────────────────────────────────────
OpenAPI Sentinel  1.0.1
  samples/basic/v1.yaml  →  samples/basic/v2-breaking.yaml
  versions: 3.0.3  →  3.0.3
────────────────────────────────────────────────────────────────────────

Result: FAILED

Summary
  Findings: 33
  Severity: critical: 1 high: 6 medium: 9 low: 10 info: 7 verify: 0
  Compatibility: 7 breaking, 9 potentially breaking, 15 non-breaking,
                 2 informational, 0 unknown
  Recommended version bump: MAJOR

Findings
[HIGH] CG2002  GET /orders → parameters.channel
        Parameter 'channel' was added to GET /orders as required.
```

## Why not just diff the JSON?

A text diff tells you that `required: false` became `required: true`.

OpenAPI Sentinel tells you:

- **What changed** — request parameter `customerId` is now mandatory.
- **Why it matters** — existing clients that omit it may fail validation.
- **Who is affected** — callers of the affected operation.
- **What to do** — introduce it safely, migrate consumers, then enforce it.
- **Which release** — MAJOR, MINOR, PATCH, or NONE.

## Highlights

| Capability | What it gives you |
|---|---|
| **85 rules + 3 engine checks** | Endpoints, parameters, request bodies, responses, schemas, security, callbacks, webhooks, and metadata using stable `CGxxxx` IDs |
| **Direction-aware compatibility** | Request tightening and response widening are not treated as the same kind of change |
| **Swagger/OpenAPI 2.0 → 3.2** | Swagger/OpenAPI 2.0, 3.0.x, 3.1.x, and 3.2.x in JSON or YAML |
| **Multi-file specifications** | Internal, sibling-file, and controlled remote references |
| **Offline & private by default** | No telemetry, analytics, AI service, account, or hosted backend is required for core analysis |
| **CI-native** | Stable exit codes, SARIF/JUnit, baselines, suppressions, and machine-readable output |
| **Professional reports** | Console, JSON, Markdown, PR summary, and single-file HTML with filtering and search |
| **Release guidance** | Semantic version recommendation and generated API changelog support |
| **Bruno collection sync** | One-click bootstrap (`bruno sync --source …`) creates config, layout and a Bruno folder workspace automatically; every output is validated against the official OpenCollection v1.0 schema before being written; incremental sync preserves manual values/scripts/tests/docs/auth. Experimental Traditional-Chinese generated scenarios are opt-in for v1.1.0. |

See the [compatibility model](docs/compatibility-model.md) and
[rule catalog](docs/rules.md) for the detailed semantics.

---

## Quick start

### 1. Download

Download the self-contained binary for your platform from the
[latest GitHub Release](https://github.com/kok751014/openapi-sentinel/releases/latest).

Supported release targets:

- Windows x64
- Windows ARM64
- Linux x64
- Linux ARM64
- macOS x64
- macOS ARM64

The self-contained release archives do **not** require a preinstalled .NET runtime.

### 2. Compare two contracts

```bash
openapi-sentinel compare old.yaml new.yaml
```

Fail CI when a High-or-greater finding is detected:

```bash
openapi-sentinel compare old.yaml new.yaml --fail-on high
```

Other useful commands:

```bash
openapi-sentinel compare git:origin/main:openapi.yaml openapi.yaml --fail-on high
openapi-sentinel explain CG2003
openapi-sentinel rules
openapi-sentinel --version
```

Inputs can be:

- local files
- `http(s)` URLs when remote access is explicitly enabled
- `-` for stdin
- `git:<rev>:<path>` for Git-reference workflows

---

## Installation

### Self-contained release archive

Download the archive for your platform from the
[latest release](https://github.com/kok751014/openapi-sentinel/releases/latest),
extract it, and place the executable somewhere on your `PATH`.

Example on Linux/macOS:

```bash
tar -xzf openapi-sentinel-1.0.1-linux-x64.tar.gz
chmod +x openapi-sentinel
./openapi-sentinel --version
```

Example on Windows PowerShell after extracting the ZIP:

```powershell
.\openapi-sentinel.exe --version
```

### .NET global tool package

Each GitHub Release includes a `.nupkg` package asset.

Public NuGet.org distribution is a separate distribution step. Until that publication is enabled,
use the attached package directly if you want to test the global-tool packaging.

Example with a local package directory:

```bash
dotnet tool install --global OpenApiSentinel \
  --version 1.0.1 \
  --add-source ./path/to/package-directory
```

### Docker

The Dockerfile is validated by GitHub Actions.

Until a public container registry image is published, build it locally:

```bash
docker build -t openapi-sentinel .
```

Then run:

```bash
docker run --rm -v "$PWD:/workspace" openapi-sentinel \
  compare /workspace/old.yaml /workspace/new.yaml --fail-on high
```

---

## Configuration

Create `openapi-sentinel.yml` next to your project:

```yaml
failOn: high

rules:
  CG1003: info

ignore:
  paths:
    - /internal/**

suppressions:
  - rule: CG1001
    path: "/legacy/**"
    reason: "Legacy mobile client migration"
    expires: 2026-12-31

remoteReferences:
  enabled: false
```

Remote reference fetching remains off unless you explicitly enable it.

See [docs/configuration.md](docs/configuration.md) for the full configuration model.

---

## Reports

OpenAPI Sentinel supports multiple output formats for humans, automation, and CI systems.

### Console

```bash
openapi-sentinel compare old.yaml new.yaml
```

### JSON

```bash
openapi-sentinel compare old.yaml new.yaml --json report.json
```

### Markdown

```bash
openapi-sentinel compare old.yaml new.yaml --markdown report.md
```

### HTML

```bash
openapi-sentinel compare old.yaml new.yaml --html report.html
```

The HTML report is a single portable file with embedded CSS/JS, no required CDN, and works offline.
It includes severity/compatibility filters, text search, collapsible details, suppressed findings,
affected endpoints, rule IDs, remediation guidance, and semantic-version recommendations.

### SARIF / JUnit

Use these formats when integrating with code scanning or CI test/report consumers.
See the [CLI reference](docs/cli-reference.md) for the exact options.

Real generated examples are available in [docs/examples/](docs/examples/).

---

## CI/CD

OpenAPI Sentinel uses stable process exit codes so CI can make policy decisions without parsing
human-readable console text.

| Exit code | Meaning |
|---:|---|
| `0` | Passed |
| `1` | Policy threshold violated |
| `2` | Invalid OpenAPI specification |
| `3` | Invalid configuration |
| `4` | Network failure |
| `5` | Internal error |

A simple shell CI step:

```bash
openapi-sentinel compare baseline.yaml candidate.yaml \
  --fail-on high \
  --json artifacts/api-diff.json \
  --html artifacts/api-diff.html
```

Repository integration examples are included for:

- [GitHub Actions](docs/github-actions.md)
- [Azure DevOps](docs/azure-devops.md)
- [GitLab CI](docs/gitlab.md)
- generic shell / PowerShell workflows

A GitHub Marketplace listing is planned separately.

---

## Compatibility model

OpenAPI compatibility is directional.

Examples:

- making a request field required can break existing clients;
- removing a required response field can break consumers;
- widening or narrowing schemas is interpreted differently depending on whether data flows
  **into** or **out of** the API;
- security requirement changes can affect callers even when endpoint paths stay identical.

This is why OpenAPI Sentinel does not reduce compatibility analysis to a text diff.

Read [docs/compatibility-model.md](docs/compatibility-model.md) for the detailed model.

---

## Supported versions

| Contract version | JSON | YAML | Notes |
|---|:---:|:---:|---|
| Swagger 2.0 | ✅ | ✅ | Body/formData normalized into the internal model |
| OpenAPI 3.0.x | ✅ | ✅ | 3.0 nullability semantics |
| OpenAPI 3.1.x | ✅ | ✅ | JSON Schema-style nullable/type-array semantics |
| OpenAPI 3.2.x | ✅ | ✅ | 3.1+ compatibility semantics where applicable |

---

## Privacy & offline operation

OpenAPI Sentinel is designed so the core comparison workflow runs locally.

There is:

- no telemetry required by the product;
- no analytics service required;
- no account required;
- no API-spec upload required;
- no source-code upload required;
- no hosted comparison backend required;
- no AI model required for compatibility analysis.

Remote URL specifications and remote `$ref` resolution are opt-in and governed by explicit
network/security controls, including protections described in the security documentation.

See:

- [Security](docs/security.md)
- [Privacy](docs/privacy.md)
- [SECURITY.md](SECURITY.md)

---

## Security model

The project includes protections and review coverage for areas such as:

- SSRF-aware remote retrieval
- explicit remote-reference controls
- path handling
- cyclic references
- bounded handling of large/deep inputs
- HTML output escaping
- secret-safe logging expectations
- dependency security review
- non-root Docker execution

For details and limitations, read [docs/security.md](docs/security.md).

---

## Release verification

GitHub Release assets include a `SHA256SUMS` file.

Example:

```bash
sha256sum -c SHA256SUMS
```

The release pipeline validates:

- Ubuntu build/test
- Windows build/test
- macOS build/test
- sample scenarios
- .NET tool packaging
- release artifact generation
- checksums
- Docker image build
- GitHub Release publication

---

## Current status

**OpenAPI Sentinel 1.1.0.** See [RELEASE_NOTES.md](RELEASE_NOTES.md)
and the [CHANGELOG](CHANGELOG.md) for release details.

Current verified quality state:

- `openapi-sentinel version` reports **1.1.0**
- **551/551 automated tests passing**; Release build succeeds
- `git diff --check` passes

Current distribution work that is intentionally separate from the GitHub Release:

- NuGet.org publication
- public container registry publication
- GitHub Marketplace listing

---

## Documentation

- [Changelog](CHANGELOG.md)
- [Getting started](docs/getting-started.md)
- [CLI reference](docs/cli-reference.md)
- [Configuration](docs/configuration.md)
- [Rule catalog](docs/rules.md)
- [Compatibility model](docs/compatibility-model.md)
- [Bruno sync](docs/bruno-sync.md)
- [Bruno scenarios (zh-TW)](docs/bruno-scenarios.md)
- [Visual Studio / MSBuild integration](docs/visual-studio-integration.md)
- [Architecture overview](docs/architecture/overview.md)
- [Example outputs](docs/examples/)
- [Benchmarks](docs/benchmarks.md)
- [Security](docs/security.md)
- [Privacy](docs/privacy.md)
- [Troubleshooting](docs/troubleshooting.md)
- [GitHub Actions](docs/github-actions.md)
- [Azure DevOps](docs/azure-devops.md)
- [GitLab](docs/gitlab.md)
- [Commercial positioning](docs/commercial/product-positioning.md)
- [Community / Pro feature matrix](docs/commercial/feature-matrix.md)
- [Engineering report](docs/engineering-report.md)
- [Architecture decisions](docs/DECISIONS.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)

---

## Commercial direction

OpenAPI Sentinel is designed so correctness and core compatibility analysis remain trustworthy
regardless of distribution or future commercial packaging.

The repository includes product-positioning and Community/Pro boundary documentation to keep
commercial features separate from the correctness of the compatibility engine.

See:

- [Product positioning](docs/commercial/product-positioning.md)
- [Community / Pro feature matrix](docs/commercial/feature-matrix.md)

---

## Build from source

Requires the .NET 10 SDK.

```bash
git clone https://github.com/kok751014/openapi-sentinel.git
cd openapi-sentinel

dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Build release artifacts:

```bash
./build/release.sh 1.0.1
```

The generated packages are written under `dist/1.0.1/`.

---

## License

See [LICENSE](LICENSE).

Third-party dependency notices are documented in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## Security issues

Please follow the guidance in [SECURITY.md](SECURITY.md) when reporting security-sensitive issues.

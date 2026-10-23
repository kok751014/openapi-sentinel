# OpenAPI Sentinel v1.3.2 release notes

## Highlights

- OpenAPI Sentinel is now open source under the Apache License 2.0, replacing the draft proprietary
  terms. Earlier releases (v0.9.0–v1.3.1) are also made available under Apache-2.0.
- Every release archive, the .NET tool package and the Docker image now ship `LICENSE`, `NOTICE` and
  `THIRD-PARTY-NOTICES.md`. Self-contained archives and the Docker image also include the .NET runtime
  license and third-party notices under `licenses/`.
- THIRD-PARTY-NOTICES.md now covers every bundled component — YamlDotNet, the embedded OpenCollection
  schema and the .NET runtime — with full license texts.
- The NuGet package declares `Apache-2.0` and links to the GitHub repository; the Docker image carries
  OCI license and source labels.
- The Docker image builds again: the Dockerfile now includes the Bruno project the CLI depends on.

## Upgrade notes

No configuration migration is required. This release changes licensing, packaging and documentation
only: comparison, rules, policy evaluation, every report format, baselines, suppressions, snapshots and
Bruno sync behave exactly as in v1.3.1.

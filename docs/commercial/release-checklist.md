# Release checklist

Pre-flight for publishing version X.Y.Z of OpenAPI Sentinel.

## Code & quality

- [ ] `dotnet build -c Release` → 0 errors, 0 warnings (warnings-as-errors enforced)
- [ ] `dotnet test -c Release` → all tests green on Linux, Windows, macOS
- [ ] No `TODO`/placeholder strings in shipped assemblies (`grep -r "TODO" src/ --include=*.cs`)
- [ ] Self-review pass completed (master spec §57 checklist) with fixes applied

## Version consistency

- [ ] `SentinelVersion` property set (release script `/p:SentinelVersion=` does this everywhere)
- [ ] `openapi-sentinel version` reports X.Y.Z
- [ ] Assembly + NuGet package + HTML/JSON reports all show the same version string
- [ ] JSON `schemaVersion` reviewed: any field removals/renames require a schema bump

## Rules & docs

- [ ] docs/rules.md regenerated from the registry (no manual edits)
- [ ] New/changed rules have description, impact, remediation, examples, limitations
- [ ] README quick start verified verbatim on a clean machine/container

## Packaging

- [ ] `./build/release.sh <version>` produces all six RID archives + SHA256SUMS + nupkg
- [ ] Spot-check: linux-x64 and win-x64 archives run self-contained on clean machines
      (`--version`, `compare` on samples/basic)
- [ ] Docker image builds; non-root user can write mounted /workspace reports
- [ ] `dotnet tool install -g` from packed .nupkg works; tool command name resolves

## Release notes

- [ ] RELEASE_NOTES.md template replaced with real highlights
- [ ] Breaking changes to *this tool's* CLI/output called out explicitly

## Post-release

- [ ] Tag `vX.Y.Z`; artifacts attached to the release
- [ ] NuGet push succeeded; `dotnet tool update` path verified
- [ ] Docs site/readme badges updated if applicable

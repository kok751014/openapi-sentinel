# OpenAPI Sentinel — Codex Handoff Checkpoint

Date: 2026-08-26

## Status

This is a WIP checkpoint, not a release-ready state.

Ox Alpha development stopped because the free usage period ended.

Current automated test result:

- Total: 539
- Passed: 537
- Failed: 2
- Skipped: 0

`git diff --check` is clean.

Previous baseline was 501 passing tests, so substantial new implementation and regression coverage has been added.

## Safety Rules

Preserve all current work.

Do NOT:

- reset
- restore
- clean
- stash
- discard existing changes
- rewrite existing progress
- create the v1.1.0 tag

Inspect the current implementation before modifying it.

## Major Work Added / In Progress

Current working tree includes release-blocker implementation for:

- Bruno native `.bru` workspace support
- native workspace reader/writer under `src/OpenApiSentinel.Bruno/Bru/`
- `CanonicalBodyFactory.cs`
- `ResponseExampleFactory.cs`
- multipart/form-data generation
- numeric enum invalid-value handling
- UUID / formatted-string scenario cleanup
- canonical request-body consistency
- response example generation
- Bruno bootstrap workflow
- native workspace preservation / round-trip behavior
- release-blocker regression tests

## Known Remaining Failures

### 1. Native workspace round-trip

Test:

`OpenApiSentinel.Bruno.Tests.BruWorkspaceRoundTripTests.Native_Workspace_RoundTrips_Through_Reader_Preserving_Manual_Content`

Current error:

`System.InvalidOperationException: Sequence contains no matching element`

Location:

`tests/OpenApiSentinel.Bruno.Tests/BruWorkspaceRoundTripTests.cs:85`

Investigate native `.bru` request reconstruction, identity/path matching, reader behavior, and preservation of manual content.

### 2. Bundled/native semantic parity

Test:

`OpenApiSentinel.Bruno.Tests.BruWorkspaceRoundTripTests.Bundled_And_Native_Outputs_Semantically_Match`

Current error:

`System.NullReferenceException`

Location:

`tests/OpenApiSentinel.Bruno.Tests/BruWorkspaceRoundTripTests.cs:120`

Investigate missing reconstructed native request/body/example data and semantic normalization between bundled `collection.yml` and native `.bru` output.

## Codex First Task

Reproduce and fix the two failing `BruWorkspaceRoundTripTests`.

Prefer fixing implementation rather than weakening/removing regression tests unless a test is demonstrably inconsistent with Bruno's official native format.

Then run:

`dotnet test -c Release --no-restore`

Target:

- 539+ tests
- 0 failed

Then run:

`git diff --check`

It must produce no output.

## Manual Validation Still Required

After automated tests pass, do NOT immediately release.

Real API test project:

`D:\Project\ssz-ams-api\ssz-ams-api`

Swagger test file:

`D:\Project\ssz-ams-api\swagger-test.json`

Validate:

1. `bruno sync --source ...` succeeds
2. native Bruno workspace opens directly and contains generated requests
3. Animals multipart POST/PUT requests contain correct form bodies
4. numeric enum invalid examples remain numeric
5. UUID scenarios are meaningful and non-redundant
6. default body and full-success example use the same canonical model
7. response examples are generated when OpenAPI provides enough information
8. filenames use summary / operationId / method-path fallback and do not absorb description HTML
9. second `bruno sync` works without `--source`
10. `--version` reports the intended v1.1 version

Do not create the `v1.1.0` tag until final manual validation and release prerequisites are complete.

# Contributing to OpenAPI Sentinel

Thanks for helping make API contract changes safer. Bug reports, false-positive/false-negative
reports, documentation fixes and pull requests are all welcome.

## Reporting issues

- **Bugs and misclassified changes:** open a GitHub issue with the two contract versions (or a
  minimal reproduction), the command line used, and observed vs expected findings.
- **Security issues:** do not open a public issue; follow [SECURITY.md](SECURITY.md).

## Development setup

Requires the .NET 10 SDK.

```bash
dotnet build -c Release    # 0 errors, 0 warnings expected (warnings are errors)
dotnet test -c Release     # all test projects
```

Run the CLI from the build output:

```bash
dotnet src/OpenApiSentinel.Cli/bin/Release/net10.0/OpenApiSentinel.Cli.dll \
  compare samples/basic/v1.yaml samples/basic/v2-breaking.yaml --fail-on high --no-color
```

## Pull requests

- Keep each pull request focused on one change.
- Add or update tests for behavior changes; rule changes need fixture coverage for both
  directions (request and response) where direction matters.
- After adding or changing rules, regenerate the rule catalog with
  `python3 build/gen-rules-doc.py` — a drift-guard test fails if docs/rules.md is stale.
- Record user-visible changes under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md).
- Do not add runtime dependencies without discussion; the runtime dependency surface is
  intentionally limited to YamlDotNet (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)).

## License

OpenAPI Sentinel is licensed under the [Apache License 2.0](LICENSE). Unless you explicitly state
otherwise, any contribution you submit for inclusion is licensed under the same terms, as
described in section 5 of the license.

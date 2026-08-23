# Third-party notices

OpenAPI Sentinel is original code. Its runtime dependency surface is deliberately minimal:

## Runtime dependencies

| Component | License | Usage |
|-----------|---------|-------|
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | MIT | YAML parsing to a representation model (no custom type resolution, data-only) |

YamlDotNet's MIT license permits commercial use and redistribution; its license text must
accompany distributions that include the library (the NuGet packaging flow carries it).

## Toolchain (build/test time only — not distributed)

- .NET SDK / runtime (MIT-licensed components)
- xUnit, Microsoft.NET.Test.Sdk, coverlet (test projects only)

## Attribution

This project does not copy code from other OpenAPI diff tooling; the parser, normalizer,
comparison engine, rule set and reporters are original implementations. Rule identifiers in
the `CGxxxx` namespace are specific to OpenAPI Sentinel.

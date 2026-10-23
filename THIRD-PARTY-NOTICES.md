# Third-party notices

OpenAPI Sentinel is licensed under the [Apache License 2.0](LICENSE) (see also [NOTICE](NOTICE)).
Its runtime dependency surface is deliberately minimal; the components below are bundled in
release artifacts and remain under their own licenses.

## Bundled components

| Component | License | Shipped in | Usage |
|-----------|---------|------------|-------|
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) 18.1.0 | MIT | all binaries, .NET tool package, Docker image | YAML parsing to a representation model (no custom type resolution, data-only) |
| [OpenCollection v1.0 JSON Schema](https://github.com/opencollection-dev/opencollection/blob/main/packages/oc-schema/src/opencollection.schema.json) | MIT | embedded resource in `OpenApiSentinel.Bruno` (all binaries) | Validates generated Bruno collections before they are written (pinned copy, see `src/OpenApiSentinel.Bruno/Validation/`) |
| [.NET runtime](https://github.com/dotnet/runtime) | MIT | self-contained release archives, Docker image | Runtime for self-contained executables |

Every release archive, the .NET tool package and the Docker image ship `LICENSE`, `NOTICE` and
this file next to the executable. Self-contained archives and the Docker image additionally ship
the .NET runtime's own `licenses/dotnet-runtime-LICENSE.TXT` and
`licenses/dotnet-runtime-THIRD-PARTY-NOTICES.TXT`, copied from the runtime pack at publish time.

The Docker image is based on `mcr.microsoft.com/dotnet/runtime-deps`; the operating-system packages
in that base image are distributed under their own licenses as documented by the image publisher.

## Toolchain (build/test time only — not distributed)

- .NET SDK (MIT-licensed components)
- xUnit and xunit.runner.visualstudio (Apache-2.0), Microsoft.NET.Test.Sdk (MIT),
  coverlet.collector (MIT) — test projects only

## Attribution

This project does not copy code from other OpenAPI diff tooling; the parser, normalizer,
comparison engine, rule set and reporters are original implementations. Rule identifiers in
the `CGxxxx` namespace are specific to OpenAPI Sentinel.

## License texts

### YamlDotNet

```text
Copyright (c) 2008, 2009, 2010, 2011, 2012, 2013, 2014 Antoine Aubry and contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### OpenCollection v1.0 JSON Schema

The upstream repository does not include a license file. The license is declared as `MIT` in the
metadata of the `@opencollection/schema` package, whose declared author is Bruno Software Inc.
The standard MIT terms are reproduced below.

```text
Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### .NET runtime

Copyright (c) .NET Foundation and Contributors, MIT License. The full license and the runtime's
third-party notices ship as separate files in self-contained artifacts (see above) and are
available at <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT> and
<https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT>.

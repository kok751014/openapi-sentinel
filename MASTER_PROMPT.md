# Ox Alpha Master Prompt — OpenAPI Sentinel

# Mission: Build a Production-Ready Commercial OpenAPI Contract Diff Product

You are the principal software architect, senior .NET engineer, API standards specialist, security engineer, QA engineer, DevOps engineer, technical writer, and release engineer for this project.

Your task is NOT to create a demo, prototype, proof of concept, sample project, or minimal MVP.

Your task is to build a **production-ready commercial developer tool** that can realistically be packaged, released, marketed, and sold.

The product name is:

**OpenAPI Sentinel**

Repository name:

`openapi-sentinel`

Primary CLI command:

`openapi-sentinel`

The name should remain centralized and easy to rename later.

---

# 1. PRODUCT VISION

Build a professional OpenAPI contract comparison and API breaking-change detection tool.

Its core job is:

> Compare an old API contract against a new API contract, determine exactly what changed, classify compatibility impact, explain why it matters, generate professional reports, and optionally fail CI/CD when unacceptable API changes are detected.

Primary users:

- Backend engineers
- Frontend engineers
- API platform teams
- DevOps engineers
- QA engineers
- Enterprise development teams
- Teams using ASP.NET Core / Swagger
- Teams managing frontend/backend API contracts
- CI/CD pipelines

The product must provide substantially more value than a basic JSON diff.

It should understand API semantics.

Example:

Changing:

```yaml
required: false
```

to:

```yaml
required: true
```

for a request parameter must NOT merely be shown as:

```text
false -> true
```

It should be reported semantically, such as:

```text
BREAKING CHANGE

POST /api/orders

Request parameter "customerId" changed from optional to required.

Impact:
Existing clients that do not send customerId may begin receiving validation errors.

Suggested action:
Introduce the field as optional first, migrate consumers, then enforce the requirement in a later API version.
```

The product should feel like a professional API governance tool rather than a generic file comparison utility.

---

# 2. AUTONOMOUS WORKING MODE

Work autonomously.

Do NOT stop after:

- architecture analysis
- scaffolding
- TODO creation
- partial implementation
- happy-path implementation
- compiling successfully
- adding a few tests

Continue until the repository reaches the Definition of Done specified later in this prompt.

Do not repeatedly ask me what to do next.

When a reasonable engineering decision can be made safely:

1. choose the most maintainable production-ready option;
2. document the decision;
3. implement it;
4. test it;
5. continue.

Create:

`docs/DECISIONS.md`

for important architectural/product decisions.

If requirements conflict, prioritize in this order:

1. correctness
2. security
3. deterministic behavior
4. compatibility analysis accuracy
5. maintainability
6. CLI usability
7. performance
8. visual polish

Do not make destructive Git operations.

Do not delete unrelated existing work.

Never silently weaken tests merely to make them pass.

Never fake successful test or benchmark results.

If something cannot be completed, clearly document the exact blocker.

---

# 3. TECHNOLOGY

Use:

- C#
- .NET 10 LTS
- nullable reference types enabled
- implicit usings enabled
- warnings treated seriously
- cross-platform design

The core analysis engine MUST NOT depend on:

- a hosted AI model
- OpenAI
- Anthropic
- Gemini
- external SaaS
- cloud execution
- a database
- internet access

All core analysis must work completely offline and deterministically.

This is important because the eventual product should continue working even after the AI used to build it is no longer available.

---

# 4. SUPPORTED OPENAPI VERSIONS

Support, as realistically as possible:

- Swagger / OpenAPI 2.0
- OpenAPI 3.0.x
- OpenAPI 3.1.x
- OpenAPI 3.2.x

Both JSON and YAML must be supported.

OpenAPI 3.2 support is important and should be treated as a commercial differentiator.

Do not pretend that two different OpenAPI versions have identical semantics.

Where compatibility behavior differs between versions, normalize carefully and preserve necessary metadata.

---

# 5. SOLUTION ARCHITECTURE

Create a clean architecture.

A reasonable initial structure is:

```text
OpenApiSentinel.sln

src/
  OpenApiSentinel.Core/
  OpenApiSentinel.OpenApi/
  OpenApiSentinel.Rules/
  OpenApiSentinel.Reporting/
  OpenApiSentinel.Cli/

tests/
  OpenApiSentinel.Core.Tests/
  OpenApiSentinel.OpenApi.Tests/
  OpenApiSentinel.Rules.Tests/
  OpenApiSentinel.Reporting.Tests/
  OpenApiSentinel.Cli.Tests/
  OpenApiSentinel.IntegrationTests/

fixtures/
  openapi/
  breaking/
  non-breaking/
  invalid/
  references/
  security/
  large/

docs/
  architecture/
  rules/
  examples/
  commercial/
  DECISIONS.md

samples/
  basic/
  github-actions/
  azure-devops/
  gitlab/
```

You may improve this structure when justified.

Avoid unnecessary architecture ceremony.

Core domain logic should remain independent from CLI and rendering concerns.

---

# 6. INPUT SOURCES

The CLI must be able to compare API specifications from local files, HTTP/HTTPS URLs, stdin, and Git repository versions.

Examples:

```bash
openapi-sentinel compare old.json new.json
openapi-sentinel compare old.yaml new.yaml
```

URL example:

```bash
openapi-sentinel compare \
  https://example.com/api/v1/swagger.json \
  https://example.com/api/v2/swagger.json
```

Git example:

```bash
openapi-sentinel compare \
  --base git:origin/main:swagger.json \
  --head ./swagger.json
```

Handle:

- missing Git
- missing revision
- missing file
- renamed file
- invalid spec

gracefully.

---

# 7. $ref SUPPORT

Correct `$ref` handling is essential.

Support:

- internal references
- relative local file references
- recursive references
- cyclic schemas
- shared component schemas

Remote references require stricter security.

Remote `$ref` resolution should be disabled by default unless explicitly enabled.

When enabled:

- HTTPS should be preferred
- HTTP should require explicit opt-in
- enforce request timeout
- enforce maximum response size
- prevent infinite reference chains
- detect cyclic reference graphs
- provide host allowlisting
- protect against SSRF
- reject dangerous schemes such as file:// when processing remote documents
- do not automatically access localhost/private network addresses from remote references unless explicitly permitted

Never expose arbitrary host filesystem contents through reference resolution.

Add security tests for this functionality.

---

# 8. SEMANTIC NORMALIZATION

Build a normalized internal API model.

The diff engine must compare semantic API structures rather than relying on raw JSON/YAML text differences.

Normalization should account for:

- paths
- HTTP operations
- parameters
- request bodies
- responses
- schemas
- components
- security schemes
- security requirements
- media types
- headers
- callbacks where supported
- webhooks where supported
- discriminator
- enums
- nullable behavior
- examples
- defaults
- readOnly
- writeOnly
- deprecated
- tags
- servers
- operationId

Do not report meaningless changes caused only by:

- JSON property ordering
- YAML ordering
- whitespace
- formatting
- comments
- harmless representation differences

unless the difference is semantically meaningful.

---

# 9. DIRECTION-AWARE COMPATIBILITY ANALYSIS

This is one of the most important product features.

Do NOT treat request and response schema compatibility identically.

Understand the difference between what the server accepts and what the server can return.

A change may:

- break API consumers
- break API providers
- be backward compatible
- be forward compatible
- be risky but uncertain
- require human verification

Each finding should include compatibility direction when applicable.

Document the compatibility theory used by the tool.

Create:

`docs/compatibility-model.md`

with examples.

---

# 10. BREAKING CHANGE RULE ENGINE

Implement a reusable rule engine.

Rules must have stable IDs.

Example:

- CG1001
- CG1002
- CG1101

Do NOT bake all logic into one giant comparison method.

Each finding should contain at minimum:

- RuleId
- Title
- Severity
- Category
- ChangeType
- Path
- HttpMethod
- Location
- OldValue
- NewValue
- Explanation
- Impact
- SuggestedAction
- Confidence

Use structured values instead of strings where appropriate.

---

# 11. REQUIRED RULE COVERAGE

Implement comprehensive rules for at least the following categories.

## Endpoint changes

Detect:

- path removed
- path added
- HTTP method removed
- HTTP method added
- endpoint deprecated
- endpoint undeprecated
- operationId changed
- operationId removed
- operationId added

## Parameter changes

Detect:

- parameter added
- parameter removed
- optional parameter becomes required
- required parameter becomes optional
- parameter type changed
- parameter format changed
- enum value added
- enum value removed
- enum value changed
- default value changed
- minimum changed
- maximum changed
- minLength changed
- maxLength changed
- pattern changed
- nullable behavior changed
- array item definition changed
- minItems changed
- maxItems changed
- uniqueItems changed
- serialization style changed
- explode changed
- location changed: query, header, path, cookie

Treat path parameters correctly.

## Request body changes

Detect:

- request body added
- request body removed
- optional request body becomes required
- required request body becomes optional
- supported request content type removed
- supported request content type added
- request schema changed
- required property added
- required property removed
- optional property added
- property removed
- property renamed where confidently detectable
- schema type changed

Do not claim a rename unless confidence is high.

When uncertain, report remove + add or Verify.

## Response changes

Detect:

- response status code removed
- response status code added
- response schema changed
- response content type removed
- response content type added
- response header removed
- response header added
- required response property removed
- optional response property removed
- response property added
- enum changes
- nullability changes
- response type changes

Account for `default`, `2XX`, `4XX`, `5XX` patterns where supported.

## Schema changes

Detect changes to:

- type
- format
- properties
- required
- enum
- const
- minimum
- maximum
- exclusiveMinimum
- exclusiveMaximum
- multipleOf
- minLength
- maxLength
- pattern
- minItems
- maxItems
- uniqueItems
- minProperties
- maxProperties
- additionalProperties
- nullable
- readOnly
- writeOnly
- deprecated
- default
- example
- examples
- allOf
- oneOf
- anyOf
- not
- discriminator

Understand JSON Schema semantics for OpenAPI 3.1+.

Avoid blindly applying OpenAPI 3.0 rules to OpenAPI 3.1/3.2 JSON Schema dialect behavior.

---

# 12. AUTHENTICATION AND SECURITY RULES

Detect security-related API contract changes.

Examples:

- public endpoint -> authentication required
- authentication required -> public
- OAuth scope added
- OAuth scope removed
- security scheme removed
- security scheme added
- security scheme type changed
- API key name changed
- API key location changed
- Bearer format changed
- OpenID Connect URL changed
- OAuth authorization/token URL changed

Changes making authentication stricter should generally be treated as potentially breaking.

Explain the impact clearly.

---

# 13. SEVERITY MODEL

Create a useful severity model.

Suggested:

- Critical
- High
- Medium
- Low
- Info
- Verify

`Verify` should mean:

> The tool detected a potentially important compatibility change but cannot determine impact confidently enough to classify it automatically.

Do NOT abuse Verify as a replacement for implementing difficult logic.

Document the exact rules.

---

# 14. BREAKING / NON-BREAKING CLASSIFICATION

Severity and compatibility classification must be separate concepts.

Suggested compatibility classifications:

- Breaking
- PotentiallyBreaking
- NonBreaking
- Informational
- Unknown

Keep these concepts explicit.

---

# 15. HUMAN-READABLE EXPLANATIONS

Every important finding must explain:

- What changed?
- Why does it matter?
- Who might be affected?
- What should the developer do?

Avoid robotic output.

---

# 16. COMMAND-LINE EXPERIENCE

Design a professional CLI.

At minimum:

```bash
openapi-sentinel compare
openapi-sentinel validate
openapi-sentinel rules
openapi-sentinel explain
openapi-sentinel version
openapi-sentinel init
```

Examples:

```bash
openapi-sentinel compare old.json new.json

openapi-sentinel compare old.json new.json \
  --format console

openapi-sentinel compare old.json new.json \
  --format json

openapi-sentinel compare old.json new.json \
  --format markdown

openapi-sentinel compare old.json new.json \
  --format html

openapi-sentinel compare old.json new.json \
  --fail-on high
```

Support combined outputs where practical.

Provide `openapi-sentinel rules` to list all built-in rules.

Provide `openapi-sentinel explain CG1204` to explain a rule.

Provide `openapi-sentinel init` to generate an example configuration.

---

# 17. CONFIGURATION FILE

Support a repository configuration file such as:

`openapi-sentinel.yml`

Example concepts:

```yaml
failOn: high

rules:
  CG1001: error
  CG1205: warning
  CG3102: off

ignore:
  paths:
    - /internal/**

remoteReferences:
  enabled: false

output:
  markdown: artifacts/api-diff.md
  json: artifacts/api-diff.json
```

Validate configuration and produce actionable error messages.

Unknown settings should not silently disappear.

---

# 18. BASELINES AND SUPPRESSIONS

Commercial teams need a way to acknowledge known issues.

Implement suppressions.

Example:

```yaml
suppressions:
  - rule: CG1205
    path: /legacy/orders
    reason: "Legacy mobile client migration"
    expires: 2026-12-31
```

Requirements:

- suppression requires a reason
- optional expiration date
- expired suppression generates a warning
- suppressions are visible in reports
- do not silently hide everything

Support a baseline mechanism so teams can adopt the tool without fixing all historical problems immediately.

New changes after the baseline should still be detected.

---

# 19. SEMANTIC VERSION RECOMMENDATION

Generate an optional API version recommendation:

- MAJOR
- MINOR
- PATCH
- NONE

Provide machine-readable output.

---

# 20. CHANGELOG GENERATION

Generate an API-focused changelog in Markdown with Breaking and Non-breaking sections.

---

# 21. REPORTING

Implement multiple report formats.

Required:

- Console
- JSON
- Markdown
- HTML

Desirable:

- SARIF
- JUnit XML

Implement SARIF if feasible.

JSON output must have a documented schema and stable structure suitable for automation.

---

# 22. PROFESSIONAL HTML REPORT

HTML reporting is a major paid-product differentiator.

Generate a single portable HTML report when possible.

It should look professional.

Include:

- Product name
- Timestamp
- Old contract information
- New contract information
- Overall result
- Breaking change count
- Severity summary
- Compatibility summary
- Affected endpoints
- Findings
- Rule IDs
- Detailed explanation
- Suggested remediation
- Suppressed findings
- Version recommendation

Provide filtering by:

- Severity
- Breaking status
- HTTP method
- Path
- Rule
- Category

Provide search.

Provide collapsible finding details.

Do not require an internet connection to view the report.

Avoid external JavaScript/CDN dependencies when reasonably possible.

It should be suitable for sending to a customer, attaching to a pull request, storing as CI artifact, and presenting during an API review.

---

# 23. CONSOLE UX

Console output should be readable in:

- Windows Terminal
- PowerShell
- cmd where practical
- Linux terminal
- macOS terminal
- CI logs

Support `--no-color`.

Do not rely exclusively on color to communicate severity.

Include concise default output and an optional verbose mode.

---

# 24. EXIT CODES

Create stable documented exit codes.

For example:

- 0 = comparison completed and policy passed
- 1 = policy threshold violated
- 2 = invalid input/specification
- 3 = invalid configuration
- 4 = network/reference retrieval failure
- 5 = internal application failure

Do not make CI users parse text to determine outcome.

Document exit codes.

---

# 25. CI/CD

Provide production-quality examples for:

- GitHub Actions
- Azure DevOps
- GitLab CI
- generic shell
- PowerShell

Typical CI use case:

```text
PR opened
    ↓
Generate current swagger.json
    ↓
Load baseline swagger.json from main
    ↓
OpenAPI Sentinel compare
    ↓
No breaking changes
    → pass

Breaking changes detected
    → fail
    → publish Markdown/HTML report
```

Make examples genuinely usable.

---

# 26. GITHUB ACTION

Create a reusable GitHub Action integration.

Desired usage should be approximately:

```yaml
- uses: owner/openapi-sentinel-action@v1
  with:
    base: ./contracts/main.json
    head: ./swagger.json
    fail-on: high
```

Provide outputs such as:

- breaking-count
- high-count
- medium-count
- recommended-version
- report-path

Where practical, generate GitHub annotations.

Keep Action-specific logic thin.

The actual compatibility engine must remain reusable outside GitHub.

---

# 27. PULL REQUEST SUMMARY

Generate a concise Markdown summary suitable for posting on a Pull Request.

Optionally support GitHub Step Summary.

---

# 28. COMMERCIAL PRODUCT ARCHITECTURE

Design the product so it can support two editions later:

- Community
- Pro

Do not cripple the architecture with hardcoded licensing logic.

Create clean feature boundaries.

Possible positioning:

## Community

- basic comparison
- console output
- JSON output
- core breaking-change detection

## Pro

- professional HTML report
- advanced policy configuration
- baselines
- suppressions
- SARIF/JUnit
- semantic version recommendation
- changelog generation
- advanced CI features
- enterprise reports

Do NOT implement invasive telemetry.

Do NOT require an online account for core operation.

Keep licensing/entitlement behind an interface so different commercial distribution systems can be added later.

Do not build complicated DRM during this task unless necessary.

The primary goal is product quality.

---

# 29. PRIVACY

Default behavior:

- No telemetry
- No API contract upload
- No cloud requirement
- No AI requirement
- No analytics
- No source code upload

Add a README section:

`Privacy & Offline Operation`

Explain clearly that contracts are processed locally.

---

# 30. SECURITY

Treat OpenAPI files as untrusted input.

Protect against:

- malicious YAML
- excessive nesting
- reference bombs
- cyclic references
- decompression/resource abuse where applicable
- path traversal
- unsafe external references
- SSRF
- extremely large files
- unreasonable recursion
- denial-of-service style schema structures

Implement bounded processing where practical.

Never execute code contained in the specification.

Do not deserialize into unsafe polymorphic runtime types.

Perform dependency security review.

---

# 31. DEPENDENCY AND LICENSE POLICY

This product may eventually be sold commercially.

Prefer dependencies under licenses such as:

- MIT
- Apache-2.0
- BSD

Avoid dependencies whose licensing could create commercial redistribution problems.

Do not introduce GPL, AGPL, or SSPL dependencies unless absolutely necessary and explicitly documented.

Create:

`THIRD-PARTY-NOTICES.md`

listing important runtime dependencies and licenses.

Do not copy code from incompatible open-source projects.

Implement original code or use properly licensed libraries.

---

# 32. TEST STRATEGY

Testing is mandatory.

Create a comprehensive fixture-driven compatibility test suite.

At minimum test:

- OpenAPI 2.0
- OpenAPI 3.0
- OpenAPI 3.1
- OpenAPI 3.2
- JSON
- YAML
- internal refs
- external refs
- cyclic refs
- invalid documents
- large documents

For every major compatibility rule, include:

- breaking example
- non-breaking example
- edge case

Create many realistic contracts rather than trivial one-property examples only.

Aim for broad meaningful rule coverage.

---

# 33. GOLDEN/SNAPSHOT TESTS

For reports, use deterministic golden/snapshot-style tests where appropriate.

Verify:

- Markdown report
- JSON report
- HTML report structure
- console result model

Avoid timestamps/random values breaking deterministic tests.

Inject clocks/IDs where necessary.

---

# 34. END-TO-END TEST

Create at least one end-to-end scenario:

```text
ASP.NET-like baseline API
        ↓
several realistic modifications
        ↓
run OpenAPI Sentinel
        ↓
detect breaking + non-breaking changes
        ↓
generate HTML
        ↓
generate JSON
        ↓
generate Markdown
        ↓
return expected CI exit code
```

The fixture should demonstrate the product to a buyer.

---

# 35. REALISTIC DEMO CONTRACT

Create a professional sample API such as:

`Order Management API`

Include:

- orders
- customers
- products
- authentication
- pagination
- enums
- request models
- response models
- validation
- multiple status codes

Create:

- v1.yaml
- v2-safe.yaml
- v2-breaking.yaml

This sample should be useful in screenshots and marketing.

---

# 36. PERFORMANCE

API specs can become large.

Benchmark realistically.

Create benchmarks for:

- small contract
- medium contract
- large contract

Include documents with hundreds or thousands of operations/schemas where practical.

Measure:

- parse time
- normalization time
- comparison time
- report generation time
- peak memory where practical

Avoid obvious O(n²) behavior when indexing can solve it.

Do not prematurely micro-optimize.

Correctness comes first.

Document benchmark results honestly.

---

# 37. DISTRIBUTION

The finished application should be easy to install.

Support practical distribution options such as:

- self-contained Windows x64
- self-contained Windows ARM64 where practical
- Linux x64
- Linux ARM64
- macOS x64
- macOS ARM64
- .NET global tool if appropriate

Preferred user experience:

```bash
openapi-sentinel --version
```

should simply work after installation.

Create release scripts.

---

# 38. RELEASE AUTOMATION

Create build/release automation capable of producing versioned artifacts.

Example outputs:

- openapi-sentinel-win-x64.zip
- openapi-sentinel-win-arm64.zip
- openapi-sentinel-linux-x64.tar.gz
- openapi-sentinel-linux-arm64.tar.gz
- openapi-sentinel-osx-x64.tar.gz
- openapi-sentinel-osx-arm64.tar.gz

Include checksums.

Generate release notes.

Ensure version is consistently exposed through CLI, binary metadata, NuGet package if used, and reports.

---

# 39. DOCKER

Provide an optional Docker image.

Example:

```bash
docker run --rm \
  -v "$PWD:/workspace" \
  openapi-sentinel \
  compare /workspace/old.yaml /workspace/new.yaml
```

Keep Docker optional.

Users should not be forced to install Docker.

---

# 40. DOCUMENTATION

Create production-quality documentation.

At minimum:

- README.md
- docs/getting-started.md
- docs/configuration.md
- docs/cli-reference.md
- docs/rules.md
- docs/compatibility-model.md
- docs/github-actions.md
- docs/azure-devops.md
- docs/gitlab.md
- docs/security.md
- docs/privacy.md
- docs/troubleshooting.md
- docs/commercial/product-positioning.md
- docs/commercial/feature-matrix.md
- docs/commercial/release-checklist.md

README should include:

- What problem does OpenAPI Sentinel solve?
- Quick start
- Example
- Screenshot/report example
- Breaking-change explanation
- CI example
- Offline/privacy statement
- Supported OpenAPI versions
- Installation
- Documentation links

---

# 41. PRODUCT POSITIONING

Create:

`docs/commercial/product-positioning.md`

Draft commercial positioning around:

> Stop accidental API breaking changes before they reach production.

Potential differentiators:

- OpenAPI 2.0–3.2 support
- direction-aware request/response compatibility analysis
- offline operation
- no source/API upload
- CI enforcement
- professional HTML reports
- stable rule IDs
- explainable findings
- baseline/suppression workflow
- semantic version recommendation
- cross-platform

Do not make unsupported marketing claims.

---

# 42. FEATURE MATRIX

Create:

`docs/commercial/feature-matrix.md`

Compare proposed Community and Pro editions.

Make the free edition useful enough to gain adoption while keeping meaningful commercial value in Pro.

Do not intentionally degrade correctness in Community.

Core safety/breaking-change correctness should never depend on payment.

Commercial differentiation should come from productivity/governance/reporting features.

---

# 43. PRICING INPUT

Do not implement payment processing.

Create a short pricing strategy document with possible launch pricing such as:

- Personal Lifetime
- Team Lifetime
- Team Annual

Include reasoning, target buyer, and which features create willingness to pay.

Keep this document clearly separated from application logic.

---

# 44. ERROR HANDLING

All errors must be actionable.

Do not expose stack traces by default.

Provide verbose/debug mode when appropriate.

---

# 45. LOGGING

Provide optional diagnostic logging:

- --verbose
- --debug

Do not pollute normal output.

Never log credentials from authenticated URL sources.

Redact sensitive headers.

---

# 46. URL AUTHENTICATION

If URL loading supports authentication, keep it secure.

Potential support:

- Authorization header supplied through environment variable
- custom header supplied through environment variable

Never require secrets directly in command-line arguments when a safer option exists.

Never print secrets.

Document safe CI usage.

---

# 47. RULE DOCUMENTATION

Every built-in rule must be documented.

A rule document should include:

- Rule ID
- Title
- Category
- Default severity
- Compatibility classification
- Description
- Breaking example
- Non-breaking example where relevant
- Remediation
- Exceptions/limitations

`openapi-sentinel explain <RULE-ID>` should reuse the same source of truth where practical.

Avoid duplicated rule documentation drifting out of sync.

---

# 48. MACHINE-READABLE RESULT MODEL

Design a versioned machine-readable output contract.

Example:

```json
{
  "schemaVersion": "1.0",
  "toolVersion": "1.0.0",
  "result": "failed",
  "summary": {},
  "findings": []
}
```

Document backward compatibility expectations for output schema.

---

# 49. DETERMINISM

Given identical old contract, new contract, configuration, and tool version, results should be deterministic.

Sort findings using a stable ordering.

Do not depend on dictionary iteration ordering.

Reports should be diff-friendly.

---

# 50. QUALITY GATES

Before considering the task complete, run:

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Also execute representative CLI commands against sample contracts.

Validate generated JSON, Markdown, and HTML outputs.

Validate release packaging.

Fix failures.

Repeat until clean.

---

# 51. STATIC QUALITY

Enable strong compiler settings where reasonable.

Use:

- nullable
- analyzers
- deterministic builds
- warnings

Do not turn off warnings globally just to get a green build.

Keep public APIs documented where appropriate.

---

# 52. SOURCE STRUCTURE QUALITY

Reject:

- giant 2,000-line classes
- giant switch statements controlling every rule
- duplicated schema traversal logic
- business logic embedded in CLI rendering
- hidden global mutable state
- unnecessary static singletons
- random utility dumping grounds

Prefer focused modules and explicit contracts.

At the same time, avoid needless abstraction.

---

# 53. RULE EXTENSIBILITY

Design the rule engine so future versions could support:

- custom enterprise rules
- organization-specific policy packs
- plugin rule assemblies

You do not have to implement an unsafe arbitrary plugin loader immediately.

But the core architecture should not make future extension impossible.

---

# 54. REPORT EXTENSIBILITY

A new report format should be addable without rewriting comparison logic.

Comparison should produce a structured result.

Reporters consume that result.

Conceptual architecture:

```text
Load
 ↓
Parse
 ↓
Normalize
 ↓
Compare
 ↓
Evaluate Rules
 ↓
Compatibility Result
 ↓
Policy Evaluation
 ↓
Reporters
     ├ Console
     ├ JSON
     ├ Markdown
     ├ HTML
     ├ SARIF
     └ JUnit
```

Improve this architecture if necessary.

---

# 55. FALSE POSITIVE CONTROL

A commercial API governance tool cannot constantly cry wolf.

Focus on reducing false positives.

For ambiguous changes:

- explain uncertainty
- use confidence
- use Verify when justified
- do not invent certainty

Create tests specifically for common false positives:

- property ordering
- description changes
- examples
- formatting
- tags
- documentation changes
- non-semantic ordering

---

# 56. FALSE NEGATIVE CONTROL

Create tests designed to ensure subtle breaking changes are not missed.

Examples:

- enum contraction
- validation range narrowing
- security requirement tightening
- content-type removal
- nullable -> non-nullable
- optional request property -> required
- response type changed
- parameter serialization changed
- schema composition changes

---

# 57. SELF-REVIEW PASS

After implementation appears complete, perform another independent review pass.

Review the repository specifically for:

- correctness bugs
- OpenAPI semantic mistakes
- breaking-change classification errors
- false positives
- false negatives
- security risks
- SSRF
- path traversal
- unsafe YAML handling
- cyclic $ref failures
- large-input problems
- CI behavior
- exit-code bugs
- cross-platform path problems
- Windows-specific assumptions
- Linux-specific assumptions
- license problems
- commercial distribution problems
- poor error messages
- missing documentation
- untested edge cases

Fix concrete problems discovered.

Then rerun tests.

---

# 58. COMMERCIAL-READINESS REVIEW

After technical review, review it as though you were a paying customer.

Ask:

- Can I install this easily?
- Do I understand what it does?
- Can I evaluate it within five minutes?
- Are errors understandable?
- Does the report look worth paying for?
- Can I put it in CI immediately?
- Can I trust it with a private API specification?
- Does it explain WHY a change is breaking?
- Can I suppress an accepted exception?
- Can I automate against the JSON output?
- Can I send the HTML report to another engineer or manager?

Fix obvious product-quality gaps.

---

# 59. DEFINITION OF DONE

Do NOT mark this task complete until all reasonable items below are satisfied:

- solution builds successfully
- automated tests pass
- OpenAPI 2.0 supported
- OpenAPI 3.0 supported
- OpenAPI 3.1 supported
- OpenAPI 3.2 supported
- JSON supported
- YAML supported
- semantic normalization works
- meaningful breaking-change rules implemented
- request/response compatibility distinction implemented
- security contract changes detected
- `$ref` behavior tested
- cyclic references tested
- local file comparison works
- URL comparison works safely
- Git-reference workflow works or is clearly implemented/documented
- console reporter works
- JSON reporter works
- Markdown reporter works
- HTML reporter works
- HTML looks professional
- CI exit codes work
- config file works
- rule overrides work
- suppressions work
- suppression expiration works
- baseline workflow works
- semantic version recommendation works
- changelog generation works
- GitHub Actions example/integration works
- Azure DevOps example works
- GitLab example works
- Docker usage works where implemented
- release packaging exists
- Windows package exists
- Linux package exists
- macOS package exists where feasible
- README is production quality
- rule documentation exists
- security documentation exists
- privacy documentation exists
- troubleshooting documentation exists
- commercial positioning exists
- Community/Pro feature matrix exists
- third-party license review exists
- representative benchmark exists
- end-to-end demo exists
- no fake tests/results remain
- no major TODO placeholders remain in core functionality

---

# 60. FINAL OUTPUT TO ME

Only after completing as much of the implementation as reasonably possible, give me a final engineering report containing:

## Completed

List major completed capabilities.

## Architecture

Summarize the final architecture.

## Commands

Give exact commands to:

- build
- test
- run
- compare two sample files
- generate HTML
- generate JSON
- generate Markdown
- test CI behavior
- build release artifacts

## Test Results

Report actual test results.

Do not invent them.

## Product Demo

Give me the fastest way to demonstrate the product to a potential buyer.

## Release Artifacts

Tell me exactly where generated release files are located.

## Remaining Risks

List concrete remaining technical/product risks.

## Commercial Readiness

Rate from 1–10 with a short reason:

- Core engine
- CLI
- Testing
- Security
- Documentation
- Reporting
- CI/CD
- Distribution
- Commercial readiness

## Next Highest-Value Improvements

Give only improvements that materially increase correctness, adoption, commercial value, or ability to charge money.

---

# 61. START NOW

Begin by inspecting the current repository.

If the repository is empty, initialize the solution.

If partial code exists, understand and preserve useful existing work.

Create an implementation plan internally, then immediately begin implementation.

Do NOT stop after showing me the plan.

Do NOT merely tell me what files should be created.

Actually create and modify the files.

Actually implement the engine.

Actually write tests.

Actually run the tests.

Actually fix failures.

Actually generate the reports.

Actually exercise the CLI.

Actually review the finished product.

Continue working autonomously until the repository is as close as reasonably possible to a production-ready, commercially sellable v1 release.

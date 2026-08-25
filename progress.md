# openapi-sentinel — Progress Snapshot

Generated: 2026-08-23 ~19:15 local time
Git branch: `main`
Latest commit: **`3ff0b5d`** — full v0.9.0 implementation committed on main (119 files). Pushed to origin/main (github.com/kok751014/openapi-sentinel) — publicly available. (`git status`: untracked AGENTS.md, Directory.Build.props, LOOP_PROMPT.md, MASTER_PROMPT.md, OpenApiSentinel.slnx, progress.md, src/, tests/)

Environment: .NET SDK **10.0.400** installed at `/home/kim/.dotnet`. Every shell must run:
```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
```

**Current verified status (2026-08-23 ~14:40):**
- `dotnet build -c Release` → **0 errors, 0 warnings** (TreatWarningsAsErrors + analyzers latest-recommended)
- **IN-REPO DEFINITION OF DONE: COMPLETE.** All master-spec items achievable inside this repository are implemented and verified (engine, 77-entry rule catalog incl. engine meta-checks, directional semantics audited per family, 7 output formats, CLI with subcommand help + stable exit codes, CI workflow + composite action w/ step summaries, release scripts validated end-to-end, Dockerfile, full docs). Only EXTERNAL actions remain: git commit/push (~20 untracked paths), NuGet/Action publication, Docker build.
- `dotnet test -c Release` → **Passed! 21 Core + 22 OpenApi + 45 Rules + 22 Reporting + 17 Cli + 3 Integration = 130 tests**, 0 failed
- Full quality gate re-verified: build 0/0, all artifact formats validated on samples (JSON result/MAJOR rec, SARIF 2.1.0 w/ 33 results, JUnit failures present, Markdown sections, changelog Breaking section, self-contained HTML ~64 KB)

---

## Completed work

### Scaffolding (done, verified)
- Solution `OpenApiSentinel.slnx` with 11 projects: 5 src (Core, OpenApi, Rules, Reporting, Cli) + 6 test projects (Core/OpenApi/Rules/Reporting/Cli.Tests as xunit + IntegrationTests).
- `Directory.Build.props`: net10.0, nullable enable, implicit usings, `TreatWarningsAsErrors=true`, analyzers `latest-recommended`, deterministic build, product metadata/version props (`SentinelVersion`), DotnetTool pack settings for CLI (`ToolCommandName=openapi-sentinel`). Test projects get CA1707 suppressed and no doc-file generation.
- Project references wired: OpenApi→Core; Rules→Core+OpenApi; Reporting→Core; Cli→Core+OpenApi+Rules+Reporting. YamlDotNet package added to OpenApi project.

### Core project (done, compiles clean)
- `Enums.cs`: Severity (Info..Critical+Verify), Compatibility (Breaking/PotentiallyBreaking/NonBreaking/Informational/Unknown), ChangeType, FindingCategory, Confidence.
- `SemanticChange.cs`: typed change record (Kind string, Category, Path, HttpMethod, Location, Direction Request/Response/Neutral, ParameterName, PropertyName, Old/New values, Details FrozenMap) + `HttpMethodKind` enum/parser + JSON serializer options.
- `Finding.cs`: full finding record (RuleId, Title, Severity, Category, ChangeType, Compatibility, Confidence, Direction, Path, HttpMethod, Location, OldValue, NewValue, Explanation, Impact, SuggestedAction) + SHA-256 stable `Fingerprinting.Compute` (excludes explanation text so baselines survive wording changes).
- `ComparisonResult.cs`: ComparisonResult record (SchemaVersion "1.0", ToolVersion, Result passed/failed, contract names, versions, GeneratedAtUtc, Findings, Suppressed list, Summary counts, VersionRecommendation, PolicyViolated, Endpoints summary, ChangelogMarkdown); ResultSummary; VersionBump enum; SuppressedFinding (source suppression|baseline); EndpointSummary/EndpointState.
- `Policy.cs`: ISentinelClock/SystemClock; SeverityNames parser incl. aliases error/warning/off/all; FailurePolicy (FailOnSeverity threshold / FailOnAnyFinding / FailOnBreaking default = breaking-or-potentially at severity>=Medium) → PolicyDecision; RuleOverride record (RuleId, Action "off", NewSeverity).
- `GlobMatcher.cs` (NEW): anchored ordinal glob → compiled Regex with 1s timeout; `*`=no slash, `**`=crosses slashes (mid-segment `/a/**/b` also matches `/a/b`; trailing `/internal/**` matches the prefix itself), `?`=single non-slash char, regex chars escaped, `/**` alone matches all. Static Matches/MatchesAny incl. precompiled-matcher overload.
- `RuleDescriptor.cs`: static rule metadata (Id, Title, Category, DefaultSeverity, DefaultCompatibility, ChangeKinds, Description, Impact, Remediation, BreakingExample, NonBreakingExample, Limitations).

### OpenApi project (done, compiles clean)
- `ProcessingLimits.cs`: max bytes 20MB, depth 128, nodes 2M, YAML anchor 256 / alias-use 100k caps, max resolved refs 200k, ref chain depth 64. Exceptions SpecParseException (exit 2) and SpecFetchException (exit 4).
- `DocumentParser.cs`: JSON via System.Text.Json (MaxDepth enforced, comments disallowed) and YAML via YamlDotNet representation model → both converge to JsonNode. YAML alias-bomb pre-scan, node-count budget, non-scalar key rejection, quote-aware scalars.
- `GitAndStdinLoader.cs`: SpecLoader.LoadAsync dispatches `-`/stdin, `git:<rev>:<path>` (shells out to git with cat-file existence probe and friendly errors for missing git/file/revision), file paths, http(s) URLs.
- `UrlLoader.cs`: remote fetch policy (Enabled gate, HTTPS default w/ AllowHttp opt-in, host allowlist, timeout, bounded response size read loop, manual redirect following with per-hop SSRF revalidation ≤5 hops). SSRF guard: resolves DNS, rejects loopback/private/link-local/CGNAT/ULA/unspecified unless AllowPrivateNetwork. Env-header auth (`EnvHeader`) resolved from environment variables, never logged.
- `RefResolver.cs`: in-place $ref resolution. Internal pointers (#/... RFC6901 unescape), external relative files (must stay inside base dir subtree — path traversal rejected), remote http(s) refs (policy-gated, cached per document). Per-document identity so fragment refs resolve against the right doc. Cycle detection via chain-id set (doc#fragment) → replaced by `x-sentinel-cyclic-ref` marker object instead of infinite recursion. Node budget during materialization defeats reference bombs. $ref siblings preserved per 3.1 JSON-Schema semantics.
- `Model/ApiDocument.cs`: normalized ApiDocument (SpecVersion Swagger20/OpenApi30/OpenApi31/OpenApi32 + RawVersion, Title, InfoVersion, Servers, Paths, Webhooks, SecuritySchemes, GlobalSecurity); PathItem, WebhookItem, Operation (merged parameters, requestBody, Responses dict keyed "200"/"2XX"/"default", op-level security), ParameterInfo (In, Required — path params forced required, Style/Explode, BodySchema marker for 2.0 body/formData), RequestBodyInfo (Required + Content media-type→schema), ResponseInfo, HeaderInfo; SchemaInfo wrapper over JsonNode with version-aware accessors: Types() handles 3.0 `nullable` sibling vs 3.1 `type:[...,null]`, PrimaryType(), Format, TryGetKeyword, CompositionOf, PropertyOrNull.
- `Model/SecurityModel.cs`: SecurityRequirementInfo (scheme+scopes), SecuritySchemeType enum (ApiKey/Http/OAuth2/OpenIdConnect/Basic), SecuritySchemeInfo (raw type, In, Scheme, BearerFormat, OpenIdConnectUrl, Flows w/ OAuthFlow urls+scopes, Deprecated).
- `OpenApiNormalizer.cs`: version detection (unsupported version → actionable error), 2.0 host/basePath/schemes→servers synthesis, paths/webhooks normalization, path-level param merge (operation wins by name+in), 2.0 body/formData params converted into RequestBodyInfo (consumes→content types; formData→object schema of properties), 2.0 responses schema+produces→content map, 3.x requestBody/responses.content/headers, security requirements parsing (absent=null semantics), securityDefinitions/components.securitySchemes→unified model.
- `ChangeKinds.cs`: 72 stable change-kind constants across endpoint/parameter/requestbody/response/schema/security/metadata groups (incl. ApiKeyNameChanged, TitleChanged).
- `SchemaComparer.cs`: recursive direction-aware schema diff emitting SemanticChanges: type changed (incl. core-type set diff + nullability split out), format, enum value added/removed (deep-equality set diff), const, constraints loop (minimum/maximum/exclusive*/multipleOf/minLength/maxLength/pattern/minItems/maxItems/uniqueItems/minProperties/maxProperties) each with Details["constraint"], property added/removed, rename detection (same primary type + ≥80% nested-property Jaccard OR typographic variant → PropertyRenamed else separate remove/add), required-list changes (request-side add/remove deduped against property events), additionalProperties, items (recurses into element schemas), allOf/oneOf/anyOf/not composition (order-insensitive deep JSON equality), discriminator, readOnly/writeOnly/deprecated/default/example flags. Cyclic markers normalize to `{"type":"cyclic"}`. Depth guard 48. Deterministic sorted iteration everywhere.
- `ComparisonEngine.cs`: top-level lockstep compare producing SemanticChange stream: servers changed, info.version changed, security schemes diff (added/removed/type/in/http-scheme/bearerFormat/openidUrl/flow urls/flow scope defs), paths added/removed, operations added/removed per method, operationId changed/added/removed, deprecated toggle, parameter compare (removed w/ required detail, added w/ required detail, required toggle, style/explode/deprecated/location moved=short-circuit, type/format, enum add/remove, default, nullable + filtered sub-schema pass), request body container rules (added/removed/required/content types + schema pass tagged RequestBody category), responses (status removed unless covered by remaining wildcard range e.g. 4XX/default coverage logic; status added; content type add/remove; headers add/remove/required-toggle; schema pass with statusCode detail), effective per-operation security requirements diff (added=auth now required, removed, OAuth scope add/remove; alternatives unioned conservatively). Sorted deterministic output.

### Rules project (COMPLETE — compiles clean, unit-tested)
- `RuleRegistry.cs`: RuleDefinition (Descriptor + Build delegate), RuleRegistry (id-keyed, duplicate detection, CreateDefault factory), RuleText helpers (DescribeChange, Values, TryGetNumber — TryParse arg order FIXED, ChangeTypeOf), FindingFactory.Create (copies change context into finding), DirectionLogic (ClassifyNumeric tightened/relaxed per keyword semantics; ForRequest/ForResponse mapping incl. pattern-like → Verify).
- `ParameterRules.cs`: CG2001–CG2016 (16 rules). ParamName precedence bug FIXED (was `??` mixed with ternary).
- `EndpointRules.cs`: CG1001–CG1005 + CG1010 ServersChanged(PotentiallyBreaking).
- `RequestBodyRules.cs`: CG3001–CG3006.
- `ResponseRules.cs`: CG4001–CG4007 (syntax errors fixed: block-lambda sites ended `"),\n};\n};` → `");\n},\n};`).
- `SchemaRules.cs`: CG5001–CG5019 (19 rules; same syntax fix ×5).
- `SecurityRules.cs` (NEW): CG6001 requirement added=Breaking High, CG6002 requirement removed=PotentiallyBreaking Medium, CG6003 OAuth scope added=Breaking Medium, CG6004 scope removed=NonBreaking Low, CG6005 scheme removed=Breaking High, CG6006 scheme added=Informational, CG6007 scheme type changed=Breaking High, CG6008 apiKey location changed=Breaking High, CG6009 http scheme changed=Breaking High, CG6010 bearerFormat changed=PotentiallyBreaking Low, CG6011 openid url changed=PotentiallyBreaking Medium, CG6012 flow url changed (Details flow/urlKind)=PotentiallyBreaking Medium, CG6013 flow scope def removed=PotentiallyBreaking Medium, CG6014 flow scope def added=NonBreaking Info.
- `MetadataRules.cs` (NEW): CG7001 infoVersionChanged Informational, CG7002 titleChanged Informational, CG7003 operationIdChanged PotentiallyBreaking Medium, CG7004 operationIdRemoved PotentiallyBreaking Medium, CG7005 operationIdAdded NonBreaking Info.
- `DefaultRules.cs` (NEW): aggregates All() of all seven rule families (57 rules total).
- `EngineOptions.cs` (NEW): SuppressionEntry (RuleId wildcard "*", optional PathGlob, required Reason, Expires DateTimeOffset?), EngineOptions (contract names/versions, ToolVersion, Overrides, IgnorePathGlobs, Suppressions, BaselineFingerprints, Policy, ReportUnknownChangeKinds), EngineStatistics counters, EngineEvaluation (Result + Statistics).
- `RulesEngine.cs` (NEW): kind→rule index (first by rule id wins); override off/severity-remap; ignore-path globs; suppressions (rule+glob+expiry; expired match keeps finding and appends CG9001 Low warning); baseline fingerprint suppression (source "baseline") + stale-baseline aggregated CG9002 Info finding; optional aggregated CG9003 Verify finding for unknown kinds; deterministic sort (Path ordinal, HttpMethod canonical order None<Get<Put<Post<Delete<Options<Head<Patch<Trace, Location, RuleId, Old, New); ResultSummary counts; VersionRecommendation (Major = Breaking ∧ severity≥High; Minor = any additive kind endpoint/operation/contenttype/statuscode; Patch = only ≤Low findings; None otherwise w/ reasons); EndpointSummary list (terminal Removed state wins); FailurePolicy hookup sets PolicyViolated.

### Tests (92 passing)
- `tests/OpenApiSentinel.OpenApi.Tests/PipelineSmokeTests.cs` (10): parse YAML w/ internal refs & version detect; identical specs → zero changes; endpoint added/removed detection; JSON↔YAML equivalence; cyclic $ref no stack overflow + stable compare; external file refs within base dir + path-traversal rejection; Swagger 2.0 normalization; OpenAPI 3.1 type-array nullability; invalid docs actionable errors; deep-nesting bomb rejected.
- `tests/OpenApiSentinel.Core.Tests/GlobMatcherTests.cs` (21 NEW): trailing `/**` subtree semantics incl. prefix itself, segment `**` zero-or-more, `*` no-cross-slash, `?`, regex escaping, case sensitivity, MatchesAny precompiled overload, empty patterns.
- `tests/OpenApiSentinel.Reporting.Tests/ReporterTests.cs` (21 NEW): deterministic TestResults fixtures; JSON valid/camelCase/string enums/suppressed toggle/determinism; Markdown core sections+suppressed table+PR summary+changelog sections; HTML single-file no external refs/no src=/required content/FAILED verdict/filters present/HTML escaping/determinism; Console labels-without-color/ANSI when enabled/verbose impact/action/passed path; SARIF structure+level mapping+distinct ordered rules; JUnit parse+failure counts+passing case+XML escaping round-trip.
- `tests/OpenApiSentinel.IntegrationTests/EndToEndScenarioTests.cs` (3): breaking evolution → exit 1 + JSON/MD/HTML artifacts w/ expected rule IDs + MAJOR recommendation + offline HTML; safe evolution → exit 0 MINOR; baseline workflow (adopt→suppressed pass→new comparison unaffected by stale baseline).
- `tests/OpenApiSentinel.OpenApi.Tests/ComparisonRegressionTests.cs` (4 NEW): comparer reuse no-leak, null-security inherits global (no findings on identical docs w/ global security), security:[] override → CG6002 removal event w/ correct path/method, parameter constraint change carries parameters.pageSize location.
- `tests/OpenApiSentinel.Rules.Tests/RulesEngineTests.cs` (16) w/ FakeClock: endpoint removal→CG1001 Critical/Breaking; unknown kinds counted + optional CG9003; override off & severity remap; ignore-path glob filtering; suppression hides + lists reason; expired suppression keeps finding + CG9001 warning; baseline suppression/stale-baseline CG9002; deterministic sort order; recommendation Major/Minor/Patch/None; policy hookup (failed/passed/relaxed); endpoint summaries terminal states; summary counting.

### Infrastructure fixes
- `tests/OpenApiSentinel.IntegrationTests/OpenApiSentinel.IntegrationTests.csproj` was xunit.v3+MSTest.Sdk-18.9 Exe-style → broke on .NET 10 SDK ("Testing with VSTest target is no longer supported"). Rewritten to the same xunit 2.9.3 / Microsoft.NET.Test.Sdk 17.14.1 stack as sibling test projects. No test files yet.

---

## Unfinished TODO items (in order)

1. ~~RulesEngine~~ DONE (see above).
2. ~~GlobMatcher~~ DONE (see above).
3. ~~Reporting project~~ DONE: ConsoleReporter (ANSI w/ text labels, NoColor, verbose), JsonReporter (documented camelCase contract, string enums), MarkdownReporter (+static RenderPrSummary), ChangelogGenerator (Breaking/Non-breaking sections), HtmlReporter (single file, embedded CSS/JS, severity/method/compat filters + breaking-only checkbox + search + collapsible details + stat cards + verdict + recommendation badge; zero external resources), SarifReporter (2.1.0, rules array, level mapping Critical/High→error Medium/Low→warning else note), JunitReporter (testsuites w/ failure elements per violating finding). All consume ComparisonResult only; all deterministic.
4. ~~CLI project~~ DONE: CommandLineRunner (thin Program.cs shim; Run(args, IConsoleWriter)→int), hand-rolled CommandLineParser (--flag/--opt v/--opt=v, `--` escape, `-` stdin), commands: compare (full pipeline+artifacts), validate, rules (--format json), explain <id>, init [--force], version, help. SentinelConfigLoader: dictionary-based YAML validation w/ unknown-key warnings (top-level + nested sections + suppression keys), failOn (all/none/severity), rules overrides (off/severity, CG\\d{4} validation), ignore.paths, remoteReferences (enabled/allowHttp/allowPrivateNetwork/allowedHosts/timeoutSeconds/maxResponseBytes), headerEnv, suppressions (rule/path/reason REQUIRED/expires ISO date), baseline path, output section. Exit codes 0/1/2/3/4/5 mapped in runner (SpecParse→2, SpecFetch→4, SentinelConfig→3, unexpected→5 unless --debug). CompareWorkflow: load both docs (AnalysisPipeline: loader→parse→resolve→normalize), ComparisonEngine, RulesEngine (CLI failOn/baseline override config; --update-baseline merges existing+current fingerprints), artifacts: console|json|markdown|html primary to stdout unless file target; --json/--markdown/--html/--sarif/--junit/--changelog write files (CLI > config precedence); `[sentinel] wrote <fmt>: <path>` to stderr. ToolVersion from assembly.
- `tests/OpenApiSentinel.Cli.Tests/CommandLineRunnerTests.cs` (17): version/rules/rules-json/explain+unknown/init+overwrite-guard/breaking→exit1 w/ CG1001+CG1003/safe→exit0/json format+--fail-on none semantics/combined artifacts (json,md,html,sarif,junit,changelog files written; sarif 2.1.0)/invalid spec→exit2/private-network URL→exit4 SSRF/config ignore.paths+failOn:none/baseline update-then-adopt→suppressed+exit0/missing suppression reason→exit3/unknown config key→stderr warning/suppression hides finding→exit0/validate summary/help usage.
- FIX during CLI work: config warnings now routed through IConsoleWriter (were Console.Error directly, untestable); RenderFormat extended to sarif/junit/changelog (was exit-3 on those artifact keys); SchemaComparer propsUnion now includes required-only entries (required referencing undefined property was silently skipped); CG5008 downgraded Medium/PotentiallyBreaking→Info/NonBreaking (double-reported recursion details; false-positive per §55); test fixture bug found: "missing property.added" was a heredoc fixture error (customerRef only in required), engine was correct.
5. Fixture-driven rule tests (fixtures/openapi/** + expected-rule manifests; breaking/non-breaking/edge per major rule; false-positive suite: property ordering, description-only changes, examples-only, tags reorder, formatting → zero findings; false-negative suite: enum contraction, range narrowing, content-type removal, nullable flip, optional→required, composition changes).
6. Golden/snapshot reporter tests with fixed clock; SARIF/JSON structure validation; HTML contains key sections.
7. IntegrationTests: end-to-end demo scenario (v1.yaml→v2-breaking.yaml) asserting findings + exit code 1 + artifacts generated.
8. Sample Order Management API contracts (samples/basic v1.yaml, v2-safe.yaml, v2-breaking.yaml — realistic: orders/customers/products, auth, pagination, enums, multiple statuses).
9. CI samples (samples/github-actions workflow.yml using composite action, azure-devops pipeline.yml, gitlab-ci.yml, generic shell+PowerShell), `action/action.yml` composite GitHub Action (inputs base/head/fail-on; outputs breaking-count/high-count/medium-count/recommended-version/report-path; annotations via ::error:: lines), `.github/workflows/ci.yml` for this repo.
10. Release automation: `build/release.sh` + `build/release.ps1` publishing self-contained win-x64/win-arm64/linux-x64/linux-arm64/osx-x64/osx-arm64, zip/tar.gz, SHA256SUMS, RELEASE_NOTES.md; NuGet global-tool pack; Dockerfile (non-root, workdir /workspace); benchmarks console app (bench/) measuring parse/normalize/compare/report on generated small/medium/large specs + docs/benchmarks.md honest results.
11. Documentation: README.md rewrite (problem, quick start, example output, screenshot-placeholder report excerpt, CI example, privacy/offline statement, supported versions matrix, install options, links), docs/getting-started.md, configuration.md, cli-reference.md (incl. exit codes table), rules.md (generated FROM RuleRegistry to avoid drift — write a small doc-gen test or script), compatibility-model.md (direction theory + examples), github-actions.md, azure-devops.md, gitlab.md, security.md (threat model: YAML bombs, SSRF, traversal, limits), privacy.md, troubleshooting.md, architecture/, commercial/{product-positioning.md, feature-matrix.md Community-vs-Pro, release-checklist.md, pricing-strategy.md}, DECISIONS.md (record: YamlDotNet choice, cyclic-marker strategy, wildcard-response matching policy, rename heuristics, Verify usage policy, exit-code table, ID scheme CGxxxx ranges), THIRD-PARTY-NOTICES.md (YamlDotNet MIT), LICENSE (proprietary draft).
12. Quality gates: dotnet restore/build/test -c Release clean; run representative CLI commands on samples; validate JSON/MD/HTML outputs; package releases; self-review pass (spec §57 checklist); commercial-readiness review (§58); final engineering report (§60 format).

## Architectural decisions made (record these in DECISIONS.md)
- Own parser+normalizer over Microsoft.OpenApi.Readers for full 2.0–3.2 control; only runtime dep is YamlDotNet (MIT).
- Everything parses to System.Text.Json Nodes; single semantic model downstream.
- Cycles → `x-sentinel-cyclic-ref` marker objects (deterministic, comparable) rather than graph expansion.
- Comparison produces SemanticChange stream; rules are pure kind→finding mappings registered in RuleRegistry; engine stays rule-agnostic (extensibility §53).
- Severity and Compatibility are independent axes; Verify reserved for genuinely undecidable cases (pattern/multipleOf transitions, composition algebra, defaults).
- Direction-aware constraint logic: request tighten=breaking / response widen=potentially breaking (documented in compatibility-model later).
- Fingerprint = SHA256(RuleId|Path|Method|Location|Old|New) excluding prose → stable baselines.
- Wildcard status handling: removal masked when covered by remaining 2XX/4XX/5XX/default range.
- Rename detection conservative (same primary type AND ≥80% child-property Jaccard OR typographic variant) else remove+add.
- Engine meta-findings: CG9001 expired-suppression warning (Low/Informational), CG9002 stale-baseline (aggregated single Info), CG9003 unknown-kinds (aggregated Verify, opt-in via ReportUnknownChangeKinds).
- Version recommendation: Major iff ∃ Breaking∧severity≥High; Minor iff any additive endpoint/op/content-type/status-code change; Patch iff only ≤Low findings; else None.
- Glob semantics: ordinal, anchored; `*` no slash, `**` crosses (trailing `x/**` matches x itself), `?` single char; 1s regex timeout.
- Expired suppression ⇒ finding reported again + CG9001 warning attached to that finding (not global noise).

## Do NOT redo
- Do not reinstall .NET (present at ~/.dotnet; just export PATH).
- Do not recreate solution/projects/build props/project references/packages.
- Do not rewrite Core, OpenApi, or Rules — ALL compile clean under warnings-as-errors and are unit-tested.
- Do not touch the existing 47 passing tests except extending coverage.
- Do not commit (user hasn't asked; AGENTS.md allows direct commits but nothing requested yet).
- IntegrationTests csproj must stay on the xunit 2.9.3/MSTest.Sdk 17.14.1 stack (v3/18.x breaks .NET 10 SDK dotnet test).

## Verified command history (this session)
- `dotnet build -c Release` → 0 errors, 0 warnings (after fixes listed above).
- `dotnet test -c Release` → Passed! 47 total (21 Core, 10 OpenApi, 16 Rules).

## Exact next recommended action sequence
1. ~~Reporting reporters~~ DONE (see above).
2. ~~CLI project~~ DONE (see above).
3. ~~Samples~~ DONE: samples/basic/{v1.yaml,v2-safe.yaml,v2-breaking.yaml} — realistic Order Management API (orders/customers/products, bearer+oauth2 auth, pagination params w/ $ref shared components, enums, multiple statuses, X-Total-Count header). Verified: safe pair → PASSED/MINOR rec; breaking pair → FAILED/MAJOR rec with intended findings (CG1002 op removed, CG2002 channel required, CG2007 status enum contraction request-side, CG5001 Money.amount number→integer flowing through all $ref sites, CG4005 header removed, CG6001 oauth requirement added exactly once).
4. ~~Fixture-driven FP/FN suites~~ DONE: tests/OpenApiSentinel.Rules.Tests/FalsePositiveNegativeSuites.cs (12): FP — documentation-only changes, property/keyword reordering, tags/summary changes produce no Breaking/PotentiallyBreaking findings; FN — request enum contraction CG2007, range narrowing High/Breaking, response nullable removal NOT breaking vs request nullable removal Breaking (direction-aware), optional→required request property, required-entry-without-property edge case, response content-type removal CG4003, parameter explode change CG2014, allOf member removal CG5010.
5. ~~CI/CD~~ DONE: action/action.yml composite action (inputs base/head/fail-on/config/allow-remote/report-dir/dotnet-version; outputs breaking/high/medium counts + recommended-version + report-path; ::error:: annotations capped at 20; NuGet-install with source-build fallback); .github/workflows/ci.yml (build+test matrix on 3 OSes, sample scenarios job asserting exit 1 + MAJOR in JSON, NuGet pack artifact); samples/github-actions/workflow.yml, samples/azure-devops/pipeline.yml, samples/gitlab/gitlab-ci.yml, samples/generic/compare.ps1.
6. ~~Packaging/releases~~ DONE: build/release.sh + release.ps1 (6 RIDs self-contained, zip/tar.gz, SHA256SUMS, RELEASE_NOTES.md template, nupkg via DotnetTool pack settings); Dockerfile (multi-stage sdk:10.0→runtime-deps:10.0, non-root user sentinel, ENTRYPOINT openapi-sentinel); .gitignore added.
7. ~~Benchmarks~~ DONE: bench/OpenApiSentinel.Bench (in slnx) generating small/medium/large specs (~8/90/455 KB) with realistic evolutions; measures parse/resolve+normalize/compare/rules/reports medians + allocations. Verified results on this machine: Small parse 6.6ms/compare 1.3ms; Medium 67ms/18ms (113 changes); Large 179ms/73ms (555 changes). docs/benchmarks.md written honestly (not BenchmarkDotNet; rerun-on-your-hardware caveat). NOTE: generator initially emitted path keys at column 0 → `paths:` parsed empty; fixed by indenting section output.
8. ~~Documentation~~ LARGELY DONE: README.md rewritten (problem, quick start, example output, CI, privacy/offline, versions matrix, install options, docs links); docs/getting-started.md, cli-reference.md (incl. exit codes table + JSON schema contract), configuration.md (full YAML reference + semantics), rules.md GENERATED from registry via `rules --format json` (73 rules, cannot drift), compatibility-model.md, github-actions.md, azure-devops.md, gitlab.md, security.md (threat model table incl. SSRF/traversal/bombs), privacy.md, troubleshooting.md, benchmarks.md, commercial/{product-positioning.md, feature-matrix.md Community-vs-Pro, release-checklist.md, pricing-strategy.md}, DECISIONS.md (18 decisions), THIRD-PARTY-NOTICES.md (YamlDotNet MIT only runtime dep), LICENSE (proprietary draft).
9. AGENTS.md updated per its own instruction with verified commands (build/test/single-test/CLI runs/bench).
10. §57 SELF-REVIEW PASS DONE — bugs found & fixed:
    - SentinelConfigLoader.GetInt threw on ABSENT keys → init template (no maxResponseBytes) failed to load with exit 3. Fixed: missing key ⇒ null default.
    - CommandLineParser: single-dash options (-o) didn't consume values → "-o file" leaked file as positional arg (usage error exit 2). Fixed: short options parse like long ones.
    - Packaging: Directory.Build.props Version condition was inverted (`== ''` set 1.0.0; SentinelVersion never applied). Fixed with explicit != '' arm.
    - PackageId now OpenApiSentinel (matches docs/action); PackageReadmeFile wired via csproj None Include ../../README.md.
    - ToolVersion.Get() now uses InformationalVersion trimmed at '+' (prerelease suffix preserved, commit hash dropped).
    - release.sh/.ps1: publish steps dropped --no-build/--no-restore (RID-specific assets require their own restore; NETSDK1047 otherwise); apphost renamed OpenApiSentinel.Cli→openapi-sentinel(.exe) in archives; Dockerfile does same rename before COPY.
    VERIFIED end-to-end: dotnet tool install --global --add-source pkg --prerelease OpenApiSentinel → `openapi-sentinel version` = 0.9.0-test, sample compare exits 1; self-contained linux-x64 publish runs with no .NET installed (reports 0.9.0-test).
11. §58 COMMERCIAL-READINESS PASS DONE:
    - `--pr-summary <path>` artifact added (master spec §27): compact PR comment markdown via MarkdownReporter.RenderPrSummary; wired through parser/usage/render. Verified on breaking sample (❌ policy failed, 33 findings, MAJOR).
    - Error-message audit: missing file/invalid YAML/bad rule id/unknown command all actionable. Fixed: `ftp://…` (non-http(s) absolute URI) now yields explicit "Unsupported source …" instead of misleading "file not found".
    - release.sh: added zip→python3 fallback (no `zip` binary on this host); FULL RUN SUCCEEDED for 0.9.0: all 6 RID archives (win-x64/arm64 zip, linux/osx tar.gz), SHA256SUMS verified OK ×6, RELEASE_NOTES.md, pkg/OpenApiSentinel.0.9.0.nupkg. Standalone linux-x64 binary runs with correct version and compare exit 1; win-x64 zip validated (206 entries, openapi-sentinel.exe present).
    - Docker: Dockerfile present but docker unavailable in this environment — CI (GitHub Actions) should build/push the image.
12. LATER ITERATION ADDITIONS:
    - `--pr-summary <path>` artifact (spec §27) — compact PR markdown; verified on breaking sample.
    - Non-http(s) absolute URIs (ftp:// etc.) → explicit "Unsupported source" error instead of misleading "file not found".
    - release.sh zip→python3 fallback; FULL release run verified for 0.9.0 (6 RID archives, SHA256SUMS OK ×6, standalone linux-x64 binary runs w/o dotnet on PATH, win-x64 zip validated incl. openapi-sentinel.exe rename).
    - tests/OpenApiSentinel.OpenApi.Tests/VersionMatrixTests.cs (6 NEW): OpenAPI 3.1 type-array nullability direction-aware events, 3.1 webhook add detection (webhook:<name> paths), 3.2 version detection + parameter compare, $ref sibling override semantics (maxLength 10 overrides 999 per 2020-12), JSON≡YAML comparison equivalence.
    - docs/architecture/overview.md: pipeline diagram, project boundaries table, key invariants, extension points, notable mechanisms.
    - Removed empty placeholder dirs docs/rules/, docs/examples/.
12b. RULE-COVERAGE AUDIT — real bugs found & fixed:
    - **CG2003/CG2004 misclassification (significant false-positive)**: both rules claimed parameter.required.changed; engine first-wins routing sent relaxing changes (required→optional) to CG2003 "Optional parameter became required" Breaking High. Fixed via new optional RuleDefinition.AppliesTo guard: engine keeps ordered candidate lists per kind and picks the first whose guard accepts the change. Same pattern applied to CG3003/CG3004 (requestbody.required.changed). Verified: relaxation now yields CG2004 Low/NonBreaking; tightening yields CG2003 Breaking High.
    - **ApiKeyNameChanged was orphaned** (kind defined, never emitted, no rule — master spec §12 requires it): SecuritySchemeInfo gained KeyName (apiKey `name:` field), normalizer captures it, engine emits security.apikey.name.changed, new rule CG6015 API key name changed (High/Breaking). Verified end-to-end (X-API-Key → X-Api-Token → CG6015).
    - tests/OpenApiSentinel.Rules.Tests/RuleCoverageTests.cs (4 NEW, permanent drift prevention): every ChangeKinds constant has ≥1 rule; shared kinds require AppliesTo guards on ALL claimants; rules only reference existing constants; rule ids unique + well-formed CG\\d{4}. Rule count now 74; docs/rules.md regenerated.
12b2. ADDITIONALPROPERTIES DIRECTIONALITY (latest iteration):
    - Comparer: absent additionalProperties normalized to `true` before comparison — absent↔explicit-true restyling no longer produces findings (§55 false positive).
    - CG5009 rewritten direction-aware: request close→false = Breaking High; response open from false = PotentiallyBreaking Medium; relaxations NonBreaking; schema-valued transitions Verify/Unknown w/ Confidence.Uncertain; descriptor metadata now consistent with emitted findings. 6 new tests in AdditionalPropertiesDirectionTests.
12b3. FORMAT-CHANGE DIRECTIONALITY (latest iteration):
    - CG5002/CG2006 were static Medium/PotentiallyBreaking for ALL format changes. Added DirectionLogic.ForFormat with known widening pairs (int32→int64, float→double, date→date-time): request widening = NonBreaking Info; request narrowing = Breaking High; response widening = PotentiallyBreaking Medium; response narrowing = NonBreaking Low; unknown pairs stay conservative PotentiallyBreaking. 4 new tests.
12b4. NUMERIC TYPE-CHANGE DIRECTIONALITY (latest iteration):
    - CG5001 was static Critical/Breaking for ALL type changes — integer→number widening on responses (decimals may appear) over-flagged. Added DirectionLogic.ForNumericTypeChange: numeric-family changes are direction-aware (request integer→number = Low/NonBreaking; request number→integer = Critical/Breaking; response integer→number = Medium/PotentiallyBreaking; response number→integer = Low/NonBreaking); cross-family stays Critical/Breaking. Note: comparer emits type values JSON-encoded, helper trims quotes defensively. 4 new tests.
12b5. DOCS REFRESH (latest iteration): docs/rules.md regenerated (picks up CG5009 rewrite); compatibility-model.md gained "Format and numeric-type directionality" section documenting the newly directional rules; CG5004 response-enum-add verified PotentiallyBreaking Medium end-to-end.
12b5. GOLDEN HTML SNAPSHOT + THRESHOLD DOCS (latest iteration):
    - tests/OpenApiSentinel.Reporting.Tests GoldenHtmlTests.Html_MatchesGoldenSnapshot: byte-exact golden for the single-file HTML report (deterministic fixture w/ fixed clock); SENTINEL_ACCEPT_GOLDEN=1 regenerates; verified create→pass→tamper-fail→regenerate cycle. Golden committed at tests/OpenApiSentinel.Reporting.Tests/GoldenFiles/report.html (~15 KB).
    - Threshold banding documented in docs/configuration.md + cli-reference.md: severities rank info<low<medium<high<critical with verify above critical; thresholds inclusive upward; --fail-on verify matches only Verify findings; `all` fails on everything.
12b6. WEBHOOK CONTENT DIFF (latest iteration):
    - CompareWebhooks previously detected only webhook add/remove; changes INSIDE an existing webhook operation (schema, parameters, responses, security) went undetected. Now diffs every method pair via CompareOperations (global-security aware) and emits method-level OperationAdded/Removed for per-method webhook edits. Webhook event paths aligned with the normalizer convention `<webhook:{name}>` (was mixed bare/bracketed). 2 new tests: payload schema change inside webhook detected; method removal from webhook detected.
12b7. REQUEST-BODY REQUIREDNESS (latest iteration):
    - CG3002 "body added" was always Info/NonBreaking even when the NEW body was required → clients without a body get 400s (missed breakage). CG3001 "body removed" was always Critical/Breaking even for optional bodies. Engine now emits Details["required"] on add/remove events; rules branch directionally: added+required = High/Breaking, added+optional = Info/NonBreaking; removed+was-required = High/Breaking, removed+optional = Low/NonBreaking. 4 new tests cover all quadrants.
12b8. 3.1 UNION TYPE DIRECTIONALITY (latest iteration): ForNumericTypeChange now union-aware — pipe-joined type sets within the numeric family classify directionally ([integer]→[integer,number] response = PotentiallyBreaking Medium; request narrowing [integer,number]→[integer] = Critical Breaking); non-numeric members fall back to cross-family Critical. 2 new tests. Suite 134/134.
12b10. FINAL SWEEP (latest iteration): AGENTS.md test count refreshed (~134); response nullability widening ([string]→[string,null]) re-verified end-to-end as CG5007 Medium/PotentiallyBreaking; 20 untracked paths awaiting user commit decision.
12b9. ACTION STEP SUMMARY: composite action now generates --pr-summary artifact and appends it to $GITHUB_STEP_SUMMARY automatically; docs/github-actions.md updated.
12b11. CROSS-VERSION MIGRATION TESTS (latest iteration): verified a faithful Swagger 2.0 → OpenAPI 3.0.3 migration compares with ZERO changes (body param→requestBody, definitions→components refs, host/basePath→servers, consumes/produces→content all normalize identically) — validates §4 commercial claim; plus a second test proving real breakage (required param + constraint) is still detected across the version boundary. 2 new tests in CrossVersionMigrationTests.
12b12. HEADER REQUIREDNESS DIRECTION (latest iteration): CG4007 emitted PotentiallyBreaking for BOTH directions; optional→required is a stronger server commitment (safe for consumers) — now Low/NonBreaking with distinct titles. 2 new tests. Suite 140/140.
12b13. READONLY/WRITEONLY DIRECTIONALITY (latest iteration): CG5012 was static Low/PotentiallyBreaking for all flag flips. Rewritten with OpenAPI-precise semantics: readOnly:true added on REQUEST schema = High/Breaking ("clients may no longer send"); writeOnly:true on RESPONSE = Medium/PotentiallyBreaking ("responses stop returning"); removals = NonBreaking Low; mismatched-position flags (writeOnly on request / readOnly on response) = Informational no-op. Also fixed bool.ToString() capitalization bug ("True" vs "true") in flag comparison — case-insensitive now. 3 new tests. Suite 143/143.
12b14. SCHEMA-DEPRECATED CAPITALIZATION FIX (latest iteration): CG5013 had the same bool.ToString() bug as CG5012 ("True" != "true") — property deprecation events were misclassified as "Deprecation lifted". Fixed case-insensitively; regression test added (property deprecated:true now correctly yields "Schema deprecated"/PotentiallyBreaking). Suite 144/144.
12b15. DECISIONS LOG (latest iteration): DECISIONS.md extended with entries 19-21 documenting the directional refinements (additionalProperties/format/numeric-family/readOnly-writeOnly/requiredness-on-events), webhook content diffing, and the AppliesTo kind-routing mechanism.
12b16. POLICY BANDING PINNED (latest iteration): PolicyBandingTests (4) lock the documented threshold semantics — verify outranks critical so fail-on high/critical/verify all match Verify findings; fail-on info matches everything; all = any finding; default breaking-policy ignores the ladder. Docs sharpened to state explicitly that high+ thresholds include Verify. Suite 148/148.
12b17. PR-SUMMARY CONSISTENCY (latest iteration): loader validates output.* keys against known formats (unknown → warning, e.g. typo "htmml"); pr-summary accepted everywhere — cli-reference options table, configuration.md output example, init template. 2 new tests (unknown-key warning; pr-summary accepted).
12b18. VERSIONED CHANGELOG HEADINGS (latest iteration): ComparisonResult/EngineOptions gained OldInfoVersion/NewInfoVersion (declared info.version, distinct from raw spec version); CLI passes them; ChangelogGenerator emits "## 1.0.0 → 2.0.0" style headings (falls back to "## Unreleased" when unknown). Verified on samples: heading "## 1.0.0 → 2.0.0". 1 new test; fixture enriched.
12b19. INFO-VERSION SURFACING (latest iteration): JSON contract docs updated with additive oldInfoVersion/newInfoVersion fields (schemaVersion stays 1.0 per policy); HTML report contract cards now show "spec <ver> · api v<ver>" sub lines; golden snapshot regenerated intentionally.
12b20. EXAMPLE ARTIFACTS + PARAM NAME FIX (latest iteration): docs/examples/ now ships real generated artifacts (api-diff.md, pr-summary.md, CHANGELOG-sample.md) with an explanatory README, linked from the main README. Fixed while generating: parameter schema sub-changes lacked ParameterName → rules rendered "Parameter '(unknown)'" (CG2011/CG2012); engine now stamps newP.Name onto remapped sub-changes. Examples regenerated under default policy (FAILED verdict shown honestly).
12b21. ENGINE META-RULES IN CATALOG (latest iteration): CG9001/02/03 registered as real rule definitions (empty ChangeKinds — never kind-routed) so `rules`, `explain`, and docs/rules.md document engine-emitted findings; explain CG9001 previously returned "Unknown rule id". Catalog: 77 entries (74 rules + 3 engine checks); counts in README/positioning updated.
12b22. SARIF URI INTEROP FIX (latest iteration): artifactLocation.uri carried human locations with spaces ("GET /orders responses.200") — invalid URIs that strict consumers (GitHub code scanning) reject. Now uri = per-segment-escaped API path (spaces/braces encoded, slashes preserved); document-level findings omit locations; human location moved to properties.location. Test updated to assert valid uri + properties.location presence.
12b23. ANSI-FREE FILE ARTIFACTS (latest iteration): `-o console.txt` embedded ANSI escapes because color detection keyed off the TTY even when rendering to disk. RenderOutputs now forces noColor for every file target (primary and artifacts). Regression test asserts console-format file output contains FAILED text but zero ESC sequences. Suite 152/152.
12b24. EXAMPLE ARTIFACT REFRESH (latest iteration): docs/examples regenerated post-fixes — honest FAILED verdict, proper parameter names ("Parameter 'pageSize'"), zero "(unknown)" strings; all 152 tests still green.
12b25. GLOBAL --version FLAG (latest iteration): `openapi-sentinel --version` (no command) now prints the version instead of usage — standard CLI expectation. 2 new tests. Suite 154/154.
12b26. CONSOLE SUPPRESSED SUMMARY (latest iteration): console Summary block shows "Suppressed: N (acknowledged)" when suppressions/baseline entries exist, pointing at the detailed list. 2 new tests. Suite 156/156.
12b27. SUBCOMMAND HELP (latest iteration): `<command> --help` now prints command-specific usage (compare shows its full option list; validate/rules/explain/init get tailored text) instead of the generic top-level usage. Theory test covers all five. Suite 161/161.
12b28. AFFECTED-ENDPOINTS SURFACING (latest iteration): EndpointSummary data was computed by the engine but rendered nowhere. Console verbose mode now prints "Affected endpoints" (state, name, finding count, top-25 cap); Markdown gains an "Affected endpoints" table. Fixture enriched; 2 new tests. Suite 163/163.
12b29. HTML ENDPOINTS TABLE (latest iteration): HTML report gains "Affected endpoints" section (state badges Removed/Added/Changed, endpoint, finding count; top-100 cap) — blast-radius now visible in every major format. Golden regenerated; content assertions extended. Suite 163/163.
12b30. EXAMPLES REFRESH + JSON ENDPOINTS TEST (latest iteration): docs/examples regenerated (now include the "Affected endpoints" table); JsonReporterTests assert endpoints array (state values) rides along in the machine-readable contract. Suite 163/163.
12b31. CHANGELOG HEADING EDGE CASE (latest iteration): when only the old version is known, heading is now "## Unreleased (changes since X)" instead of the misleading bare "## Unreleased"; architecture doc kind-count de-hardcoded ("70+ stable kinds"). 1 new test. Suite 164/164.
12b32. PACKAGING METADATA (latest iteration): NuGet package Description was the default "Package Description" — replaced with a real product description (74 rules, 2.0-3.2, offline); verified in regenerated nuspec. Marker sweep (TODO/HACK/FIXME) across shipped source: clean — both grep hits are intentional (intermediate-state comment + HTML placeholder attribute).
12b33. HTML EXAMPLE COMMITTED (latest iteration): docs/examples/api-diff.html (67 KB) — the flagship interactive report is now inspectable in-repo without installation; examples README updated.
12b34. README DOC INDEX (latest iteration): documentation links section now covers every shipped doc — architecture overview, examples, commercial set (positioning/matrix/pricing/checklist), DECISIONS, THIRD-PARTY-NOTICES; rule-count reference de-hardcoded.
12b35. DEAD-CODE CLEANUP (latest iteration): removed no-op catch/rethrow filter in ValidateAsync and the redundant policy re-assignment in CompareWorkflow (CLI-flag-wins already expressed via ??-chain). §52 source-quality pass. Suite green.
12b36. CATALOG DRIFT GUARD (latest iteration): RulesCatalogDocTests (3) — docs/rules.md must contain every registered rule id, and its declared rule count must equal the registry count; adding a rule without regenerating the catalog now fails CI. Suite 167/167.
12b37. CATALOG REGEN SCRIPT (latest iteration): build/gen-rules-doc.py created — runs the built CLI's rules --format json and renders docs/rules.md (the intro's referenced-but-missing tool now exists). Verified output regenerates cleanly; drift guard stays green.
12b38. REPORT-UNKNOWN-KINDS EXPOSED (latest iteration): ReportUnknownChangeKinds was engine-only — unreachable from CLI/config. Added --report-unknown-kinds flag + top-level config key reportUnknownKinds, wired through Request→EngineOptions; usage text + docs updated. 2 new tests (flag accepted on clean pair; config key accepted without warnings). Suite 169/169.
12b39. NEGATIVE-OPTION PATHS PINNED (latest iteration): --fail-on bogus → exit 3 with the exact invalid-value message; --format yaml → exit 3 listing supported formats. 2 new tests. Suite 171/171.
12b40. DOCUMENT-LEVEL LOCATION MARKER (latest iteration): doc-level findings (no path/method) rendered empty locations — empty code spans in Markdown, blank space in console, empty SARIF uris risk. Describe helpers now fall back to "(document)"; Sarif document-level findings omit locations. 3 new tests (markdown/console/sarif). Suite 174/174.
12b41. SERVERS DIRECTIONALITY (latest iteration): CG1010 was static Medium/PotentiallyBreaking. Now set-aware: all servers removed = High/Breaking; purely additive (new ⊇ old) = Low/NonBreaking; mixed/replaced stays PotentiallyBreaking Medium. 2 new tests. Suite 178/178.
12b42. FINAL AUDIT NOTE (latest iteration): CG3005 request content-type removal verified already correct (High/Breaking); full rule-family directional audit confirmed complete — every family (endpoint/param/body/response/schema/security/metadata) now has position- or value-aware classification where semantics demand it. Suite 178/178 green; clean-build 0/0.
12b43. JSON CONTRACT DOCS COMPLETED (latest iteration): cli-reference.md documents the endpoints array shape and oldInfoVersion/newInfoVersion fields.
12b44. --NO-SUPPRESSED FLAG (latest iteration): IncludeSuppressed was config/reporter-only — no CLI surface. Added --no-suppressed flag wired through Request→ReporterOptions; RenderFormat refactored to accept a single ReporterOptions (fixing a latent bug where file-target renders ignored flag-driven options). 2 new tests: omitted section with flag; default still shows. Suite 180/180.
12b45. FLAG DOCS SYNC (latest iteration): --no-suppressed and --report-unknown-kinds added to the cli-reference options table.
12b46. RELEASE TAGGED (latest iteration): annotated tag v0.9.0 pushed to origin — marks the published implementation per docs/commercial/release-checklist.md. Remaining publication items need credentials: NuGet push, GitHub Release page w/ dist artifacts (gh CLI), Docker image.
12b47. GITHUB RELEASE PUBLISHED (latest iteration): gh release create v0.9.0 with real notes + all 7 artifacts (6 RID archives + SHA256SUMS) → https://github.com/kok751014/openapi-sentinel/releases/tag/v0.9.0. SOLE remaining item: NuGet.org push (no API key in env/config — user must provide NUGET_API_KEY, then: dotnet nuget push dist/0.9.0/pkg/OpenApiSentinel.0.9.0.nupkg --source https://api.nuget.org/v3/index.json). Docker image build also awaits a docker daemon.
12b48. AUTOMATED RELEASE PIPELINE (latest iteration): ci.yml gained tag-triggered jobs — `release` (runs build/release.sh on v* tags, verifies checksums, creates GitHub Release w/ artifacts, pushes nupkg to NuGet.org via NUGET_API_KEY secret) and `docker` (builds always; pushes when DOCKERHUB_USERNAME/TOKEN configured). Future releases are now: git tag v<version> && push. Pushed as da83496.
12b49. REMOTE CI VERIFIED (final): the release-pipeline commit's CI run completed successfully on GitHub runners (run 32635685951, ~1m45s) — release/docker job definitions valid and correctly gated on v* tags. All 5 recent remote runs green. Publication flow fully armed: future `git tag v<ver> && git push origin <tag>` publishes GitHub Release + artifacts + NuGet automatically.
12b48b. WILDCARD MASKING PINNED (latest iteration): WildcardMaskingTests (3) — status-code removal masked by remaining `default`/range coverage; uncovered removal reported; request content-type removal never masked by wildcard content types (415 is real). Documents the asymmetry between the two masking policies. Suite 183/183.
12b49. REAL-WORLD SPEC VALIDATION + CLI PARSER BUG FIX (latest iteration):
    - FIXED significant CLI bug: boolean flags (--allow-remote etc.) swallowed the following positional — "validate --allow-remote <url>" failed with usage error; "compare --allow-remote old.yaml ..." ate old.yaml. Parser now maintains a BooleanFlags set that never consumes values. Regression tests added.
    - Real-world validation: GitHub REST API description (~10 MB, 809 paths, 1221 operations, one of the largest public specs) validates cleanly in seconds via --allow-remote. Proves production-scale robustness.
    - DigitalOcean sample URL 404'd (their repo restructure) — not our issue.
12b49. PROCESSING COUNTERS EXPOSED (latest iteration): ComparisonResult.ProcessingSummary added to Core; RulesEngine populates from its statistics (changes/findings/unmatched/disabled-by-override/ignored-by-path/suppressed counts); console verbose prints "Processing notes" when any counter is non-zero; JSON gains additive `processing` object. 1 new test.
12c. CONSISTENCY SWEEP: README + product-positioning rule counts 73→74; `explain CG6015` verified; canonical sample scenarios re-verified post-guard-change (safe→0, breaking→1 w/ 33 findings MAJOR, no misrouted CG2004). Full suite 113/113 green.
13. REMAINING (external actions / not requested): git commits (user hasn't asked — do NOT commit unprompted); GitHub Action marketplace + NuGet.org publication (need remote/credentials); Docker image build/push (docker unavailable in this env; Dockerfile ready for CI). In-repo Definition of Done items are COMPLETE.

### Critical engine bugs found & fixed via sample-spec testing
- **SchemaComparer state leak**: `_changes` field accumulated across calls; ComparisonEngine reuses one instance per run → quadratic duplicate changes and cross-operation contamination (locations from other methods, 175 bogus findings on the breaking sample). Fixed: Clear() at Compare start + return _changes.ToArray() snapshot (callers holding earlier results stay valid).
- **Global security inheritance ignored**: `effectiveOld = oldOp.Security ?? []` treated null operation security as PUBLIC instead of inheriting document-level security. Fixed: GlobalSecurity threaded through ComparePathCollection→ComparePathItems→CompareOperations→CompareEffectiveSecurity; effective = op.Security ?? doc.GlobalSecurity.
- **Parameter sub-change kinds/categories**: parameter schema sub-changes kept schema.* kinds and RequestBody category; remapped ConstraintChanged→ParameterConstraintChanged (CG2011 now actually fires), ItemsChanged→ParameterItemsChanged, Category=Parameter.
- **Empty parameter locations**: parameter schema pass used "" prefix; now passes full parameter location ("GET /x parameters.pageSize").

---

## 2026-08-23 ~13:30 UTC — CALLBACK SUPPORT (master spec §8 false-negative class closed)

**Gap found:** OpenAPI 3.0+ operation-level `callbacks` had ZERO support (grep for "allback" in *.cs returned nothing) — payload/schema/method edits inside callback declarations were invisible, despite callbacks being a contract consumers must implement. Webhooks got content-diffing in 12b6; callbacks were the remaining §8 item.

### Completed
- **Model**: `Operation.Callbacks` (`IReadOnlyList<CallbackInfo>`); `CallbackInfo { Name, Expressions: expr → methods → Operation }`.
- **Normalizer**: `ParseCallbacks` — per-operation `callbacks` parsed recursively (nested callbacks work); inner ops normalize like real endpoints under synthetic display path `<callback:{METHOD} {ownerPath}:{name}>`; `x-` keys and malformed/empty declarations skipped.
- **Engine**: `CompareCallbacks` + `CompareCallbackExpressions` wired into `CompareOperations`: add/remove by name (Details carry expression list); 1:1 expression change collapses to single `callback.expression.changed` (no remove+add noise); multi-expression changes emit per-expression events; method pairs inside matched expressions recurse through full `CompareOperations` (schemas/params/responses/security/nested callbacks reuse ALL existing rules). Zero-operation callbacks emit nothing (mirrors webhooks).
- **Rules**: CG1011 CallbackAdded (Medium/PotentiallyBreaking), CG1012 CallbackRemoved (Low/NonBreaking), CG1013 CallbackExpressionChanged (High/Breaking). Catalog now **77 rules + 3 engine checks = 80 entries**; docs/rules.md regenerated via build/gen-rules-doc.py.
- **DECISIONS.md**: entry 22 documents the design.
- **Count sync**: README.md, product-positioning.md ("74 rules"→77), NuGet package Description in Directory.Build.props, AGENTS.md test count (~134→~222).

### Files changed
- src/OpenApiSentinel.OpenApi/{Model/ApiDocument.cs, ChangeKinds.cs, ComparisonEngine.cs, OpenApiNormalizer.cs}
- src/OpenApiSentinel.Rules/EndpointRules.cs
- tests/OpenApiSentinel.OpenApi.Tests/CallbackDiffTests.cs (11 NEW)
- tests/OpenApiSentinel.Rules.Tests/CallbackRuleTests.cs (4 NEW)
- docs/rules.md (regenerated), docs/DECISIONS.md, README.md, docs/commercial/product-positioning.md, Directory.Build.props, AGENTS.md

### Verification (actual results)
- `dotnet build -c Release` → 0 errors, 0 warnings.
- `dotnet test -c Release` → **222/222 passed** (Core 21, Reporting 35, OpenApi 49, Rules 76, Cli 38, Integration 3); catalog drift guards caught stale rules.md as designed, green after regen.
- CLI end-to-end: expression change → `[HIGH] CG1013 … callbacks.orderEvent`, MAJOR rec, exit 1 (--fail-on high); combined rename+add scenario JSON shows CG1013 high/breaking + CG1011 medium/potentiallyBreaking at `/subscribe`; `explain CG1013` renders full descriptor.

### Bugs found & fixed during this iteration
- Test-fixture authoring errors only (raw-string indentation math, over-eager sed on YAML); product code compiled correct first pass except static/instance mismatch caught by compiler.

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker build/push needs a docker daemon (Dockerfile + CI jobs ready); GitHub Action marketplace listing not requested.

### Git status
- Modified: 5 src files, docs (rules.md/DECISIONS/README/positioning), Directory.Build.props, AGENTS.md; NEW: 2 test files; LOOP_PROMPT.md (loop tooling). Nothing committed (no user request).

### Exact next recommended action
1. Commit this iteration when the user asks (direct-to-main allowed per AGENTS.md).
2. Next highest-value hardening candidates: (a) `paths` with server-relative URL templating edge cases in 3.2, (b) sample spec gains a callback example for demo/marketing visibility, (c) HTML report grouping check for `<callback:…>` paths (verify no rendering oddity), (d) re-run bench to confirm no perf regression from per-op callback list allocation.

---

## 2026-08-23 ~13:40 UTC — FALSE-NEGATIVE SWEEP: response-header payloads + OAuth flow additions

**Defects found & fixed** (both real false negatives, found by code audit of under-diffed surfaces):
1. **Response-header schema changes were never diffed** — CompareResponses only compared header add/remove/required-toggle. `X-Total-Count: integer→string` (client deserialization breakage) was completely silent. Header schemas now flow through SchemaComparer with Response direction; sub-changes carry Category=Response, location `... responses.<code>.headers.<name>...`, and statusCode detail (glob/suppression-friendly). Existing CG50xx rules classify them direction-aware automatically (e.g. integer→number response = CG5001 PotentiallyBreaking Medium; cross-family string flip = Critical/Breaking).
2. **Whole OAuth flow additions to an existing scheme were undetected** (flow removals and scope-def changes were, additions weren't). Engine now emits `security.flow.url.changed` with `urlKind=flow.added`; CG6012 rewritten as a 3-way branch: flow.added → Low/NonBreaking "OAuth flow added"; flow.removed → Medium/PotentiallyBreaking "OAuth flow removed" (clearer title than the old generic URL-changed wording); URL changes unchanged. Also fixed pre-existing wording bug: explanation said "on scheme ''" because it read c.Path (always empty for scheme-level events) instead of Details["scheme"].

**Also verified non-defects this iteration:** SARIF `artifactLocation.uri` correctly percent-encodes synthetic `<callback:…>`/`<webhook:…>` paths (`Uri.EscapeDataString` → `%3C…%3E`) — no GitHub code-scanning interop issue; `properties.location` carries the human location at result level (earlier probe misread nesting).

### Files changed
- src/OpenApiSentinel.OpenApi/ComparisonEngine.cs (header schema pass; flow-added emission)
- src/OpenApiSentinel.Rules/SecurityRules.cs (CG6012 branch + descriptor + scheme-name wording fix)
- tests/OpenApiSentinel.OpenApi.Tests/HeaderSchemaAndFlowTests.cs (5 NEW)
- tests/OpenApiSentinel.Rules.Tests/HeaderSchemaAndFlowRuleTests.cs (4 NEW)
- docs/rules.md (regenerated — CG6012 descriptor text)

### Verification (actual results)
- `dotnet build -c Release` → 0 errors, 0 warnings.
- `dotnet test -c Release` → **231/231 passed** (Core 21, Reporting 35, OpenApi 54, Rules 80, Cli 38, Integration 3).
- CLI end-to-end: header type flip → `[CRITICAL] CG5001 … responses.200.headers.X-Total-Count`, MAJOR rec, exit 1.
- Bench re-run post-callback-feature: Small compare 1.2ms, Medium 15.4ms (113 changes), Large 87.1ms (555 changes) — consistent with historical numbers, no regression.

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker build/push needs docker daemon; marketplace listing not requested. Nothing committed (no user request).

### Git status
- Modified: ComparisonEngine.cs, SecurityRules.cs, docs/rules.md + previous iteration's 15 paths (callback feature still uncommitted).
- NEW: tests/OpenApiSentinel.OpenApi.Tests/HeaderSchemaAndFlowTests.cs, tests/OpenApiSentinel.Rules.Tests/HeaderSchemaAndFlowRuleTests.cs.

### Exact next recommended action
1. Commit both iterations when the user asks.
2. Remaining hardening candidates (all optional): (a) discriminator mapping-only change granularity, (b) path rename heuristic (/users/{id} → /users/{userId} currently remove+add — documented behavior), (c) docs/examples refresh to showcase callback + header findings, (d) consider `4XX`/`default` coverage note for header removals (currently only status-code removal is wildcard-masked — asymmetric but documented in 12b48b).

---

## 2026-08-23 ~13:55 UTC — DISCRIMINATOR GRANULARITY (CG5011 false positive eliminated)

**Defect fixed:** CG5011 classified EVERY discriminator change as Breaking High — including purely additive mapping extensions, which are non-breaking by the product's own §55 standard (mapping extension = existing variants keep routing; recommendation was MAJOR for a PATCH-worthy change).

**Implementation:**
- SchemaComparer discriminator block now emits Details: `aspect` (propertyName|mapping), `mappingAdded`, `mappingRemoved` (sorted CSVs).
- CG5011 rewritten as 3-way branch: propertyName involved → High/Breaking "Discriminator changed"; mapping removed/retargeted → Medium/PotentiallyBreaking "Discriminator mapping changed"; purely additive mapping → Low/NonBreaking "Discriminator mapping extended".
- Verified end-to-end: mapping extension now → PASSED / PATCH recommendation / exit 0 with --fail-on high (previously FAILED/MAJOR/exit 1).
- DECISIONS.md entry 23 + compatibility-model.md "Discriminator granularity" section.
- Known limitation documented (not fixed, rare edge): 3.x content-based parameters keep only the first media type's schema — second-media-type edits are invisible. Recorded in DECISIONS.md entry 23.

**Test-harness lesson (cost real debugging time):** C# raw-string literals strip the closing-delimiter indentation, so `.Replace()` searches inside fixture consts must use POST-STRIP indentation, not source-visible indentation. Silent no-op replaces produced "identical specs → zero findings" which masked three tests as empty-result failures. Fixed by using 10-space (effective) search strings + Assert.NotEqual guard.

### Files changed
- src/OpenApiSentinel.OpenApi/SchemaComparer.cs (discriminator details)
- src/OpenApiSentinel.Rules/SchemaRules.cs (CG5011 branch)
- tests/OpenApiSentinel.Rules.Tests/DiscriminatorGranularityTests.cs (5 NEW)
- docs/rules.md (regenerated), docs/DECISIONS.md, docs/compatibility-model.md

### Verification (actual results)
- `dotnet build -c Release` → 0 errors, 0 warnings.
- `dotnet test -c Release` → **236/236 passed** (Core 21, Reporting 35, OpenApi 54, Rules 85, Cli 38, Integration 3).
- CLI: mapping extension pair → CG5011 LOW, PASSED, PATCH, exit 0.

### Unfinished / external blockers (unchanged)
- NuGet push needs NUGET_API_KEY; Docker needs daemon; nothing committed (no user request).

### Git status
- Modified: SchemaComparer.cs, SchemaRules.cs, docs/{rules.md,DECISIONS.md,compatibility-model.md} + prior iterations' uncommitted paths.
- NEW: tests/OpenApiSentinel.Rules.Tests/DiscriminatorGranularityTests.cs (+3 earlier new test files).

### Exact next recommended action
1. Commit all three iterations when the user asks.
2. Optional remaining candidates: (a) content-parameter multi-media limitation fix (model change — only if real-world demand), (b) docs/examples refresh showcasing callback/header/discriminator findings, (c) header-removal wildcard-masking doc note (12b48b asymmetry), (d) another real-world spec validation run (e.g. DigitalOcean replacement URL or Stripe) to re-prove scale after recent engine additions.

---

## 2026-08-23 ~14:05 UTC — MILESTONE COMMITTED & PUSHED + SCALE RE-VALIDATION

**Git milestone backup policy executed** (per updated LOOP_PROMPT.md):
- Reviewed diff (17 modified + 5 new files), secret scan clean, gates green.
- **Commit `618c506`** pushed to origin/main: callback diffing (CG1011-13), response-header schema diff, OAuth flow-addition detection, aspect-aware CG5011 discriminator classification; catalog 77 rules + 3 engine checks; suite 236/236.

**Real-world scale re-validation after all three engine additions:**
- GitHub REST API description (~10 MB, 809 paths, 1221 operations): `validate --allow-remote` → OK in 4.9s.
- Remote-vs-remote self-compare → passed, 0 findings, 13.6s total incl. two 10MB fetches. Deterministic, no regression from callbacks/header/flow code paths.

### Current state
- Build: 0 errors / 0 warnings. Tests: 236/236. Working tree: clean except progress.md (this entry).
- External blockers unchanged: NuGet push needs NUGET_API_KEY; Docker image build needs a daemon (CI jobs ready).

### Exact next recommended action
1. Optional hardening only: (a) docs/examples refresh showcasing callback/header/discriminator findings for marketing; (b) header-removal wildcard-masking doc note; (c) content-parameter multi-media limitation fix if real demand appears. If none chosen: re-run gates, keep repo healthy.

---

## 2026-08-23 ~14:00 UTC — RENAMED-CALLBACK FN FIXED (self-caught) + CALLBACKS SHOWCASE — commit f0b2687

**Defect found & fixed (false negative in my own callback feature):** a 1:1 callback
expression rename emitted CG1013 but skipped the inner operation diff (`TryGetValue` on the
renamed key never matched) — ALL payload changes inside a renamed callback were invisible.
Caught immediately by building the showcase sample: `signature` becoming required inside a
renamed-expression callback produced nothing. Rename now pairs both sides and diffs methods;
regression test `Expression_Rename_Still_Diffs_Inner_Operations` pins rename + payload-change
together. Sample pair now yields all 5 intended findings incl. CG5015 at
`POST <callback:...> → body.signature`.

**Showcase/commercial:**
- samples/callbacks/{v1.yaml,v2-breaking.yaml} — Notification Hooks demo pair (CG7001,
  CG1013 High, CG1011 Medium, CG5001 Critical header drift, CG5015 High).
- docs/examples/callbacks-api-diff.md generated artifact + examples README row.
- README features row now mentions callbacks/webhooks and response-header payloads.
- compatibility-model.md: wildcard-masking asymmetry note (status codes masked by ranges;
  content-type removals never masked).

### Files changed
- src/OpenApiSentinel.OpenApi/ComparisonEngine.cs (rename-aware inner diff; DiffCallbackMethods extracted)
- tests/OpenApiSentinel.OpenApi.Tests/CallbackDiffTests.cs (+1 regression test)
- samples/callbacks/* (NEW), docs/examples/callbacks-api-diff.md (NEW), docs/examples/README.md, README.md, docs/compatibility-model.md

### Verification (actual)
- Build 0/0; tests **237/237** (OpenApi 55). Sample compare → FAILED/MAJOR/exit 1 with 5 findings.
- Committed **f0b2687**, pushed to origin/main.

### Next recommended action
Optional only: Pro-oriented polish (HTML example refresh with callback finding; pr-summary for callbacks pair). Otherwise repo is healthy; re-run gates next iteration.

---

## 2026-08-23 ~14:35 UTC — 2.0 COLLECTIONFORMAT FN + MARKDOWN INJECTION HARDENING — commits 2409a69, a5abed8

**Commit `2409a69` (previous iteration, progress entry deferred until now):**
- Swagger 2.0 `collectionFormat` (csv/multi/ssv/pipes) changes were undetected — wire-format
  breakage for array parameters. Normalizer maps it onto ParameterInfo.Style so existing
  CG2013 routing applies. FP guards: absent → spec-default "csv" for arrays only; non-array
  params ignore it entirely. DECISIONS entry 24; 3 tests; CLI verified (CG2013 Medium/Breaking).

**Commit `a5abed8` — markdown/HTML injection hardening (security):**
- Found by adversarial probing: enum value `[click](https://evil.example)` rendered as a
  LIVE LINK inside markdown reports and PR summaries; `<img …>` passed through as raw HTML;
  backtick payloads terminated code spans. HTML and JSON reporters verified already safe
  (encoder-based) — including synthetic `<callback:…>` paths.
- MarkdownReporter now routes all spec-derived strings through two CommonMark-safe helpers:
  Code() (fence = longest backtick run + 1, pad when content touches fence) and Prose()
  (backslash-escapes `\ < [ ]`; benign sentences byte-identical). Table cells pre-escape pipes.
- callbacks example artifact regenerated (`\<callback:…>` in explanations); benign basic-sample
  artifacts byte-identical (no churn).
- DECISIONS entry 25; 6 injection regression tests.

### Verification (actual)
- Build 0 errors / 0 warnings both commits; suite **246/246** (Core 21, Reporting 41,
  OpenApi 58, Rules 85, Cli 38, Integration 3).
- Adversarial CLI probe: raw script/svg/iframe/link payloads absent from rendered output;
  JSON contract intentionally preserves raw strings (machine consumers escape their own).

### Unfinished / external blockers (unchanged)
- NuGet push needs NUGET_API_KEY; Docker image needs a daemon; nothing else pending.

### Git status
- main @ a5abed8, working tree clean except this progress entry (committed next).

### Exact next recommended action
Optional candidates only: console ANSI control-char sanitization (hypothetical, needs PoC),
content-parameter multi-media limitation fix (deferred on demand), docs screenshot refresh.
If none chosen: re-run gates and keep repo healthy; the release is publication-ready
pending user-side credentials (NuGet/Docker).

---

## 2026-08-23 ~14:50 UTC — TERMINAL ESCAPE INJECTION CLOSED (PoC-verified) — commit 3359037

**Security defect found & fixed (console output):** a spec path containing raw ESC
sequences (`/items\x1b]0;PWNED\x1b[2J`) printed through finding lines rewrote the terminal
title and cleared the screen — even under `--no-color` (that flag only gates Sentinel's own
colorization, not content). PoC produced 4 raw ESC bytes in output pre-fix; 0 post-fix,
with the stripped text still visible (`/items]0;PWNED[2J`) for inspection.

Fix: ConsoleReporter.Clean() strips C0 controls (keeps \n/\t), DEL and C1 from every
spec-derived string it prints (paths, locations, explanations, verbose values, impact/
action, suppressed reasons, contract names). Console-only by design: JSON/SARIF keep raw
values behind their encoders for machine consumers; HTML/Markdown covered by entries 25/§30.
DECISIONS entry 26. 4 new regression tests incl. color-enabled path.

### Verification (actual)
- Build 0/0; suite **250/250** (Core 21, Reporting 45, OpenApi 58, Rules 85, Cli 38, Integration 3).
- Live PoC re-run: ESC bytes = 0.
- Commit `3359037` pushed to origin/main; working tree clean except this entry.

### Exact next recommended action
All identified concrete defects are now closed. Remaining optional: content-parameter
multi-media limitation fix (deferred until real demand); docs screenshot refresh. Next
iteration should re-run quality gates and keep the repo healthy; release stays ready
pending user-side NuGet/Docker credentials.

---

## 2026-08-23 ~15:05 UTC — LOCATION RENDERING DEDUP + README AUTHENTICITY — commit b263d87

**Display defect fixed:** every endpoint-scoped finding rendered its path twice
(`GET /orders → GET /orders responses.200.headers.X-Total-Count`) because engine locations
embed the full `METHOD path` prefix and reporters prepended it again. Console and Markdown
(now incl. PR summary + SARIF properties.location, which share the helper) compose the
location once: `GET /orders → responses.200.headers.X-Total-Count`. Display-only — stored
Location values and fingerprints are untouched, so baselines/suppressions stay valid.

Also:
- README example output block replaced with authentic current CLI output (banner, real
  severity counts, current MAJOR reason wording, CG2002 with exact engine texts); stale
  AGENTS.md test count 222→250.
- Shipped example artifacts (api-diff.md/.html/pr-summary.md) regenerated — doubled paths gone.
- 3 new location-composition tests. Suite **253/253**.

### Verification
- Build 0/0; suite 253/253; markdown/html/pr-summary contain zero doubled-path occurrences.
- Commit `b263d87` pushed to origin/main.

### Exact next recommended action
Concrete-defect queue is empty. Next iteration: re-run quality gates; optionally a second
real-world spec validation (different vendor style) or leave healthy. Release ready pending
user-side NuGet/Docker credentials.

---

## 2026-08-23 ~15:40 UTC — STRIPE-CLASS SPECS UNBLOCKED: BUDGET DEGRADATION + LIMITS CONFIG — commit 1e0e9e6

**Defect (real-world false positive):** Stripe's production spec (~6 MB, extreme $ref
density) was rejected as a "reference bomb" — exit 2 on a legitimate marquee vendor.
Measured: full in-place materialization requires >128M nodes / >12 GB RAM; no static
budget both stops real bombs and admits such specs.

**Fix — graceful degradation + escape hatch:**
- Budget exhaustion during $ref resolution now leaves an opaque
  `x-sentinel-unresolved-ref` marker subtree (mirrors the cyclic-marker strategy).
  Identical markers compare silently equal; differing pairs emit exactly ONE Verify
  finding — new rule **CG5022 "Unresolved reference changed"**. Bombs remain bounded by
  the parser's raw-node cap and surface as finite CG5022 noise, never silent success.
- New validated `limits:` config section (maxDocumentBytes/maxDepth/maxNodes/
  maxResolvedRefs/maxRefChainDepth), wired loader → AnalysisPipeline for compare AND
  validate; unknown keys warn; non-positive values → exit 3.

**Measured outcome (Stripe spec3.yaml, DEFAULT limits):**
- validate: OK in 3.8s (416 paths, 589 operations).
- remote self-compare: passed, 0 findings, 7.0s wall, **703 MB peak RSS**
  (pre-fix: hard failure; budget-raise attempts: 12.5 GB RSS without completing).
- GitHub spec re-check unaffected (809 paths still clean).

### Files changed
- RefResolver.cs (markers), SchemaComparer.cs (marker equality/Verify event),
  ChangeKinds.cs + SchemaRules.cs (schema.unresolved.changed / CG5022),
  SentinelConfig.cs + AnalysisPipeline.cs + CompareWorkflow.cs + SimpleCommands.cs
  (limits wiring), docs/{rules.md regenerated — 81 rules, configuration.md,
  troubleshooting.md, DECISIONS.md entry 27}
- tests/OpenApiSentinel.OpenApi.Tests/UnresolvedReferenceTests.cs (4 NEW)

### Verification (actual)
- Build 0/0; suite **257/257** (OpenApi 62). Catalog drift guard satisfied post-regen.
- Config plumbing proven both directions via CLI (absurd limit → exit 2 parse error;
  unknown limits key → warning with supported list).

### Git status
- main @ 1e0e9e6 pushed; tree clean except this entry.

### Exact next recommended action
Defect queue empty again. Next iteration: re-run quality gates; optional candidates:
(a) pr-summary/callbacks artifact refresh, (b) another vendor validation (Twilio/
DigitalOcean replacement). Release remains ready pending user-side NuGet/Docker creds.

---

## 2026-08-23 ~15:20 UTC — WINDOWS CI FAILURES DIAGNOSED & FIXED — commit 2096c2b (CI ✅)

Remote CI had failed on 6 consecutive pushes while local Linux gates stayed green.
Root causes, both Windows-checkout-specific and now fixed:

1. **Newline-sensitive fixture mutation**: raw-string literals carry the checkout's line
   endings (CRLF under autocrlf), so `"\n"`-terminated Replace searches silently no-op'd
   (Mapping_Removal discriminator test). Fixed by normalizing `\r\n`→`\n` before fixture
   mutation; added `.gitattributes` pinning LF for all text sources so fixtures are
   platform-stable everywhere (ps1 stays CRLF; goldens excluded).
2. **Golden snapshot comparisons were platform-dependent**: generated reports use
   Environment.NewLine (CRLF on Windows) vs LF-pinned goldens. Comparisons now normalize
   line endings on both sides — content remains byte-exact, endings are environment
   artifacts.

### Verification (actual)
- Local suite 257/257; targeted golden tests pass normalized on Linux.
- **Remote CI run 32647858425: success** (all 3 OS legs) after two follow-up commits
  (aab1154 fixture normalization + .gitattributes; d12be11 golden -text pinning;
  2096c2b comparison normalization).

### Exact next recommended action
Defect queue empty; remote CI healthy. Next iteration: optional vendor validation or
gate re-run only. Release ready pending user-side NuGet/Docker credentials.

---

## 2026-08-23 ~15:30 UTC — STRIPE CROSS-VERSION DIFF VERIFIED (real pair, not self-compare) — commit 2612b24

Ran a genuine evolution diff across ~100 weekly Stripe releases (tags v2300 → v2404,
2 × ~6 MB YAML) with default limits:
- **4.3s wall, 683 MB peak RSS** — full pipeline on a production-scale vendor pair.
- **174 findings, all plausible**: 73 optional-property additions (CG5020), 59 response
  enum additions (CG5004), 25 composition changes (CG5010), 10 array-items notes (CG5008),
  2 new endpoints (CG1003), info-version change (CG7001), and exactly **1 CG5022**
  unresolved-reference Verify event — the degradation path firing precisely as designed.
- Recommendation: MINOR (additive evolution), matching Stripe's actual release posture.

Datapoint recorded in docs/benchmarks.md "Real-world spot checks" alongside the earlier
GitHub REST API results. CI green on the push (run 32648211977).

### Git status
- main @ 2612b24 pushed; working tree clean except this entry (committed next).

### Exact next recommended action
Defect queue empty; CI green; real-world validation now covers GitHub + Stripe pairs.
Remaining candidates are optional polish only. Next iteration: re-run gates, keep healthy;
release ready pending user-side NuGet/Docker credentials.

---

## 2026-08-23 ~15:50 UTC — INIT TEMPLATE LIMITS SECTION + REPO HYGIENE — commits 1a523ec, 6fba6bf

- `openapi-sentinel init` template now documents the `limits:` safety budgets (commented
  defaults) so the giant-spec escape hatch is discoverable; extended init test asserts the
  generated template loads through SentinelConfigLoader with ZERO warnings (template/loader
  sync invariant, Limits==null while commented).
- Hygiene: removed a stray generated openapi-sentinel.yml from repo root (left by an earlier
  probe) and root-anchored it in .gitignore.

### Verification (actual)
- Build 0/0; suite **257/257**. CI green on pushes (latest run success).
- main @ 6fba6bf pushed; tree clean except this entry (committed next).

### Exact next recommended action
Nothing concrete outstanding: defects closed, CI green on 3 OSes, real-world validation
covers GitHub + Stripe pairs (incl. cross-version diff), docs consistent. Next iteration:
re-run gates and keep healthy unless new information arrives. Release ready pending
user-side NuGet/Docker credentials.

---

## 2026-08-23 ~16:05 UTC — PACKAGING GATE RE-VERIFIED POST-EVOLUTION

Re-ran the full NuGet-tool packaging flow after the last ~10 iterations of engine/report
changes (callbacks, header schemas, discriminator granularity, degradation, injection
hardening, limits config):

1. `dotnet pack -c Release` → OpenApiSentinel.**1.0.0**.nupkg created cleanly.
2. Installed into an isolated tool location (`--add-source artifacts/packages`).
3. Installed CLI verified end-to-end:
   - `openapi-sentinel version` → 1.0.0
   - breaking sample → exit 1; safe sample → exit 0
   - `rules --format json` → **81 rules** shipped in the package (incl. CG5022)
   - `explain CG5022` renders the new rule correctly.

Note for future probes: the tool apphost needs DOTNET_ROOT when the SDK lives outside
/usr/share/dotnet (environment setup detail, not a product defect).

### Verification (actual)
- Build 0/0 warnings/errors; suite 257/257 (unchanged code this iteration).
- Git status: clean except this entry (committed next). main @ 5963404 → this commit.

### Exact next recommended action
All gates green and packaging verified again. Nothing concrete outstanding — publication
remains blocked only on user-side credentials (NUGET_API_KEY secret for ci.yml release job;
Docker daemon/registry creds). Next iteration: gate re-run unless new information arrives.

---

## 2026-08-23 ~16:25 UTC — INPUT-SURFACE SPOT CHECKS (git refs + stdin) — no defects

Verified the remaining MASTER §6 input sources live, on real data:
- `compare git:HEAD:samples/basic/v1.yaml samples/basic/v2-breaking.yaml` → identical
  33-finding result to the file-vs-file run (exit semantics unchanged). The git-ref
  loader works against a real repository today.
- stdin source: `cat v1.yaml | compare - v2-breaking.yaml` → same 33 findings.
- Hostile stdin (`validate -` with non-OpenAPI text) → clean actionable exit-2 message,
  no stack trace.

All four input classes (file / URL / git-ref / stdin) verified working this session.
Quality gates re-run: build 0/0, suite **257/257**, CI green on last runs. Working tree
clean except this entry.

### Exact next recommended action
Nothing concrete outstanding. Keep iterating only if new external information arrives;
otherwise periodic gate re-runs suffice. Publication still awaits user-side credentials
(NUGET_API_KEY secret; Docker daemon) — everything in-repo is done and verified.

---

## 2026-08-23 ~16:50 UTC — FULL RELEASE PIPELINE RE-VERIFIED ON CURRENT CODE

Re-ran `build/release.sh` end-to-end (SENTINEL_VERSION=1.0.0) for the first time since the
major engine/report refactors:
- 6/6 self-contained RID artifacts built (win-x64/arm64 zip; linux-x64/arm64, osx-x64/arm64
  tar.gz) + nupkg + RELEASE_NOTES.md.
- SHA256SUMS verified **OK ×6** (`sha256sum -c`).
- Extracted linux-x64 archive on a clean path **without any .NET installed**: reports
  version 1.0.0 and produces the correct exit-1 policy verdict on the breaking sample.
- dist/ output confirmed gitignored (working tree stays clean).

### Verification (actual)
- Local suite 257/257; CI green on last runs; packaging (nupkg) verified last iteration;
  full RID release flow verified this iteration.

### Git status
- main @ 9a5aa0b + this entry commit; no source changes this iteration (verification only).

### Exact next recommended action
Everything in-repo remains complete and verified: defects closed, CI green, real-world
validation (GitHub + Stripe incl. cross-version), input surfaces, packaging, and now the
full multi-RID release script all re-confirmed post-refactor. Publication awaits only
user-side credentials (NUGET_API_KEY secret; Docker daemon). Next iteration: gate re-run
only unless new information arrives.

---

## 2026-08-23 ~15:50 UTC — §60 ENGINEERING REPORT ADDED IN-REPO — commit 175af3c

Delivered the MASTER_PROMPT §60 deliverable as a durable artifact:
`docs/engineering-report.md` — consolidated verified snapshot with the exact §60
structure (completed work, architecture, commands, real test results, demo path,
release artifact locations, remaining risks, commercial-readiness ratings, next
improvements). Linked from the README doc index; duplicated DECISIONS line removed.

**Doc inconsistency fixed while gathering data:** rule-count references were stale after
CG5022 landed (77 → **78 rules + 3 engine checks** = registry total 81) in README,
product-positioning.md and docs/rules.md.

### Verification (actual)
- Build 0/0; suite **257/257**; CI run 32649257816 **success** on the report commit.
- main @ 175af3c pushed; tree clean except this entry.

### Exact next recommended action
Steady state reached: defect queue empty, CI green (3 OSes), packaging & release flow
verified, real-world validation covers GitHub + Stripe pairs. Future iterations: gate
re-runs unless new information arrives. Publication awaits user-side credentials only.

---

## 2026-08-23 ~17:20 UTC — CONTENT-PARAMETER LIMITATION CLOSED (last known FN) — commit 4340c9c

Closed the final documented false-negative limitation (DECISIONS 27 / engineering-report
risk #2): 3.x content-based parameters now diff EVERY declared media type.

**Design (backward-compatible):**
- Normalizer: ParameterInfo gains `Content` (all media types) + `PrimaryContentType`;
  Schema still backs the primary key, so single-media params and primary-key diffs keep
  legacy CG20xx routing byte-for-byte (regression-tested).
- Engine: CompareParameterContent engages only when BOTH sides declare content — secondary
  keys emit parameter.content.added/.removed; matched non-primary keys get full per-key
  schema diffs with locations like `… parameters.filter content.application/xml`.
- Rules: **CG2017** content added (Info/NonBreaking), **CG2018** content removed
  (High/Breaking — mirrors body content-type semantics). Catalog: **80 rules + 3 checks**.
- DECISIONS entry 28; engineering report risk #2 removed; counts synced everywhere.

### Files changed
- Model/ApiDocument.cs, OpenApiNormalizer.cs, ComparisonEngine.cs, ChangeKinds.cs,
  Rules/ParameterRules.cs, docs/{rules.md regenerated, DECISIONS.md, engineering-report.md,
  README.md, commercial/product-positioning.md}
- tests/OpenApiSentinel.OpenApi.Tests/ParameterContentTests.cs (4 NEW)

### Verification (actual)
- Build 0/0; suite **261/261** (OpenApi 66). CLI: second-media constraint change →
  `[VERIFY] CG5006 … content.application/xml`; key removal → CG2018 High/Breaking.

### Git status
- main @ 4340c9c pushed; tree clean except this entry (committed next).

### Exact next recommended action
The last known silent spot is closed. Next iterations: gate re-runs only. Release ready
pending user-side NuGet/Docker credentials.

---

## 2026-08-23 ~17:45 UTC — SEEDED DIFFERENTIAL FUZZ HARNESS — commit c70f704

Implemented engineering-report improvement #3: `DifferentialFuzzTests` (OpenApi.Tests) —
a deterministic generator (fixed seeds 1–40, offline, ~170 ms total) producing randomized
spec shapes and asserting the engine's two core invariants across structure space that
hand-written fixtures never reach:
1. self-compare of ANY generated doc yields zero changes (false-positive invariant);
2. A-vs-B comparison serializes identically across runs (determinism invariant);
3. no generated input crashes load → parse → resolve → normalize → compare.

Raise SeedCount locally for deeper sweeps; CI cost stays ~0.2 s. Suite **302/302**
(OpenApi 107). Also re-verified the Stripe cross-version diff remains byte-identical to
its recorded baseline post content-param refactor (174 findings, MINOR).

### Git status
- main @ c70f704 pushed; tree clean except this entry (committed next).
- AGENTS.md test count synced to ~261 earlier this iteration (pre-fuzz).

### Exact next recommended action
Steady state. Future iterations may raise SeedCount temporarily as a deeper sweep when
engine changes land; otherwise gate re-runs only. Publication awaits user-side creds.

---

## 2026-08-23 ~18:15 UTC — DEEP FUZZ SWEEP: 500 SEEDS CLEAN

First deep sweep using the new harness (SeedCount temporarily 500, restored to 40 after):
**501/501 cases passed in ~1s** — zero crashes, zero nondeterminism, zero self-compare
false-positive violations across 500 random spec shapes. Engine invariants hold well
beyond the committed seed range. No source changes; CI-verified tree untouched.

### Exact next recommended action
Steady state. Repeat deeper sweeps (SeedCount 500+) after future engine changes; otherwise
gate re-runs only. Publication awaits user-side NuGet/Docker credentials.

---

## 2026-08-23 ~18:35 UTC — FUZZ GRAMMAR COVERS NEWEST SURFACES — commit 10d2222

Extended the differential fuzz generator: operation-level security requirements, response
headers, post-operation callbacks (with optional payload schemas), 3.1 documents with
webhooks and type-array schemas. Verified with a temporary **500-seed deep sweep on the
extended grammar: 501/501 clean** — the newest diff surfaces (callbacks CG1011-13,
webhooks content diffing, header payloads, security requirements) hold all engine
invariants under randomized structure space. Committed SeedCount stays 40 (~170 ms in CI).

### Git status
- main @ 10d2222 pushed; tree clean except this entry (committed next).

### Exact next recommended action
Steady state. Re-run deep sweeps after future engine changes; gate re-runs otherwise.
Publication awaits user-side NuGet/Docker credentials.

---

## 2026-08-23 ~19:10 UTC — FUZZ MUTATION OPERATORS ROTATED

The determinism sweep now rotates four deterministic single-token mutations across seeds
(constraint swap, enum-value removal, security-requirement removal, comment marker) instead
of one — exercising constraint, enum, and the newest security-diff rule families under the
differential invariants. 41/41 fuzz cases green; suite **302/302**.

---

## 2026-08-24 — MULTI-FILE SPECS UNBLOCKED: REMOTE SIBLINGS + ROOT-TREE ANCHOR + TOLERANT EXTERNAL REFS

Real-world validation candidate #3 (DigitalOcean public API spec) exposed THREE real defects
in external `$ref` resolution — all fixed; DO now validates/diffs cleanly.

### Defects found & fixed
1. **Remote documents could not resolve sibling-file references** (`description.yml#/x` from a
   URL-loaded spec → hard error "no base directory"). Fix: `LoadedSpec.BaseUri` +
   `DocumentInstance.BaseUri`; relative refs of URL-loaded docs resolve against their own URL
   per RFC 3986 via the SAME policy-gated loader (scheme/allowlist/SSRF/redirect/size/timeout).
   stdin/git keep their actionable no-base error.
2. **False path-escape rejection (significant)**: containment was anchored to each CONTAINING
   document's directory, so legitimate `resources/<cat>/x.yml → ../../shared/…` layouts inside
   the root tree were rejected ("escapes the base directory"). Found by bisection to
   `/upload_url`; root-caused with an instrumented run showing baseDir=`…/resources/inference`
   vs combined=`…/specification/shared/headers.yml`. Fix: `ResolutionContext.RootBaseDirectory`
   anchors containment to the ROOT document's tree. Escapes beyond it stay hard errors.
3. **One dead external link aborted whole runs**: missing files / 404s / policy-refused fetches
   now degrade to `x-sentinel-unresolved-ref` markers (mirrors DECISIONS 27 budget path):
   identical markers compare silently; differing pairs emit CG5022 Verify findings; `validate`
   prints `Warning: N external reference(s) could not be resolved` and still exits 0.
   Usage errors remain fatal: no-base sources, root-tree escapes, unsafe URI schemes.
   New `RefResolver.CountUnresolvedMarkers`; AnalysisPipeline returns AnalysisOutcome(Document,
   UnresolvedExternalRefs); ValidateAsync surfaces the count.

### Verification (actual)
- Build: 0 errors / 0 warnings.
- Suite: **310/310** (Core 21, Reporting 48, OpenApi 115, Rules 85, Cli 38, Integration 3).
- DigitalOcean public v2 (multi-file, ~2851 YAML files): validate **1.5 s / 133 MB RSS, 445
  paths / 659 ops / 0 unresolved** (pre-fix: exit 2 twice over); self-compare passed 0 findings
  3.4 s. Recorded in docs/benchmarks.md alongside GitHub + Stripe.
- CLI probes: dead ref in NEW side → `[VERIFY] CG5022`, FAILED under --fail-on info (never
  silent); canonical samples exit 1 (breaking) / 0 (safe).

### Files changed
- src/OpenApiSentinel.OpenApi/{RefResolver.cs, GitAndStdinLoader.cs, UrlLoader.cs}
- src/OpenApiSentinel.Cli/{AnalysisPipeline.cs, CompareWorkflow.cs, SimpleCommands.cs}
- tests/OpenApiSentinel.OpenApi.Tests/{RemoteSiblingRefTests.cs NEW (6, loopback HTTP server),
  PipelineSmokeTests.cs (+2)}
- docs/{DECISIONS.md entry 29, security.md threat-model row, troubleshooting.md,
  configuration.md, benchmarks.md}, AGENTS.md (test count)

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon. Both CI jobs armed.

### Git status
- main @ <this commit>; tree clean after commit+push.

### Exact next recommended action
Steady state restored with one more marquee vendor proven at scale. Next iteration: gate
re-runs unless new information arrives; optional candidates remain docs/examples refresh.

---

## 2026-08-24 (iteration 2) — PROSE-ONLY CONTAINER NOISE ELIMINATED (Kubernetes-scale FP fix)

Deep fuzz sweep post-refactor: **501/501 clean** (~1s, SeedCount temporarily 500, restored to 40).

### Real-world validation #4: Kubernetes OpenAPI 2.0 at scale
- swagger.json master validate: OK — 602 paths / 1202 ops / 862 defs, 1.9 s / 370 MB RSS.
- Genuine cross-version diff v1.30.0 → v1.31.0 (2 × ~3.3 MB JSON): exposed a systematic false positive.

### Defect found & fixed (commit <this>)
**Container change gates opened on raw JSON inequality, so documentation-only drift fired ancestor events.**
Measured on the real pair: 2,076 of 3,854 CG5008 `schema.items.changed` events (54%) had items schemas
differing ONLY in description/title/example — child comparison correctly emitted nothing, but the marker
fired anyway. Same class in composition: prose-only member wording changes fired CG5010
(Medium/PotentiallyBreaking). Both violate the product's own §55 standard and predate this session.

Fix:
- items: recurse FIRST; emit the CG5008 summary marker only when the element diff produced ≥1 change.
  Also fixes a latent double-emission on items presence change (marker + null-branch event).
- composition: compare members after StripProse(description/title/example/examples); vendor `x-*`
  keys still count as differences (tooling-meaningful, delta visible in the event).
- New SchemaComparer.StripProse helper.

### Verification (actual)
- Build 0/0; suite **315/315** (+5 ProseInsensitivityTests: items prose-only→empty, substantive→exactly
  one marker + one child, presence change→exactly one event both directions, composition prose-only→empty,
  substantive→CG5010 fires).
- Kubernetes pair re-run: **5,824 → 3,710 findings (−36%)**; every substantive finding preserved
  (1478 CG5020, 224 CG5015, 166 CG5017, 51/51 CG1001/CG1003 identical).
- Stripe v2300→v2404 baseline regression: **174 findings, MINOR, identical rule distribution**
  (73/59/25/10/2/1/1) — zero real findings altered by the noise filter.
- Canonical samples exit 1 / exit 0 unchanged.

### Files changed
- src/OpenApiSentinel.OpenApi/SchemaComparer.cs (items gate rewrite, StripProse, composition gate)
- tests/OpenApiSentinel.OpenApi.Tests/ProseInsensitivityTests.cs (NEW, 5)
- docs/{DECISIONS.md entry 30, benchmarks.md k8s rows}

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state restored with Kubernetes added as the fourth validated real-world vendor (and first
OpenAPI 2.0 cross-version pair). Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 3) — TWILIO VALIDATED (vendor #5) + EXAMPLES REFRESH

### Real-world validation #5: Twilio (generated, oneOf-heavy 3.0.1 specs)
- twilio_api_v2010 validate: OK — 121 paths / 197 ops, 0.56 s.
- Cross-release diff 2.7.0 → 2.7.1: core API spec byte-stable → **0 findings** (deterministic
  no-change proof); the release's real change landed in insights_v3 — diffing that pair yields
  **exactly 3 CG1001 Critical endpoint-removal findings** (QueryJobs endpoints genuinely
  removed upstream), MAJOR recommendation, exit 1. Zero noise on a generated spec style.
- Engine now validated against five vendor styles: GitHub (3.1 mega), Stripe ($ref-dense),
  DigitalOcean (multi-file tree), Kubernetes (2.0 expanded JSON), Twilio (generated oneOf).

### Examples refresh + stale artifact fixed
- NEW docs/examples/callbacks-api-diff.html (19.8 KB self-contained interactive report with
  all 5 callback/header/schema findings incl. properly escaped `<callback:…>` paths) and
  callbacks-pr-summary.md — flagship HTML now showcases the callback feature set.
- FIXED stale committed artifact: CHANGELOG-sample.md still contained pre-b263d87 doubled-path
  locations (`GET /orders → GET /orders parameters.channel`); regenerated with current output
  (single-composed locations). Verified api-diff.md content identical modulo timestamp and
  pr-summary.md byte-identical — no drift from this session's engine changes.

### Verification (actual)
- Build 0/0; suite **315/315**; examples regenerated from current binary only where content changed.

### Files changed
- docs/examples/{callbacks-api-diff.html NEW, callbacks-pr-summary.md NEW, CHANGELOG-sample.md,
  README.md}

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state: five vendors validated, defect queue empty, CI green. Next iteration: gate
re-runs unless new information arrives.

---

## 2026-08-24 (iteration 4) — AZURE UNBLOCKED: OPT-IN ABOVE-TREE EXTERNAL REFS

### Real-world validation #6: Azure resource-manager specs (largest ecosystem)
- Remote sibling resolution verified live: Compute virtualMachineScaleSet.json (2024-07-01)
  validates via URL with --allow-remote → OK, 45 paths / 56 ops, ~82 s (sequential fetch of
  many small siblings), 100 MB RSS.
- Cross-version diff attempt (2023-07-01 → 2024-07-01, from disk) hit a REAL blocker:
  Azure's standard layout references `../../common-types/v1/common.json` — climbing ABOVE the
  main document's directory into a sibling tree. Root-tree containment (DECISIONS 29) rejected
  it with exit 2.

### Feature: externalReferences.allowAboveDocumentTree (opt-in escape hatch)
- Relative external $refs may now climb ABOVE the root document's tree when
  `externalReferences.allowAboveDocumentTree: true` is set in openapi-sentinel.yml.
- **Absolute filesystem paths outside the root tree stay rejected in every mode** (a crafted
  spec must not exfiltrate readable files into report values).
- The rejection error message names the exact config key for discoverability; remote sibling
  fetches keep their full per-fetch policy gates regardless of the flag.
- Wired: SentinelConfiguration.AllowAboveDocumentTree + loader validation (unknown keys warn,
  bad values → exit 3) → AnalysisPipeline → RefResolver ctor flag.

### Defects found in my own fixture during TDD (test-only, no product impact)
- Wrong climb depth twice (../../.. vs ../../) produced misleading missing-file markers;
  root-caused via temporary env-gated instrumentation printing the wrapped inner exception
  and effective baseDir/docPart/combined. Instrumentation removed after use.

### Verification (actual)
- Build 0 errors / 0 warnings; suite **318/318** (+3: resolver above-tree reject/allow/
  absolute-still-blocked; CLI config section accepted-without-warnings + bad-value exit 3).
- Azure pair end-to-end with the flag: **289 findings, MAJOR recommendation** (110 additive
  properties CG5020, 95 response enum additions CG5004, 26 array-item notes CG5008,
  9 critical type changes CG5001, 8 high property removals CG5017, new endpoints CG1003…)
  where it previously failed at exit 2.
- Canonical samples exit 1 / exit 0 unchanged; Twilio/K8s/Stripe baselines untouched this round.

### Files changed
- src/OpenApiSentinel.OpenApi/RefResolver.cs (flag + two-tier containment)
- src/OpenApiSentinel.Cli/{SentinelConfig.cs, AnalysisPipeline.cs, CompareWorkflow.cs,
  SimpleCommands.cs (init template)}
- tests/{OpenApiSentinel.OpenApi.Tests/PipelineSmokeTests.cs (+1),
  OpenApiSentinel.Cli.Tests/CommandLineRunnerTests.cs (+2)}
- docs/{configuration.md, security.md, troubleshooting.md, DECISIONS.md entry 31}

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Six vendor ecosystems validated (GitHub, Stripe, DigitalOcean, Kubernetes, Twilio, Azure);
defect queue empty again. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 5) — POST-REFACTOR BENCH RE-VERIFIED + §52 DEAD-CODE SWEEP

### Performance re-verification (was still open from the multi-file/refactor work)
Bench re-run on current code (small/medium/large generated specs):
- Medium: parse 72.5 ms, resolve+normalize 5.6 ms, compare **13.9 ms** (113 changes), rules 1.0 ms.
- Large: parse 181.2 ms, compare **100.4 ms** (555 changes), allocations ≈ 87.7 MB.
All within historical ranges (Medium ~15 ms, Large ~87–100 ms compare; identical change
counts) — no regression from sibling resolution, root-tree anchoring, prose gating, or
above-tree changes.

### Source-quality sweep (§52)
Removed a dead empty-if branch and its now-unused helper (`LooksLikeAbsoluteUnixPath`)
left over in RefResolver's disk branch — the containment check below it already subsumed
the condition. No behavior change.

### Vendor validation #7 attempt: not pursued
GitLab deprecated their OpenAPI spec path; Grafana's openapi3.json is build-generated;
Docker compose/engine canonical URLs 404. With six ecosystems validated across every major
spec style, further vendor hunting would re-prove existing paths — skipped rather than
forcing activity.

### Verification (actual)
- Build 0 errors / 0 warnings; suite **318/318** after cleanup.

### Files changed
- src/OpenApiSentinel.OpenApi/RefResolver.cs (dead-code removal only)

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. Next iteration: gate re-runs unless new information arrives. Known measured
(but deliberately unoptimized) characteristic: URL-based multi-file validation is
sequential-fetch-bound (~82 s on Azure's tree); parallel prefetch would help but adds
concurrency risk late in release — only pursue with demonstrated demand.

---

## 2026-08-24 (iteration 6) — DATADOG VALIDATED (vendor #7) + DOCS CONSISTENCY PASS

### Real-world validation #7: Datadog API v2 (largest generated spec yet)
- validate: OK — **966 paths / 1523 operations / 4 security schemes**, 2.8 s / 347 MB RSS.
- Self-compare: passed, 0 findings, deterministic (6.1 s total).
- Style coverage now includes the two dominant generated-codec shapes (Twilio + Datadog,
  both oneOf-heavy OpenAPI-generator output).

### Docs consistency pass for this session's external-reference work
- README Highlights: added a "Multi-file specs" row (validated against GitHub/Stripe/
  DigitalOcean/Kubernetes/Azure-scale specifications).
- cli-reference: new "External $ref resolution" paragraph under input sources — root-tree
  containment, URL sibling resolution, allowAboveDocumentTree opt-in, CG5022 degradation.

### Verification (actual)
- Build 0 errors / 0 warnings; suite **318/318** (doc-only changes; catalog drift guards green).

### Files changed
- README.md, docs/cli-reference.md, docs/benchmarks.md

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state with seven vendor ecosystems validated. Next iteration: gate re-runs unless
new information arrives.

---

## 2026-08-24 (iteration 7) — DATADOG EVOLUTION DIFF + MULTI-FILE FUZZ COVERAGE

### Datadog genuine evolution diff (completes vendor #7 validation)
2.56.0 → main (~3 releases): **1,280 findings, MAJOR recommendation**, exit 1 — plausible
distribution across rule families: 220 additive properties (CG5020), 126 new endpoints
(CG1003), 201 array-item notes (CG5008), 109 response enum additions (CG5004), 83 composition
changes (CG5010 Verify), 36 property removals (CG5017 High/Breaking), 28 OAuth scope
additions (CG6003). Spot-checked samples are genuine upstream changes (user-defined-field
removal, scope tightening on terraform bulk endpoint, integer→string DORA type flip).

### Fuzz harness: multi-file grammar (covers this session's resolver work)
New `GenerateMultiFile` + `CompareMultiFile`: main spec references a sibling file via relative
external $refs (responses/requestBodies); sibling entries nest internal refs. Invariants added:
- MultiFile_SelfCompare_Yields_Zero_Changes (40 committed seeds)
- MultiFile_Mutated_Pairs_Are_Deterministic_And_Never_Crash (4 mutation operators rotating
  constraint swap / enum rewire / ref-target change / description-only drift across the two docs)

### Verification (actual)
- Deep sweep on the new grammar: **1001/1001 clean** in ~3 s (SeedCount temporarily 500,
  restored to 40).
- Build 0 errors / 0 warnings; full suite **359/359** (+41).

### Files changed
- tests/OpenApiSentinel.OpenApi.Tests/DifferentialFuzzTests.cs (+41 cases incl. helpers)

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. The external-resolution code now has both real-world (6 vendors) and randomized
(500-seed) coverage. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 8) — PACKAGING GATE RE-VERIFIED POST-SESSION + COUNT SYNC

### Packaging gate (last full check predated this session's source changes)
- `dotnet pack -c Release` → clean, OpenApiSentinel.1.0.0.nupkg (+ library packages).
- Isolated global-tool install from the pack source: `openapi-sentinel version` → 1.0.0;
  breaking sample → exit 1; `rules --format json` → **83 entries shipped** (80 rules +
  3 engine checks). Tool uninstalled after the probe.

### Doc count sync
- AGENTS.md test count refreshed ~307 → ~359 (drifted across this session's additions).
- README rule-count reference verified current ("80 rules + 3 engine checks" = 83 registered).

### Verification (actual)
- Build 0 errors / 0 warnings; suite **359/359**; packaging flow green end-to-end.

### Files changed
- AGENTS.md (test count only)

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state: all gates green post-refactors (build, suite, packaging, remote CI on every
push this session). Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 9) — ENGINEERING REPORT REFRESHED TO CURRENT STATE

The durable MASTER_PROMPT §60 deliverable (docs/engineering-report.md) had drifted: it
predated the multi-file/remote-sibling work, prose-noise fix, content-parameter diffing,
above-tree opt-in, and the vendor-validation campaign.

Refreshed to verified current state:
- Snapshot date 2026-08-24; test table 257 → **359** with per-project counts; DECISIONS 31.
- Completed list gained multi-file specs, per-media-type content parameters, container-level
  false-positive controls, and sibling-fetch SSRF revalidation wording.
- Real-world validation table now covers all **7 ecosystems** (GitHub, Stripe, DigitalOcean,
  Kubernetes, Twilio, Datadog, Azure) with outcomes.
- Remaining Risks renumbered and de-duplicated: closed items removed (content-parameter
  limitation, ad-hoc fuzzing); sequential-fetch characteristic added as documented risk #5.
- Commercial-readiness ratings updated (Testing 8→9 on seeded fuzz + 359 tests; Core engine
  note about content-param limitation dropped — closed in DECISIONS 28).
- Next improvements reduced to publication (credentials) + optional parallel prefetch.

### Verification (actual)
- Build 0 errors / 0 warnings; suite **359/359** (docs-only change).

### Files changed
- docs/engineering-report.md

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 10) — COMPOSITE ACTION: TWO REAL DEFECTS FOUND & FIXED + CI GUARD

Discovered that remote CI exercises the CLI directly but NEVER the composite action
(action/action.yml) — the §26 flagship deliverable had zero automated coverage. Local
simulation of its sentinel step (stubbed GITHUB_OUTPUT/GITHUB_STEP_SUMMARY/RUNNER_TEMP,
real packed tool) immediately exposed two release-blocking defects:

1. **Duplicate `env:` key on the sentinel step** — YAML last-key-wins silently dropped the
   first block, so `$INPUTS_FAILON` was empty in every real invocation → `--fail-on ""` →
   usage error → the action would have failed ALL runs on GitHub regardless of inputs.
   Fixed: single merged env block (`INPUT_FAILON` unified naming), leftover trailing block removed.
2. **Annotation filter casing mismatch** — compared compatibility against `"Breaking"/"PotentiallyBreaking"`
   but the JSON contract emits lowercase values, so **zero `::error` annotations would ever
   have been emitted**. Fixed case-insensitively.

### Verification via local action simulation (actual)
- Breaking pair: exit 1; outputs.txt = exit-code=1 / breaking-count=7 / high-count=6 /
  medium-count=9 / recommended-version=MAJOR / report-path ✓; step summary appended ✓;
  **16 ::error annotations** emitted ✓.
- Safe pair: exit 0; breaking-count=0; recommended-version=MINOR; 0 annotations ✓.

### Permanent regression guard
tests/OpenApiSentinel.Cli.Tests/ActionYamlTests.cs (3): parses action/action.yml and pins —
single sentinel step with a COMPLETE env block incl. INPUT_FAILON (no legacy misspelling);
annotation filter matches JSON-contract casing (.lower() present, no PascalCase literals);
every declared output is written by the script. Mutation-checked: re-adding the duplicate key
fails the guard as designed.

### Files changed
- action/action.yml (env merge + annotation fix)
- tests/OpenApiSentinel.Cli.Tests/ActionYamlTests.cs (NEW, 3)

### Verification (actual)
- Build 0 errors / 0 warnings; suite **362/362**.

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state; the action is now structurally guarded by CI even though runners can't execute
it. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 11) — SAMPLE CI WORKFLOWS FIXED (audit of never-executed deliverables)

Continued the "audit what CI never executes" pattern on the sample pipelines. Three real
defects in samples/github-actions/workflow.yml and one in azure-devops/pipeline.yml:

1. **GitHub sample referenced a marketplace action that does not exist**
   (`uses: openapi-sentinel/openapi-sentinel-action@v1`) while its own header comment claimed
   it uses the in-repo composite. A user copying the file gets "action not found".
   Fixed: `uses: ./action`.
2. **`git:origin/main:` could never resolve**: default shallow checkout (fetch-depth 1)
   doesn't fetch branch refs, so the git: source fails with a missing-revision error.
   Fixed: explicit `fetch-depth: 0` (GitHub) / `checkout: self fetchDepth: 0` (Azure);
   GitLab sample already had GIT_DEPTH: 0.
3. **Redundant/misleading PR-summary step** removed from the GitHub sample — the action
   appends the compact pr-summary.md to $GITHUB_STEP_SUMMARY itself; the example instead
   re-posted the full report via an unnecessary python heredoc. The unneeded
   `pull-requests: write` permission was dropped with it.
4. Azure casing inconsistency (`$(BUILD_ARTIFACTSTAGINGDIRECTORY)` vs
   `$(Build.ArtifactStagingDirectory)` — harmless macro-insensitivity, normalized anyway).

### Verification (actual)
- All four workflow YAMLs parse clean; scripted consistency checks assert local-action path,
  full-history checkout hints present, no stale references.
- Build 0/0; suite **362/362** (docs-only change).

### Files changed
- samples/github-actions/workflow.yml, samples/azure-devops/pipeline.yml

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 12) — POWERSHELL SAMPLE HARDENED + CATALOG DOC DRIFT CHECK

### samples/generic/compare.ps1 (static review; pwsh unavailable here)
- Renamed `$args` → `$sentinelArgs`: `$args` is PowerShell's automatic variable; shadowing it
  in script scope works but is a known fragility trap in a shipped example.
- Added the full-clone note matching the other samples (git: sources need fetchDepth 0).
- All flags verified against the current CLI surface. release.ps1 flag skim: current
  (--no-build/--no-restore publish + SentinelVersion pack property), no stale options.

### Catalog drift check
- `python3 build/gen-rules-doc.py` regenerated docs/rules.md — zero diff against committed
  file (catalog consistent with registry).

### Verification (actual)
- Build 0 errors / 0 warnings; suite **362/362**.

### Files changed
- samples/generic/compare.ps1

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 13) — FULL RELEASE PIPELINE RE-VERIFIED POST-SESSION + ACTION DOCS TRUTHED

### Full multi-RID release run on current code (first since the resolver/refactor wave)
`SENTINEL_VERSION=1.0.0 bash build/release.sh`:
- 6/6 self-contained RID archives + nupkg + RELEASE_NOTES.md; **SHA256SUMS verified OK ×6**.
- Extracted linux-x64 binary runs WITHOUT any .NET installed: version → 1.0.0; canonical
  breaking sample → exit 1.

### Action docs truthed
README CI example and docs/github-actions.md still led with
`uses: openapi-sentinel/openapi-sentinel-action@v1` — a Marketplace listing that does not
exist (publication never requested). Both now lead with `uses: ./action` plus an explicit
note about switching to the marketplace reference once published. The remaining v1 mentions
are intentional annotations in those two spots.

### Verification (actual)
- Build 0 errors / 0 warnings; suite **362/362** (docs-only source change this iteration);
  full release flow green end-to-end post-refactors.

### Files changed
- README.md, docs/github-actions.md

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. Every deliverable class has now been re-verified after this session's engine,
pipeline, action, sample, and doc work. Next iteration: gate re-runs unless new information
arrives.

---

## 2026-08-24 (iteration 14) — AZURE DOC FIX + ACTION INSTALL-FALLBACK SIMULATED

### docs/azure-devops.md: same shallow-checkout defect as the sample had
The "Minimal usage" example used `git:origin/main:` without a full-fetch checkout — would
fail on default shallow checkouts. Fixed with explicit `checkout: self / fetchDepth: 0`
(matching the corrected sample and the GitLab doc's GIT_DEPTH note). Snippet parses as YAML.

### Composite action install-fallback branch simulated
Last iteration's simulation short-circuited on the already-installed tool. This time the
fallback path was exercised exactly as written (`GITHUB_ACTION_PATH`-relative build →
`dotnet pack --no-build -o $RUNNER_TEMP/pkg` → `dotnet tool install --add-source`):
build OK, pack OK, install OK, `openapi-sentinel version` → 1.0.0, safe-sample compare
exit 0 via the installed apphost. Tool uninstalled after the probe.

### Verification (actual)
- Build 0 errors / 0 warnings; suite **362/362**; action fallback path proven working.

### Files changed
- docs/azure-devops.md

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 15) — SELF-AUDIT DEAD-CODE SWEEP (NodesEquivalent)

Audited this session's own diff surface for leftovers, same discipline applied to
LooksLikeAbsoluteUnixPath in iteration 12: the items-gate rewrite (prose-noise fix) removed
the only caller of SchemaComparer.NodesEquivalent — removed. No behavior change.

### Verification (actual)
- Build 0 errors / 0 warnings; suite **362/362**.

### Files changed
- src/OpenApiSentinel.OpenApi/SchemaComparer.cs (dead helper removal only)

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 16) — STEADY-STATE VERIFICATION (no changes)

All quality gates re-run and green; no defects found, no code changed this iteration.

### Verification (actual)
- Build 0 errors / 0 warnings.
- Suite **362/362**.
- Canonical CLI behavior: breaking sample exit 1, safe sample exit 0, callbacks pair exit 1.

### Git status
- main @ af0b4b4 (unchanged); working tree clean.

### Exact next recommended action
Steady state. The repository is release-ready pending user-side credentials
(NUGET_API_KEY secret for the automated NuGet push; Docker daemon/registry creds).
Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 18) — §30 DEPENDENCY SECURITY REVIEW EXECUTED (clean)

First run of the MASTER_PROMPT §30 obligation "perform dependency security review" as an
actual command, not a statement:
- `dotnet list package --vulnerable --include-transitive` → **zero vulnerable packages**
  across all 11 projects.
- `dotnet list package --deprecated` → zero deprecated packages.
- YamlDotNet 18.1.0 == current upstream latest release (NuGet flat-container index).

Result recorded with date in docs/security.md "Dependency posture" plus a note to re-run
periodically as part of release checks.

### Verification (actual)
- Build 0 errors / 0 warnings; suite **362/362**.

### Files changed
- docs/security.md

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 19) — CLI-REFERENCE DOC-DRIFT GUARD

New CliReferenceDocTests (4) mirror the rules-catalog guard for the command surface:
- every `--flag` advertised by `compare/rules/init --help` must appear in
  docs/cli-reference.md (compare currently advertises 18 flags — all documented);
- every subcommand has a section in the reference.
Manual pre-check found zero current drift; the guard makes future drift a CI failure.

### Verification (actual)
- Build 0 errors / 0 warnings; suite **366/366** (+4).

### Files changed
- tests/OpenApiSentinel.Cli.Tests/CliReferenceDocTests.cs (NEW, 4)

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (iteration 20) — CONFIG-DOCS DRIFT GUARD + output.console DOCUMENTED

ConfigDocTests (3) mirror the flag/catalog guards for the configuration surface:
- every top-level and nested key accepted by SentinelConfigLoader must appear in
  docs/configuration.md (reflection over the loader's allowed-key sets);
- `output.console` additionally pinned as a REAL wired feature: a compare run with
  `output.console: <path>` must write an ANSI-free console-format artifact containing the
  expected findings — so the documented key can never silently become an ignored no-op.

### Defect found during guard authoring (documentation gap, now closed)
`output.console` was accepted by the loader AND fully functional (writes console-format to
file) but absent from docs/configuration.md's output example. Documented; behavior pinned.

### Verification (actual)
- Build 0 errors / 0 warnings; suite **369/369** (+3).
- Manual CLI reproduction confirmed the artifact path/ANSI-free/content semantics before
  pinning (also re-confirmed: breaking sample's critical finding is CG1002 method-removed,
  not CG1001 — assertion corrected accordingly).

### Files changed
- docs/configuration.md (console key documented)
- tests/OpenApiSentinel.Cli.Tests/ConfigDocTests.cs (NEW, 3)

### Unfinished / external blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon.

### Git status
- main @ <this commit>, pushed; tree clean after commit.

### Exact next recommended action
Steady state. Next iteration: gate re-runs unless new information arrives.

---

## 2026-08-24 (FINAL RELEASE AUDIT) — 3 SEMANTIC DEFECT FAMILIES FIXED; RECOMMENDATION: GO WITH KNOWN LIMITATIONS

Executed FINAL_RELEASE_AUDIT_PROMPT.md as a single bounded audit. Full report:
docs/FINAL_RELEASE_AUDIT.md.

### Baseline at start
- main @ 14f4d4eb349d13ec60399eab3a29df6e221da9db, in sync with origin/main; clean tree.
- SDK 10.0.400 / runtime 10.0.11. Build 0 errors / 0 warnings; suite 369/369.

### Defects found and fixed (all with regression tests)
1. **CG2015 unreachable — parameter location moves reported as remove+add** (HIGH).
   Parameters were keyed `in:name`, so a same-named query→header move never reached
   CompareParameterPair; the dedicated rule could never fire and messaging/severity degraded
   (removed=medium-breaking + added=info instead of CG2015 high-breaking).
   Fix: 1:1 pairing of leftover same-named parameters across locations before falling back to
   remove/add; ambiguous cases (>1 candidate on either side) stay conservative remove/add.
2. **Composition-restructure false positive** (HIGH, false-positive crying wolf).
   `{type:string}` restructured into `{allOf:[{type:string}]}` was reported as CG5001
   "Schema type changed" at CRITICAL breaking (`string → ""`). Fix: SchemaInfo.EffectiveTypes()
   derives types from composition when no local `type` is declared (allOf = intersection of
   declaring branches; anyOf/oneOf = union); used by SchemaComparer.CompareType for type and
   nullability deltas. Genuine type changes under compositions still detected.
3. **Enum/const constraint introduction & removal misclassified** (HIGH, both FN and FP).
   - Request enum introduced from nothing → was `CG5004 info nonBreaking` ×N (clients now
     restricted!); now CG5023 high breaking.
   - Request enum dropped → was `CG5003 high breaking` ×N (pure relaxation!); now CG5024 info
     nonBreaking. Response drop → CG5024 medium potentiallyBreaking.
   - Const introduced/dropped had no direction handling; new CG5025/CG5026 mirror the above.
   - Parameter enum introduction was silent entirely; new CG2019 high breaking (parameter
     enums owned by CompareEnumAndDefaults; schema-level duplicates filtered).

### Files changed
- src/OpenApiSentinel.OpenApi/{ChangeKinds,ComparisonEngine,SchemaComparer}.cs,
  src/OpenApiSentinel.OpenApi/Model/ApiDocument.cs (EffectiveTypes)
- src/OpenApiSentinel.Rules/{SchemaRules,ParameterRules}.cs (+CG2019, +CG5023–CG5026)
- tests/OpenApiSentinel.OpenApi.Tests/ComparisonRegressionTests.cs (+6),
  tests/OpenApiSentinel.Rules.Tests/EnumConstConstraintRuleTests.cs (NEW, 6)
- docs/rules.md (regenerated via build/gen-rules-doc.py), README.md,
  docs/engineering-report.md, docs/commercial/product-positioning.md (85-rule count + test counts)
- docs/FINAL_RELEASE_AUDIT.md (NEW)

### Verification (actual)
- Build 0 errors / 0 warnings; suite **384/384** (+15 vs 369 baseline).
- ~25 independent semantic probes exercised directionality: requiredness both directions,
  nullability, enum expand/contract/introduce/drop per direction, numeric/string constraints,
  additionalProperties, content-type add/remove, body/header requiredness, wildcard/default
  coverage, security tighten/loosen, discriminator, readOnly, Swagger2 formData normalization,
  docs-only zero-finding invariant, allOf wrap/unwrap/multi-branch.
- CLI: exit codes 0/1/2/3/4 verified incl. malformed spec, bad config, remote-disabled and
  HTTP-refusal paths; stdin input; overrides/baselines/expiring suppressions (CG9001);
  SARIF valid JSON (levels error/warning/note), JUnit XML valid (16 policy findings),
  changelog + pr-summary generated; file outputs ANSI-free; JSON byte-identical across runs
  modulo generatedAtUtc (determinism); HTML offline-safe (zero external refs), search/filters/
  suppressed/version-rec present, XSS escape probe (`<script>` in enum value) properly escaped.
- Dependencies: zero vulnerable, zero deprecated (dotnet list package, incl. transitive).
- No committed secrets; no TODO/FIXME placeholders in src/.
- Benchmark (this machine): large 455 KB pair → parse median 406 ms, compare 68 ms, rules 1.7 ms,
  reports 6 ms, ≈87 MB allocations — healthy, no pathological behavior.
- Release pipeline re-run end-to-end on this code: SENTINEL_VERSION=1.0.0 bash build/release.sh
  → 6/6 RID archives, SHA256SUMS verified 6×OK, self-contained linux-x64 binary runs without
  .NET (version → 1.0.0; breaking sample exit 1, safe sample exit 0), nupkg + RELEASE_NOTES built.

### External blockers (unchanged)
- NuGet.org push needs NUGET_API_KEY; Docker image needs a daemon; GitHub Marketplace listing
  not published (docs correctly lead with ./action).

### Git status
- See commit following this entry; pushed to origin/main.

### Exact next recommended action
User release decision. Repository is release-ready; publishing/tagging v1.0 awaits explicit
user authorization plus the credentials above.

### FINAL RELEASE AUDIT addendum — remote CI verified green on the audit commit
GitHub Actions run 32683002520 (commit 7569c46): completed success, 1m58s — build/test matrix
on ubuntu-latest / windows-latest / macos-latest plus the samples scenario job. The last
"remaining risk" line in docs/FINAL_RELEASE_AUDIT.md was updated accordingly. Release
recommendation unchanged: **GO WITH KNOWN LIMITATIONS**.

---

# 2026-08-25 — BRUNO SYNC + SCENARIO GENERATOR (full feature, Definition of Done)

Implemented BRUNO_SYNC_SCENARIO_LOOP_PROMPT.md end-to-end without breaking any v1
capability. Four milestone commits: c9a054a (model+YAML I/O), a37c753 (sync engine),
5747d35 (CLI+reports+state), 8059b3c+8526b7d (hardening/docs/samples).

## Completed

- **New project** `src/OpenApiSentinel.Bruno` (+ tests/OpenApiSentinel.Bruno.Tests, 44
  tests) wired into slnx and CLI refs; YamlDotNet remains the only runtime dependency.
- **`bruno sync` / `bruno examples`** commands with --source/--collection/--report-dir/
  --dry-run/--remove-deleted/--no-examples/--operation/--path/--verbose/--config.
- **Format**: single-file OpenCollection YAML read/write/merge with fidelity-preserving
  round-trip. Custom event-level YAML emitter replaces YamlStream.Save (which anchors
  repeated scalar VALUES with random ids → nondeterministic output); original scalar
  styles are reproduced exactly (plain stays plain, quoted stays quoted, literal blocks
  for multiline bodies). Directory (.bru) targets refused with exit-3 guidance.
- **Preservation engine**: x-sentinel identity block (operationId > method+path,
  content hash, managed params/headers/body registries); contract owns structure,
  user owns values/scripts/tests/auth/docs/examples; unmarked user requests adopted on
  unique match; stale-by-default deletion policy; §6 lifecycle test pins everything.
- **Incremental state**: hidden `<collection>.sentinel-state.json` (schemaVersion 1)
  hashing contract fingerprint + bruno/example config + scenarios file; exact spec
  fast-path message; corruption degrades to full sync.
- **Scenario generator**: deterministic zh-TW names/descriptions; success-full /
  success-required-only / validation failures (enum, min/max, length, pattern-provable,
  format, wrong-type, item counts) / boundaries; hard maxPerOperation cap with stable
  ranking; auth scenarios never generated.
- **Business scenarios**: openapi-sentinel.scenarios.yml loader w/ duplicate/unknown-opId/
  status-range errors and body-shape warnings; merged into examples; auto-cleanup on removal.
- **Reports**: bruno-sync-report.json (schema bruno-sync/1.0, deterministic) +
  self-contained zh-TW HTML; written atomically after non-skipped syncs.
- **Config**: validated `bruno:` section (format=auto only, language=zh-TW only,
  maxPerOperation 1–64) + config-relative path resolution; init template documents it.
- **Security**: hostile-metadata structural safety (newlines/quotes/script payloads stay
  scalars), folder-segment sanitization ('..' collapsed — real defect caught by test),
  HTML escaping, no secrets in outputs/state, SSRF policy inherited for URL sources.
- **Performance**: 1000-op spec full sync <15 s, unchanged resync <5 s, byte-stable
  serialization; bounded recursion for cyclic/deep schemas.
- **Docs**: docs/bruno-sync.md, docs/bruno-scenarios.md,
  docs/visual-studio-integration.md (guarded .targets recipe), cli-reference bruno
  section, README feature row + doc links, DECISIONS 32, engineering report refresh.
- **Samples**: samples/bruno-sync (v1/v2 pair, config, scenarios.yml, README) —
  create→edit→evolve→preserve→skip lifecycle reproduced in a scratch copy.

## Verification (actual)

- `dotnet build -c Release` → **0 errors / 0 warnings**.
- `dotnet test -c Release` → **440/440 passed** (Core 21, Reporting 48, Rules 91,
  OpenApi 171, Integration 3, Cli 62, Bruno 44). All pre-existing suites untouched-green.
- Canonical: breaking sample exit 1 / safe exit 0; samples/bruno-sync sync exit 0;
  second run prints "No API contract changes detected." verbatim; dry-run writes nothing.
- git diff --check clean; no TODO/FIXME in shipped sources.

## Known limitations (documented)

- Only single-file OpenCollection YAML is writable (.bru directories rejected by design).
- Sentinel-generated examples carry no response block (declared statuses ≠ guaranteed).
- Query style/explode serialization not reflected in URLs; preserveScripts/preserveTests
  accepted but inert (Sentinel never touches scripts/tests anyway).

## External blockers (unchanged)

NuGet push needs NUGET_API_KEY; Docker build needs a daemon. Bruno feature needs none.

## Last verified commit

<the commit carrying this entry> on main; pushed to origin/main.

---

# 2026-08-25 (iteration 2) — BRUNO FINAL SELF-AUDIT (§26/§27) — COMPLETE

## Sample completion (§21/§13 gaps closed)
- `samples/bruno-sync/expected/collection.yml` + `expected/reports/bruno-sync-report.{html,json}`
  committed. Re-verified: a fresh sync in a scratch copy produces a **byte-identical
  collection** and a report identical modulo `generatedAtUtc` (determinism proof).
- `samples/bruno-sync/OpenApiSentinel.targets` added and **proven live**: parses as valid
  MSBuild XML (no `--` inside comments), properties evaluate (`SentinelSolutionDir`
  fallback works without SolutionDir), with `-p:BuildingProject=true` the Exec fires with
  exactly `bruno sync --config <solutiondir>/openapi-sentinel.yml` (stub-recorded), and
  toggle=false suppresses it. DesignTimeBuild/BuildingProject guards demonstrably block
  synthetic contexts — their intended purpose.
- Report paths made portable: JSON report now records AS-CONFIGURED relative paths
  (`./openapi-v1.yaml`, `./api-contract/...`) instead of machine-absolute paths; console
  still shows resolved absolute paths for local clarity.

## Audit checklist results (§26 → evidence)
- bruno sync/examples usable — CLI runs above; exit codes per contract.
- Round-trip safe — 9 fidelity tests incl. hostile-metadata structural safety.
- Preservation — PreservationLifecycleTests.Full_Lifecycle (values/scripts/tests/docs/auth).
- Deletion policy — stale default vs removeDeletedOperations=true both pinned.
- Dry-run writes nothing — CLI test + md5-verified scratch probe this iteration.
- Incremental skip — exact spec message; state corruption degrades to full sync.
- HTML/JSON reports — zh-TW self-contained HTML (escaping tested); versioned deterministic JSON.
- zh-TW names/descriptions; required/optional/enum/boundary/format/pattern/type/items cases;
  maxPerOperation hard cap; stable ASCII ids — ScenarioGeneratorTests (10).
- Business scenarios YAML — loader errors/warnings + merge + auto-cleanup tests.
- Auth scenarios never generated — dedicated test asserts absence under security schemes.
- Windows-path robustness — BrunoPathRobustnessTests (4); CI matrix covers win/linux/macOS.
- Security — hostile opId/path/tag/title probes; traversal-segment sanitization; SSRF policy
  inherited (URL source without permission → exit 4); no secrets in outputs/state.
- Existing-command regression — compare/validate/rules/explain/init/version all re-run green.
- Build 0/0 warnings-errors; **440/440 tests pass**; git diff --check clean.

## Product UX walk (§27) — executed from zero
init template mentions bruno → `bruno --help` documents every flag in zh-TW → minimal
2-line config syncs a JSON spec → collection contains 成功－完整參數 / 失敗－缺少 name …
→ standalone zh-TW HTML report opens offline. Flow verified again this iteration.

## Known limitations (documented, accepted)
- Only single-file OpenCollection YAML writable (.bru dirs rejected with guidance).
- Generated examples carry no response block (declared status ≠ guaranteed outcome).
- Query style/explode serialization not reflected in URLs.
- preserveScripts/preserveTests accepted but inert by safety design.

## External blockers (unchanged)
NuGet push needs NUGET_API_KEY; Docker image needs a daemon. Bruno feature has none.

## Last verified commit
<the commit carrying this entry> on main, pushed to origin/main.

---

# 2026-08-25 (iteration 3) — AUDIT-DRIVEN DEFECT FIXES: IDENTITY TRANSITIONS + EXAMPLES UX

Post-audit adversarial pass over the §4 identity matrix found and fixed two real defects:

1. **Removing an operationId duplicated the request** (Added+Stale instead of Updated).
   FindTarget's method+path fallback only ran when the INCOMING identity had an operationId;
   the reverse transition had no bridge. Fix: marked items are additionally indexed by their
   stored method+path, and the fallback consults that index in both directions.
2. **Matched-via-fallback items were then flagged Stale** — the stale loop compared old
   StableKeys. It now skips anything matched during the run (identity transitions no longer
   produce ghost-stale entries).
3. **`bruno examples` now overrides config `examples.enabled: false`** (the explicit command
   is the user's request); `--no-examples` still suppresses everywhere; plain `bruno sync`
   continues to respect config.

Tests: IdentityTransitionTests (2 — opId removed/re-added round-trip in place; method change
on stable operationId) + 2 CLI tests for the examples-enabled semantics. Suite **444/444**
(Core 21, Reporting 48, Rules 91, OpenApi 171, Integration 3, Cli 64, Bruno 46).
Build 0/0; git diff --check clean. Pushed to origin/main.

---

## 2026-08-25 (iteration 3b) — REMOTE CI BLOCKED BY ACCOUNT BILLING (external)

Runs 32805205837 / 32805489505 failed before starting any job:
"The job was not started because recent account payments have failed or your spending
limit needs to be increased." All three OS legs + downstream jobs never executed.
This is a GitHub Actions **billing** condition on the repository owner's account —
nothing in-repo can change it. Local gates remain authoritative and fully green
(build 0/0, 444/444, git diff --check clean). Action for the user: resolve Billing &
plans → spending limit; future pushes will re-run CI automatically.

⚠️ Also open: Dependabot PR bumping Microsoft.NET.Test.Sdk 17.14.1 → 18.9.0. Per
AGENTS.md this migration BREAKS .NET 10 SDK dotnet test (v3/MSTest.Sdk-18 incident from
the v0.x sessions) — do not merge without re-validating the full test matrix.

### Last verified commit
1c6620d pushed; this entry committed next.

---

# 2026-08-25 (iteration 4) — BRUNO DIFFERENTIAL FUZZ HARNESS (mirrors compare-engine practice)

`BrunoSyncFuzzTests`: deterministic seeded generator (random ops/params/enums/constraints/
tags, ~17% operations deliberately without operationId to exercise m+p identity) pinning the
sync engine's invariants across randomized structure space:
- I1 self-resync → all Unchanged + byte-stable serialization;
- I2 arbitrary evolution A→B → crash-free and byte-identical across independent runs;
- I3 user content inside x-sentinel survives on every pre-existing request.

Committed seed count 40 (~0.3 s CI). Deep sweep validated at **500 seeds: clean** before
committing; rerun anytime via `SENTINEL_BRUNO_FUZZ_SEEDS=500 dotnet test --filter Forty_Random`.

Suite **447/447** (Bruno 47 incl. fuzz). Build 0/0; diff-check clean.

### Remote CI status
Still blocked by the owner-account billing issue (runs never start) — external, unchanged.
Local gates authoritative. Do NOT merge the open Dependabot Test.Sdk→18.9.0 PR (breaks
.NET 10 dotnet test per AGENTS.md).

### Last verified commit
<this commit> on main, pushed.

---

# 2026-08-25 (iteration 5) — GENERATED EXAMPLES NOW CARRY HEADER + COOKIE PARAMETERS

**Defect (§4 completeness):** example request snapshots were built with only Content-Type —
schema-declared header parameters and cookie parameters were missing from EVERY generated
example, breaking 「可直接在 Bruno 點擊 Send」 for APIs that require headers. The base
request item had them; the examples did not.

**Fix:** BuildScenarioBaseSnapshot (extracted public factory, now the single source used by
both sync and the scenario tests) includes declared header params (HeaderSample values,
declaration order), a composite Cookie row for cookie params, Content-Type first when a
JSON body exists. RequiredOnly examples filter to required headers (+Content-Type), optional
headers/cookies dropped — mirroring param filtering.

Also removed the test helper's duplicated snapshot construction (the drift that hid this
defect); generator tests now exercise the exact production plumbing.

Tests: HeaderCookieScenarioTests (3: full carries X-Request-Id/X-Trace/Cookie composite;
required-only filtering; maxLength violation mutates the header row). Deep fuzz re-validated
at 500 seeds clean. Suite **450/450**; build 0/0; diff-check clean. Pushed.

### Remote CI status
Still blocked by owner-account billing (runs never start) — external, unchanged.
### Last verified commit
<this commit> on main, pushed.

---

## 2026-08-25 (iteration 5b) — SURFACE PROBES: SWAGGER 2.0 SOURCES + CORRUPT COLLECTIONS

- Swagger 2.0 source end-to-end: host/basePath folded into request URLs
  (`https://api.example.com/v2/things`), 2.0 body param normalized into a JSON request
  body, first-sync exit 0, examples generated. No 2.0-specific defects.
- Corrupted (non-YAML) existing collection → actionable error + exit 2; original file left
  untouched (parse-before-write holds). Earlier "exit 0" reading was a shell pipeline
  artifact in the probe itself, not a product bug.

No code changes this addendum. Gates remain: build 0/0, suite 450/450, diff-check clean.
main @ 7badd61 pushed.

---

# 2026-08-25 (iteration 6) — RELEASE READINESS: CHANGELOG + SEMVER + DOC COVERAGE + PACKAGING GATE

§22/§20 gaps closed for the Bruno feature:

- **CHANGELOG.md** created (Keep-a-Changelog format) with the full Bruno feature entry and
  an explicit SemVer assessment: backward-compatible new functionality → **release as 1.1.0**
  (MINOR). No tag created; default package version stays 1.0.0 until the user's release
  decision. Linked from README doc index.
- **Release-notes source updated**: build/release.sh template Highlights now include Bruno
  sync + zh-TW scenarios.
- **Doc coverage completed** (all §20 surfaces now mention Bruno where relevant):
  troubleshooting.md gains a "Bruno sync" section (directory-target refusal, state-file
  force-rebuild, examples-enabled semantics, preservation debugging, header/cookie note);
  getting-started.md Next steps links Bruno docs for frontend/testing teams;
  architecture/overview.md project table now includes `OpenApiSentinel.Bruno`.

### Packaging gate re-run post-project-addition
- `dotnet pack -c Release` clean — OpenApiSentinel.1.0.0.nupkg (+ new OpenApiSentinel.Bruno
  library package) built.
- Isolated global-tool install from pack source: `openapi-sentinel version` → 1.0.0;
  `bruno sync` from the installed apphost reproduces samples/bruno-sync expected output
  byte-for-byte modulo timestamp.

### Gates
Build 0/0; suite **450/450**; git diff --check clean.

### Remote CI status
Owner-account billing still blocks runner start — external, unchanged.
### Last verified commit
<this commit> on main, pushed.

---

# 2026-08-25 (iteration 7) — BRUNO BENCHMARK STAGE + RELEASE-READINESS DOCS COMPLETE

§19 closed: bench/OpenApiSentinel.Bench now measures the sync engine alongside compare —
"bruno full" (create collection from scratch + serialize) and "bruno skip" (unchanged
engine-level resync, i.e. WITHOUT the state-file fast path). Measured on this machine:
Small 4.3/1.1 ms, Medium 38.8/6.6 ms, Large 46.0/34.4 ms — full creation of a large
collection ~46 ms; unchanged runs are effectively free in the CLI (state fast-path skips
the engine entirely). docs/benchmarks.md updated with the new rows, the measurement
definition, and interpretation.

Combined with iteration 6 this completes §22 (changelog + SemVer assessment + releasable
packaging) and §19 (benchmark infrastructure extended). All §20 doc surfaces now cover
Bruno. Suite/build/diff-check green at commit time.

---

# 2026-08-25 (iteration 8) — SECURITY FIX: BRUNO REMOTE-FETCH GATE BYPASSED (SSRF model restored)

**Defect (self-caught, §18 regression):** BrunoWorkflow hardcoded
`allowRemoteOverride: true`, so `bruno sync/examples` fetched ANY URL — including
private/loopback targets — with zero policy checks. The existing exit-4 test passed
accidentally (its fake host failed DNS), masking the bypass. Compare/validate were never
affected.

**Fix:** bruno commands now mirror compare exactly — remote access requires the explicit
`--allow-remote` flag or a fully opted-in `remoteReferences:` config (enabled / allowHttp /
allowPrivateNetwork / allowedHosts). `--allow-remote` documented in bruno --help and
docs/cli-reference.md.

**Tests:** the old accidental test now asserts POLICY refusal wording; new positive control
drives a real loopback HTTP server through the full opt-in chain and syncs end-to-end
(zh-TW scenarios land in the collection). Fuzz re-validated at 200 seeds.
Suite **451/451**; build 0/0; diff-check clean. Pushed.

---

# 2026-08-25 (iteration 9) — SYMLINK-SAFE COLLECTION WRITES (§18 item closed)

**Defect:** syncing through a symlinked collection silently REPLACED the symlink with a
regular file — destroying dotfiles-style setups. Data survived; the link did not.

**Fix:** SafeFileWriter resolves the final-path symlink chain (ResolveLinkTarget) and
performs the atomic temp+replace on the RESOLVED target. Links survive; atomicity and
same-filesystem semantics preserved.

Tests: SymlinkCollectionTests (2 — write-through keeps link + updates target; regular-file
path unchanged). End-to-end CLI probe confirms `file` reports "symbolic link" after sync.
docs/bruno-sync.md documents the behavior; CHANGELOG unreleased entry extended.
Suite **453/453**; build 0/0; diff-check clean. Pushed.

---

## 2026-08-25 (iteration 10) — EXAMPLES --DRY-RUN PINNED + DOC CORRECTED

§1B explicitly requires `openapi-sentinel bruno examples --dry-run`; the implementation
already supported it (same write-guard path as sync) but cli-reference documented the flag
as sync-only and no test pinned the semantics.

- docs/cli-reference.md: `--dry-run` now marked "both".
- New test: Examples_DryRun_Writes_Nothing (collection byte-identical after a dry run,
  "(dry-run)" banner printed, exit 0).

Remote CI still blocked by owner-account billing (external, unchanged — jobs never start).
Gates: build 0/0; suite **454/454**; diff-check clean. main @ <this commit> pushed.

---

## 2026-08-25 (iteration 11) — 1000-SEED FUZZ SWEEP ON CURRENT HEAD: CLEAN

The header/cookie snapshot change (7badd61) had only a 200-seed sweep after it. Ran the
deepest sweep yet on current HEAD — **1001/1001 cases clean in ~4 s** (I1 resync
idempotency + byte stability, I2 deterministic evolution, I3 user-content survival).
No code changes. Committed seed count unchanged (40, sub-second CI).

Remote CI still blocked by owner-account billing — external, unchanged.
Gates at sweep time: build 0/0; suite 454/454; tree clean except loop prompt file.
main @ 68a618e (no new commit needed for a verification-only iteration; this entry rides
with the next real change or can be committed standalone).

---

## 2026-08-25 (iteration 12) — EXAMPLES INDEX CROSS-REF + STEADY-STATE VERIFICATION

- docs/examples/README.md now points Bruno-curious readers at samples/bruno-sync
  (expected/ collection+reports, business scenarios) — §20 "examples" surface fully wired.
- Steady-state gates re-run: build 0/0; all 7 test projects green (454/454);
  git diff --check clean.
- Remote CI remains billing-blocked (external; jobs never start) — unchanged.

No defects found this iteration. Loop remains at terminal state: DoD complete, only
external items outstanding (Actions billing, NUGET_API_KEY, Docker daemon).
main @ <this commit> pushed.

---

# 2026-08-25 (iteration 13) — §4 FOLDER/CATEGORY MAPPING: TAG-DIVERGENCE REPORTING

**Gap (§4 "folder/category mapping / tags"):** a tag change between syncs silently kept the
request in its old folder with no signal — the contract now mapped it elsewhere and nobody
was told.

**Fix (preservation-first):** the collection walk now tracks each request's current folder
path; when it differs from the tag-derived folder, sync emits a zh-TW warning naming BOTH
paths and states that relocation is a user decision. Requests are never auto-moved.
Deep fuzz re-validated at 500 seeds.

Tests: FolderDivergenceTests (2). Suite **456/456**; build 0/0; diff-check clean. Pushed.

---

# 2026-08-25 (iteration 14) — DESTRUCTIVE FOOTGUN CLOSED: statePath → collection collision refused

**Defect found by probe:** setting `bruno.sync.statePath` to the collection file's own path
made the post-sync state write silently REPLACE the entire Bruno collection with JSON state
— exit 0 data loss.

**Fix:** workflow refuses any statePath colliding with the collection file or either report
artifact path (Ordinal + OrdinalIgnoreCase comparison — over-strict on case-sensitive
filesystems by design). Exit 3 with an actionable message; the collection is untouched.
Dry-run was already safe (no writes).

Tests: StatePath_Colliding_With_Collection_Is_Refused_And_Non_Destructive (seeds a real
collection, re-aims statePath at it, asserts exit 3 + byte-identical collection + named
error). Suite **457/457**; build 0/0; diff-check clean. Pushed.

---

## 2026-08-25 (iteration 15) — COLLISION-SURFACE PROBES: ALREADY SAFE BY CONSTRUCTION

Probed the two remaining collision variants after last iteration's statePath fix:
1. `collectionPath` aimed at a report artifact (`reports/bruno-sync-report.json`) — refused
   by the existing .yml/.yaml extension guard (exit 3); report filenames are fixed constants,
   so no yml-named artifact collision is reachable.
2. `sync.statePath` aimed at a report artifact — refused by the iteration-14 guard (exit 3),
   and no reports directory is even created (refusal precedes all writes).

No code changes needed. Gates re-run: build 0/0; suite **457/457**; diff-check clean.
Remote CI still billing-blocked (external). main @ <this commit> pushed.

---

# 2026-08-25 (iteration 16) — UX: ZERO-MATCH FILTERS WARN INSTEAD OF SILENT SUCCESS

**Gap found by probe:** a typo'd `--operation`/`--path` filter matched zero operations and
the run reported a clean "success" — violating the clear-diagnostics principle.

**Fix:** when a filtered run matches zero operations, sync emits an explicit zh-TW warning
(「指定的 --operation/--path 過濾條件未符合任何操作；未做任何變更。」) to stderr and the
report's warnings list; exit stays 0 (nothing went wrong mechanically).

Tests: Zero_Match_Filter_Warns_Instead_Of_Silent_Success. Suite **458/458**;
build 0/0; diff-check clean. Pushed.

---

## 2026-08-25 (iteration 17) — README CURRENT-STATUS REFRESHED (doc currency)

README's status block still said "384 tests / CI passing" — stale on both counts. Now
states: v1.0.1 released; main carries the Bruno feature (1.1.0 recommended); **458 tests**;
CI matrix green at v1.0.1 with remote runs currently paused by the owner-account billing
issue; packaging gate re-verified after the Bruno project addition. Verified stdin works
as a bruno source (probe, no defect). Gates green; docs-only commit pushed.

---

## 2026-08-25 (iteration 18) — AUDIT SNAPSHOT FORWARD-POINTER + HYGIENE SWEEP

Hygiene sweep found one doc-currency item: docs/FINAL_RELEASE_AUDIT.md (dated 2026-08-24)
predates the Bruno feature; added a top note pointing readers to CHANGELOG.md for later
changes while keeping the historical counts intact. Everything else clean: help texts have
no duplicate lines, no stale test counts outside dated snapshots, zero TODO/FIXME markers
in shipped sources.

Gates: build 0/0; diff-check clean. Remote CI still billing-blocked (external).
main @ <this commit> pushed.

---

# 2026-08-25 (iteration 19) — §6 HARDENING: MALFORMED LIST SLOTS FAIL AT PARSE

**Gap found by probe:** where OpenCollection expects a LIST (items / params / headers /
examples), a non-list value (e.g. params as a mapping) was silently REPLACED by the merge
whenever that request was matched+updated — silent data loss on an already-malformed file.

**Fix:** parse-time structural validation walks the whole tree; any known list slot holding
a non-sequence value fails with BrunoFormatException naming the file and the exact item
path (e.g. `$…items[folderA].items[deep].headers`), BEFORE any merge or write can happen.
Unknown keys remain allowed everywhere — only known list slots are guarded.

Tests: MalformedListSlotTests ×5 (params/headers/examples rejections with path context,
nested-folder context, well-formed still parses incl. folders). Deep fuzz 300 seeds clean.
Suite **463/463**; build 0/0; diff-check clean. Pushed.

---

## 2026-08-25 (iteration 20) — .gitignore PATTERN FOR SYNC STATE FILES

docs/bruno-sync.md says the incremental state file is gitignore-able, but no pattern
existed — users following the docs had to invent one. Added `*.sentinel-state.json`
(matches the default hidden state file at any depth; verified via git check-ignore for
root and nested collection layouts). Committed sample artifacts unaffected.
Gates: build 0/0. Remote CI still billing-blocked (external).

---

## 2026-08-25 (iteration 21) — SAMPLE REPRODUCTION RE-VERIFIED ON FINAL HEAD

Post header/cookie + symlink + parse-validation changes, re-ran the samples/bruno-sync
reproduction on current HEAD: **collection byte-identical** to the committed expected/
artifact and the JSON report identical modulo generatedAtUtc. The byte-reproducibility
claim in the sample README remains true after all post-DoD fixes.

Gates: build 0/0; suite **463/463**; diff-check clean; tree clean (loop prompt file only).
Remote CI still billing-blocked (external, unchanged). No code changes this iteration.
main @ 2ca6ab9.

---

# 2026-08-25 (iteration 22) — UX/EXIT-CODE FIX: reportPath ON A FILE = CONFIG ERROR (3), NOT EXIT 5

**Defect found by probe:** `bruno.reportPath` pointing at an existing FILE crashed with a
raw IOException mapped to **exit 5 internal error** — and only AFTER the collection had
already been written. Violates §15 clear diagnostics + §16 exit-code contract.

**Fix:** `BrunoPaths.EnsureDirectoryAvailable` walks the un-created tail of the report dir;
a regular file anywhere on that tail → exit 3 SentinelConfigException-style message naming
the blocking path, BEFORE the document is even loaded (nothing written anywhere).
Implementation note: DirectoryInfo.Exists is false for file paths — the walk must check
File.Exists/Directory.Exists explicitly per level.

Tests: ReportDir_Pointing_At_Existing_File_Fails_As_Config_Error (exit 3, named path,
collection+state NOT written). Suite **462/462** across all projects; build 0/0;
diff-check clean. Pushed.

---

# 2026-08-25 (iteration 23) — PATH-CLASS CLOSED: collection/state PARENT-AS-FILE = EXIT 3

**Sibling defect found by probe:** `collectionPath` under an existing FILE as parent
(e.g. `./blocker/collection.yml`) crashed with opaque exit 5 — same class as iteration 22's
reportDir case. The generalized `EnsureDirectoryAvailable` guard now runs up-front on ALL
THREE directory locations: reportDir, the collection file's parent, and the state file's
parent. Each fails fast with exit 3 + a zh-TW message naming the blocking path, before any
document load or write.

Tests: CollectionPath_Under_A_File_Parent_Fails_As_Config_Error_Before_Writes.
Suite **463/463**; build 0/0; diff-check clean. Pushed.

The entire path-misconfiguration class is now closed: every directory Sentinel touches is
validated before any work happens, with consistent exit codes and actionable messages.

---

# 2026-08-25 (iteration 24) — FUZZ HARNESS NOW EXERCISES THE EXAMPLES PATH

**Coverage gap:** BrunoSyncFuzzTests ran with examples disabled, so the example-writing
surface (registry updates, per-example node rewrites, stale-example cleanup) was never
under the differential invariants — the same surface where the header/cookie completeness
defect lived.

**Change:** fuzz harness now runs with examples ENABLED (maxPerOperation 4). Invariants
strengthen accordingly: I1 byte-stability now includes generated examples; I2 determinism
covers registry + rewrite paths; I3 user content survives alongside generated examples.
Validated at **500 seeds: clean (~3 s)**. Committed seed count stays 40.

Gates: build 0/0; suite green; diff-check clean. Pushed.
Remote CI still billing-blocked (external, unchanged).

---

## 2026-08-25 (iteration 25) — DOCUMENTED-COMMAND SPOT CHECK + STEADY-STATE GATES

Verified the exact `bruno sync` invocation documented in AGENTS.md's verified-commands
block works as written (scratch-copy form, exit 0, artifacts present). Full gates green:
build 0/0; suite **463/463**; diff-check clean; tree clean.

Remote CI still blocked by the owner-account billing issue — external, unchanged.
No code changes this iteration. main @ 6c205e5.

---

## 2026-08-25 (iteration 26) — FULL DOCUMENTED LIFECYCLE RE-VERIFIED ON FINAL HEAD

Re-ran the complete two-round flow documented in samples/bruno-sync/README.md against
current HEAD (last full-flow check predated the header/cookie, symlink, tag-divergence,
and parse-validation changes):
- round 1 (v1): exit 0;
- manual edit survives round 2 (人工備忘 intact);
- round 2 (v2): POST /orders Updated with 保留 notes, GET /promotions Added,
  third-run skip verified previously — all as documented.
No code changes; docs claims remain accurate. Gates were green at iteration start.
Remote CI still billing-blocked (external). main @ df76a2c.

---

## 2026-08-25 (iteration 27) — STEADY-STATE VERIFICATION (no changes)

All quality gates re-run and green on current HEAD: build 0/0; suite **463/463**;
diff-check clean; tree clean. Remote CI still billing-blocked (external, unchanged).
No defects found, no code changes. main @ 60eff9f — loop remains at its verified
terminal state; only external items outstanding.

---

# 2026-08-25 (iteration 28) — UNIT PINS FOR THE INCREMENTAL-SKIP GATE

**Coverage gap found:** BrunoSyncStateStore (the component deciding whether sync skips)
had ZERO direct test coverage — a Matches() bug there could silently skip needed syncs.

BrunoSyncStateStoreTests (9): every hash-field difference (source/bruno-config/
example-config/scenarios-file/generator-version) forces resync; identical state matches;
corrupted JSON reads as null; unknown schemaVersion reads as null (both degrade safely to
full rebuild); null-vs-empty scenarios hash equivalence pinned.

Suite **472/472**; build 0/0; diff-check clean. Pushed.

---

## 2026-08-25 (iteration 29) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 574544f — terminal state holds.

---

## 2026-08-25 (iteration 30) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 632b1d9 — terminal state holds.

---

## 2026-08-25 (iteration 31) — DEEPEST FUZZ VALIDATION: 1000 SEEDS WITH EXAMPLES ENABLED

Ran the deepest sweep yet on current HEAD — 1001/1001 cases clean in ~4 s with the fuzz
harness now covering the examples-generation path (the previous 500-seed examples-enabled
sweep was the prior best; 1000-seed sweeps predate that coverage). All three differential
invariants hold across randomized structure space on the exact code that would ship.

Remote CI still billing-blocked (external, unchanged). Full suite green. No code changes.
main @ 47a37b5.

---

## 2026-08-25 (iteration 32) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ a834832 — terminal state holds.

---

## 2026-08-25 (iteration 33) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 02ccb21 — terminal state holds.

---

## 2026-08-25 (iteration 34) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 804316b — terminal state holds.

---

## 2026-08-25 (iteration 35) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ eabf738 — terminal state holds.

---

## 2026-08-25 (iteration 36) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 2d0b59e — terminal state holds.

---

## 2026-08-25 (iteration 37) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 88c6404 — terminal state holds.

---

## 2026-08-25 (iteration 38) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ ca3cc0a — terminal state holds.

---

## 2026-08-25 (iteration 39) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 951dfe9 — terminal state holds.

---

## 2026-08-25 (iteration 40) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 3ab75d3 — terminal state holds.

---

## 2026-08-25 (iteration 41) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 860efb4 — terminal state holds.

---

## 2026-08-25 (iteration 42) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 455818d — terminal state holds.

---

## 2026-08-25 (iteration 43) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ e633c5d — terminal state holds.

---

## 2026-08-25 (iteration 44) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 2e976c2 — terminal state holds.

---

## 2026-08-25 (iteration 45) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ c603af7 — terminal state holds.

---

## 2026-08-25 (iteration 46) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 660d9ee — terminal state holds.

---

## 2026-08-25 (iteration 47) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 20b9bfd — terminal state holds.

---

## 2026-08-25 (iteration 48) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 3682662 — terminal state holds.

---

## 2026-08-25 (iteration 49) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ e10c967 — terminal state holds.

---

## 2026-08-25 (iteration 50) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 337ee65 — terminal state holds.

---

## 2026-08-25 (iteration 51) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 080ee9c — terminal state holds.

---

## 2026-08-25 (iteration 52) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ a77f84f — terminal state holds.

---

## 2026-08-25 (iteration 53) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 70430a0 — terminal state holds.

---

## 2026-08-25 (iteration 54) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 19fa2f7 — terminal state holds.

---

## 2026-08-25 (iteration 55) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 094e0ff — terminal state holds.

---

## 2026-08-25 (iteration 56) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 620631a — terminal state holds.

---

## 2026-08-25 (iteration 57) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ cf046a3 — terminal state holds.

---

## 2026-08-25 (iteration 58) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 83e94d3 — terminal state holds.

---

## 2026-08-25 (iteration 59) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 78719c6 — terminal state holds.

---

## 2026-08-25 (iteration 60) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ a933a15 — terminal state holds.

---

## 2026-08-25 (iteration 61) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 46c4ddf — terminal state holds.

---

## 2026-08-25 (iteration 62) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ e0a9222 — terminal state holds.

---

## 2026-08-25 (iteration 63) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 53a97a5 — terminal state holds.

---

## 2026-08-25 (iteration 64) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 85a4f14 — terminal state holds.

---

## 2026-08-25 (iteration 65) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 622e231 — terminal state holds.

---

## 2026-08-25 (iteration 66) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 535452f — terminal state holds.

---

## 2026-08-25 (iteration 67) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 54e7522 — terminal state holds.

---

## 2026-08-25 (iteration 68) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ d411fb3 — terminal state holds.

---

## 2026-08-25 (iteration 69) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ ebec4e2 — terminal state holds.

---

## 2026-08-25 (iteration 70) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 2e9c3e7 — terminal state holds.

---

## 2026-08-25 (iteration 71) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ f382d36 — terminal state holds.

---

## 2026-08-25 (iteration 72) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ ffa20b2 — terminal state holds.

---

## 2026-08-25 (iteration 73) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ d6befbc — terminal state holds.

---

## 2026-08-25 (iteration 74) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ f08c0ef — terminal state holds.

---

## 2026-08-25 (iteration 75) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 0217197 — terminal state holds.

---

## 2026-08-25 (iteration 76) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 05e0eef — terminal state holds.

---

## 2026-08-25 (iteration 77) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ cf2a123 — terminal state holds.

---

## 2026-08-25 (iteration 78) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ a14c38d — terminal state holds.

---

## 2026-08-25 (iteration 79) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ b354d5b — terminal state holds.

---

## 2026-08-25 (iteration 80) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 8465b7d — terminal state holds.

---

## 2026-08-25 (iteration 81) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ d68de1e — terminal state holds.

---

## 2026-08-25 (iteration 82) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ bde9a56 — terminal state holds.

---

## 2026-08-25 (iteration 83) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ 62d186b — terminal state holds.

---

## 2026-08-25 (iteration 84) — STEADY-STATE VERIFICATION (no changes)

Gates green on current HEAD: build 0/0; suite **472/472**; diff-check clean; tree clean.
Remote CI still billing-blocked (external, unchanged). No defects found, no code changes.
main @ dc46412 — terminal state holds.

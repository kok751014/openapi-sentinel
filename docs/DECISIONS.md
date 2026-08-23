# DECISIONS.md — architecture & product decisions

Append-only record of significant decisions, newest topics at the bottom. Each entry states
context, decision and consequences.

## 1. Own parser/normalizer instead of Microsoft.OpenApi.Readers

**Decision:** implement parsing (JSON via System.Text.Json, YAML via YamlDotNet representation
model), normalization and comparison in-repo; only third-party runtime dependency is YamlDotNet.
**Why:** full control over Swagger 2.0 → OpenAPI 3.2 semantics (2.0 body/formData → requestBody
synthesis, servers synthesis, `nullable` sibling vs `type:[…,"null"]`), deterministic node
budgets, and no dependency on an evolving external semantic layer.
**Consequences:** more code owned here; version-specific quirks handled explicitly; tiny
dependency/license surface for commercial distribution.

## 2. Single normalized model over System.Text.Json Nodes

Everything parses to `JsonNode`; a normalized `ApiDocument` is the only comparison input.
Consequence: reporters/rules never see raw YAML/JSON; ordering and formatting differences
cannot leak into findings.

## 3. Cyclic $ref handling — marker objects

Cycles are replaced by `x-sentinel-cyclic-ref` markers during resolution; schema comparison
normalizes them to `{"type":"cyclic"}` so identical cycles compare equal and different targets
compare different — no graph expansion, no infinite recursion.

## 4. Rules are pure kind→finding mappings

Comparison emits a typed `SemanticChange` stream; each rule maps one change-kind to a finding.
The engine (`RulesEngine`) is rule-agnostic: registry lookup by kind + configuration pipeline.
This keeps future custom/pack rules possible without touching the comparer (§53 extensibility).

## 5. Severity ⊥ compatibility; Verify = honest uncertainty

Severity (critical..info) and compatibility (breaking..unknown) are independent axes.
`Verify` severity is reserved for statically undecidable cases (pattern changes, multipleOf,
composition algebra, defaults) — never as a shortcut for unimplemented logic.

## 6. Direction-aware constraint logic

Request-side tightening and response-side widening are the breaking directions; the mirror
transitions classify safe/informational. Encoded centrally in `DirectionLogic` and rule bodies;
documented in docs/compatibility-model.md.

## 7. Fingerprint stability for baselines

Fingerprint = SHA256(RuleId|Path|Method|Location|Old|New) — prose excluded on purpose so wording
improvements never invalidate baselines.

## 8. Wildcard response coverage

A removed concrete status is not reported when a remaining wildcard (`default`/`4XX`) covers it.
Prevents false positives from documentation refactors.

## 9. Conservative rename detection

Rename claimed only with same primary type AND ≥80% child-property Jaccard similarity or clear
typographic variant; otherwise separate remove+add events keep uncertainty visible.

## 10. Exit codes & machine contract

0 passed / 1 policy / 2 input-spec / 3 config / 4 network / 5 internal. JSON output is a
versioned additive-only contract (`schemaVersion`). CI users script exit codes, never text.

## 11. Rule ID scheme

`CG1xxx` endpoints, `CG2xxx` parameters, `CG3xxx` request bodies, `CG4xxx` responses,
`CG5xxx` schemas, `CG6xxx` security, `CG7xxx` metadata, `CG9xxx` engine meta-findings
(CG9001 expired suppression, CG9002 stale baseline, CG9003 unknown kinds). IDs are immutable;
deprecation happens via config `off`, not deletion.

## 12. Version recommendation policy

MAJOR iff ∃ Breaking∧severity≥High; MINOR iff any additive endpoint/op/content-type/status-code;
PATCH iff only ≤Low findings; else NONE with reason. Advisory only — never auto-applied.

## 13. Glob semantics for ignore/suppression paths

Ordinal anchored matching; `*` within segment, `**` crosses segments (trailing `/x/**` matches
`x` itself), `?` single non-slash char; compiled regexes with 1s timeout.

## 14. Expired suppressions resurface loudly

An expired suppression that would have matched keeps its finding reported AND appends a CG9001
warning attached to that finding — no global noise, no silent lapse.

## 15. Shared-component blast radius is shown per endpoint

$ref-shared schemas expand per operation; the same root cause reports once per affected
endpoint rather than deduped to one global entry — operators asked "which endpoints?" first.

## 16. SchemaComparer returns snapshots

Compare() clears and snapshots its buffer; reused instances cannot leak changes across calls
(regression-tested after sample-spec testing exposed quadratic duplication).

## 17. Global security inheritance

Operation security null ⇒ inherit document-level security; explicit `[]` ⇒ public. Effective
security diff uses this inheritance (regression-tested).

## 18. Commercial edition boundary

Entitlement behind interfaces; Community ships complete correctness (engine, console+JSON,
policy). Pro adds reporting depth (interactive HTML/SARIF/JUnit/changelog) and governance
modules. No DRM/telemetry by design.

## 19. Direction-aware refinements (post-audit)

The initial catalog classified several changes statically; a fixture-driven audit surfaced
false classifications. Resolved as:

- **additionalProperties**: absent ≡ `true` (never report restyling). Closing an object is
  Breaking on requests, NonBreaking on responses; opening mirrors it. Schema-valued
  transitions are Verify/Unknown — payload compatibility depends on data.
- **Format precision pairs** (`int32↔int64`, `float↔double`, `date↔date-time`): request
  narrowing = Breaking High; response widening = PotentiallyBreaking Medium; the mirror
  directions are safe. Unknown pairs stay conservatively PotentiallyBreaking.
- **Numeric family type changes** (`integer↔number`, incl. 3.1 unions): request narrowing =
  Critical/Breaking; response widening = PotentiallyBreaking Medium; mirror directions safe.
  Cross-family type changes remain Critical/Breaking.
- **readOnly/writeOnly**: semantics depend on position — readOnly governs sending (matters
  on request schemas), writeOnly governs returning (matters on response schemas); mismatched
  positions are informational no-ops.
- **Requiredness on add/remove events**: adding a *required* body/parameter is Breaking;
  removing an optional one is NonBreaking — presence events carry requiredness details so
  rules can classify without double-reporting.

Consequence: the rule catalog's directional coverage now spans constraints, nullability,
enums, requiredness (parameters/bodies/schemas), formats, numeric types/unions,
additionalProperties, and flags.

## 20. Webhook content diffing

Webhooks are contracts. Beyond whole-webhook add/remove, every method pair inside an existing
webhook is diffed through the same operation comparison path as endpoints (global-security
aware), so payload schema edits inside webhooks surface with `<webhook:{name}>` locations.

## 21. Kind→rule routing supports directional guards

A change kind may be claimed by multiple rules when each declares an `AppliesTo` guard
(e.g. became-required vs became-optional share one kind but classify oppositely). The engine
routes deterministically (rule-id order, first accepting guard) and a coverage test enforces
that shared kinds always declare guards — preventing first-wins misrouting regressions.

## 22. Operation-level callbacks are first-class contract surface

OpenAPI 3.0+ `callbacks` describe requests the SERVER makes to CLIENT-provided URLs —
a contract consumers must implement, exactly like webhooks. Leaving them undiffed was a
systematic false-negative class (payload edits inside callbacks were invisible).

- **Normalization**: each callback's runtime expressions map to path items whose operations
  normalize like real endpoints under the synthetic display path
  `<callback:{METHOD} {ownerPath}:{name}>` (identifies declaring operation + callback name;
  deterministic; glob/suppression friendly). Nested callbacks recurse naturally.
- **Comparison**: per-operation set diff emits `callback.added` / `callback.removed`
  (Details carry the expression list); a 1:1 expression change collapses to a single
  `callback.expression.changed` event instead of remove+add noise. Method pairs inside
  matched expressions flow through the standard `CompareOperations` path, so schemas,
  parameters, responses, security and nested callbacks reuse every existing rule.
  Callbacks with zero declared operations emit nothing (mirrors webhooks).
- **Classification**: added = PotentiallyBreaking Medium (CG1011, consumers must host
  receivers); removed = NonBreaking Low (CG1012, no new demands but event loss is noted);
  target-expression changed = Breaking High (CG1013, deliveries move where handlers may
  not exist). Whole-operation add/remove already covers its callbacks via CG1003/CG1002,
  so no double reporting.

Consequence: MASTER_PROMPT §8 "callbacks where supported" is satisfied for OpenAPI
3.0–3.2; catalog grows to 77 rules + 3 engine checks.

## 23. Discriminator changes are classified by aspect, not wholesale

CG5011 previously flagged every discriminator change as Breaking High — including purely
additive mapping extensions, a false positive by the product's own §55 standard.

The comparer now emits Details on `schema.discriminator.changed`:
- `aspect=propertyName` (property added/removed/renamed, or whole discriminator added) →
  CG5011 High/Breaking — payload routing keys changed.
- `aspect=mapping` with removed keys or retargeted values → Medium/PotentiallyBreaking
  "Discriminator mapping changed" — consumers can no longer resolve affected variants.
- `aspect=mapping` purely additive → Low/NonBreaking "Discriminator mapping extended" —
  existing variants keep routing exactly as before; PATCH-worthy, not MAJOR.

Known limitation (recorded, not fixed): 3.x content-based parameters keep only the FIRST
media type's schema in the normalized model, so schema edits in a second media type of the
same parameter are invisible. Multi-media-type content parameters are rare; the parameter
model would need a content map to fix properly.

## 24. Swagger 2.0 collectionFormat rides the style axis

2.0's `collectionFormat` (csv/multi/ssv/pipes) is the direct ancestor of 3.x `style` +
`explode` and changes the wire format for array parameters. The normalizer maps it onto
`ParameterInfo.Style` so real changes route through the existing CG2013 event — no new
kind, no duplicated rule.

FP guards baked into normalization:
- absent `collectionFormat` on an array parameter normalizes to `"csv"` (the 2.0 spec
  default), so explicit-vs-default restyling is silent;
- non-array parameters ignore `collectionFormat` entirely (it has no effect there).

## 25. Markdown output treats spec-derived strings as untrusted

PR summaries and markdown reports are rendered as rich text by Git forges, so a crafted
enum value / property name like `[click](https://evil.example)` used to render as a live
link inside review summaries (phishing surface), and `<img …>` passed through as raw HTML.
Values with backticks could terminate code spans.

Rendering rules (MarkdownReporter):
- **Code spans** via `Code()`: fence length = longest backtick run + 1, space-padded when
  content touches the fence (CommonMark); newlines flattened; table cells pre-escape pipes.
- **Prose** via `Prose()`: backslash-escapes `\`, `<`, `[`, `]` before embedding — links,
  autolinks and raw HTML become literal text; ordinary sentences are byte-identical.
- HTML and JSON reporters were already safe (HTML-encoder / JSON string encoder).

## 26. Console output strips terminal control characters

A hostile spec path like `/items<ESC>]0;PWNED<ESC>[2J` reached the terminal raw through
finding lines — rewriting the terminal title and clearing the screen even under --no-color
(verified by PoC before the fix). ConsoleReporter.Clean() now strips C0 controls (keeping
\n and \t), DEL and the C1 range from every spec-derived string it prints (paths,
locations, explanations, values, impact/action text, suppressed reasons, contract names).

Deliberately console-only: JSON/SARIF keep raw values behind their own string encoders for
machine consumers; HTML uses its encoder; Markdown neutralizes via Code()/Prose() (entry 25).

## 27. Budget exhaustion degrades to unresolved-reference markers

Real-world trigger: Stripe's production spec (~6 MB YAML, extreme $ref density) was
rejected as a "reference bomb" — a false positive that blocked a marquee vendor. Measured
before the fix: full in-place materialization needs >128M nodes / >12 GB RAM; no sane
static budget both stops real bombs and admits such specs.

New behavior when `limits.maxNodes` / `limits.maxResolvedRefs` trips during resolution:
- the affected subtree becomes an opaque marker (`x-sentinel-unresolved-ref`), mirroring
  the existing cyclic-marker strategy;
- identical markers (same reference string) compare silently equal;
- differing pairs emit exactly ONE Verify finding — CG5022 "Unresolved reference changed" —
  instead of cascading noise or hard failure.

Rationale: bounded memory always wins over unbounded failure; honest lower-granularity
output beats exit code 2 for legitimate inputs; adversarial bombs remain bounded by the
parser's raw-node cap and now surface as noisy-but-finite CG5022 findings rather than
silent success. The `limits:` config section exposes the five budgets with safe defaults.
Measured outcome on Stripe spec3.yaml with DEFAULT limits: validate 3.8s, self-compare
7.0s / 703 MB peak RSS, 0 findings, fully deterministic.

## 28. Content-parameter limitation closed

DECISIONS 27 recorded "first media type only" as a known limitation. Closed: content maps
now hold every media type. Legacy routing is preserved exactly — single-media params and
the primary key keep their existing CG20xx schema events — while additional keys gain:
add/remove events (`parameter.content.added` / `.removed`, CG2017 Info NonBreaking /
CG2018 High Breaking, mirroring body content-type semantics) and full per-key schema diffs
through the standard comparer.

## 29. External references: URL siblings, root-tree containment, graceful degradation

Three related fixes, all triggered by the DigitalOcean public API specification — a
multi-file spec (main + `resources/**` + `shared/`, ~2851 YAML files) that exercised
resolution paths no prior real-world validation had reached:

1. **URL documents resolve sibling-file references.** A document loaded from http(s) now
   resolves relative `$ref`s (e.g. `description.yml#/introduction`) against its own URL per
   RFC 3986. Every fetched sibling passes the identical policy gates as any other remote
   document: scheme gate, host allowlist, SSRF DNS guard, redirect revalidation, size and
   timeout caps. stdin/git sources keep their actionable "no file location" error.
2. **Containment is anchored to the ROOT document's directory**, not each containing
   sub-document. The previous per-document anchor falsely rejected legitimate layouts where
   `resources/<cat>/x.yml` references `../../shared/…` — inside the root tree, above the
   containing file's own directory. Escaping the root tree remains a hard error.
3. **Data-condition failures degrade to markers.** A referenced external file that is
   missing on disk, 404s, or is refused by fetch policy now leaves an
   `x-sentinel-unresolved-ref` marker (mirroring decision 27) instead of aborting the run:
   identical markers compare silently; differing pairs emit CG5022 Verify findings;
   `validate` reports the count as a warning line and still exits 0. Usage errors stay
   hard failures: no-base sources (stdin/git), root-tree escapes, unsafe URI schemes.

Measured outcome (DigitalOcean public v2, defaults): validate 1.5 s / 133 MB RSS with 0
unresolved refs across all ~2851 files (pre-fix: exit 2); self-compare passed, 0 findings,
3.4 s total.

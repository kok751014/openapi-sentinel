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

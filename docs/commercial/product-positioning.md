# Product positioning

> **Stop accidental API breaking changes before they reach production.**

## The problem

Modern teams evolve API contracts continuously. Nothing in a typical CI pipeline notices when
a contract edit silently invalidates the promise made to consumers — until a client team
reports 400s in production. JSON diffs don't understand `required`, direction, enums, or
security semantics. Governance reviews happen after the damage.

## Positioning statement

For engineering teams that ship APIs iteratively, **OpenAPI Sentinel** is a contract
compatibility gate that understands OpenAPI semantics — unlike generic diff tools — because it
classifies every change with a direction-aware breaking-change model, explains the impact in
plain language, and enforces your policy directly in CI.

## Differentiators (all verifiable in-repo)

- **OpenAPI 2.0 → 3.2** support including 3.1/3.2 JSON-Schema dialect handling.
- **Direction-aware analysis**: request vs response compatibility treated as distinct promises.
- **80 rules + 3 engine self-checks** (`CGxxxx`) with human explanations, impact and remediation text.
- **Offline & private by default**: no telemetry, no uploads; remote fetch is opt-in and
  SSRF-guarded.
- **CI-native**: documented exit codes, SARIF/JUnit, GitHub annotations, baselines,
  expiring suppressions with reasons.
- **Professional single-file HTML report**: filters, search, collapsible detail, zero CDN
  dependencies — sendable to customers or managers.
- **Deterministic output**: identical inputs → identical report bytes; diff-friendly.

## Proof points

- Sample scenario: Order Management API v1 → v2-breaking yields 33 precise findings incl.
  endpoint removal, required-parameter introduction, enum contraction, type change flowing
  through `$ref` graph, OAuth tightening — each with explanation and remediation.
- Test suite: 100+ tests including false-positive/false-negative fixture suites.

## Anti-positioning

- Not an API gateway, catalog, or design-time editor.
- Not an AI summarizer: deterministic rules you can audit line by line.
- Does not replace contract testing; complements it at the contract boundary.

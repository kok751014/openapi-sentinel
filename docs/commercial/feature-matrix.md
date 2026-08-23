# Feature matrix — Community vs Pro

Core correctness is never paywalled: detecting breaking changes accurately is the free
product's job. Pro adds governance, reporting depth and enterprise workflow.

| Capability | Community | Pro |
|------------|:---------:|:---:|
| OpenAPI 2.0/3.0/3.1/3.2, JSON + YAML | ✅ | ✅ |
| Full rule catalog & stable rule IDs | ✅ | ✅ |
| Direction-aware breaking-change detection | ✅ | ✅ |
| Console report, exit codes | ✅ | ✅ |
| JSON machine-readable report (versioned schema) | ✅ | ✅ |
| Configuration file, rule overrides, ignore paths | ✅ | ✅ |
| Suppressions (with reasons/expiry) & baselines | ✅ | ✅ |
| Semantic version recommendation | ✅ | ✅ |
| Markdown report + PR summary | ✅ | ✅ |
| **Professional HTML report** (filters/search/offline) | summary page | ✅ full interactive |
| **SARIF** code-scanning integration | — | ✅ |
| **JUnit XML** for CI test UIs | — | ✅ |
| **Changelog generation** (API-focused, Breaking sections) | — | ✅ |
| Custom rule packs / organization policy profiles | — | ✅ |
| Report branding (logo, colors) for customer-facing reviews | — | ✅ |
| Priority support & SLA | community | ✅ |

## Rationale

- Free tier must be genuinely useful alone: engine + console + JSON covers solo developers.
- Paid value concentrates where organizations feel pain: stakeholder-grade reporting,
  security-tooling integration, custom governance rules.
- No feature exists "to be removed": Pro capabilities are additive modules behind clean
  interfaces (`IResultReporter`, rule registry), not crippled core paths.

## Licensing architecture note

Entitlement stays behind an interface; the Community build ships with an open implementation.
No DRM, no phone-home. See DECISIONS.md.

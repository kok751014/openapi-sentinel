# Feature matrix — Community (open source) vs Pro (planned)

Core correctness is never paywalled: detecting breaking changes accurately is the open-source
product's job. **Community** is this repository, licensed under the
[Apache License 2.0](../../LICENSE). **Pro** is a possible future offering; it does not exist
today, and nothing in the Pro-only rows below is implemented.

| Capability | Community | Pro (planned) |
|------------|:---------:|:-------------:|
| OpenAPI 2.0/3.0/3.1/3.2, JSON + YAML | ✅ | ✅ |
| Full rule catalog & stable rule IDs | ✅ | ✅ |
| Direction-aware breaking-change detection | ✅ | ✅ |
| Console report, exit codes | ✅ | ✅ |
| JSON machine-readable report (versioned schema) | ✅ | ✅ |
| Configuration file, rule overrides, ignore paths | ✅ | ✅ |
| Suppressions (with reasons/expiry) & baselines | ✅ | ✅ |
| Semantic version recommendation | ✅ | ✅ |
| Markdown report + PR summary | ✅ | ✅ |
| Single-file HTML report (filters/search/offline) | ✅ | ✅ |
| SARIF code-scanning integration | ✅ | ✅ |
| JUnit XML for CI test UIs | ✅ | ✅ |
| Changelog generation (API-focused, Breaking sections) | ✅ | ✅ |
| Bruno collection sync | ✅ | ✅ |
| Custom rule packs / organization policy profiles | — | planned |
| Report branding (logo, colors) for customer-facing reviews | — | planned |
| Priority support & SLA | community (GitHub issues) | planned |

## Rationale

- The open-source edition must be genuinely complete on its own: engine, rules and every
  report format already shipped stay in Community.
- Paid value, if offered, would concentrate on organization-specific governance and service:
  custom policy packs, branding, support commitments.
- No feature exists "to be removed": Pro capabilities would be additive modules behind clean
  interfaces (`IResultReporter`, rule registry), not crippled core paths.

## Licensing note

This repository is licensed under Apache-2.0, apart from the third-party files listed in
THIRD-PARTY-NOTICES.md, which keep their own licenses. It contains no entitlement checks, license
keys, DRM or phone-home code. A Pro edition, if built, would ship as separate additive modules
and would not change the license of code already released here. See DECISIONS.md (entry 33).

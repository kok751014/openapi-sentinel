# Pricing strategy (input draft)

Not implemented in product logic — commercial input only, kept out of application code.
No Pro edition is available today. This document does not affect the
[Apache License 2.0](../../LICENSE) that covers this repository; see the
[feature matrix](feature-matrix.md) for the Community/Pro boundary.

## Target buyers

1. **Solo/small OSS-minded backend developers** — price-sensitive; Community edition converts them into advocates.
2. **Product engineering teams (5–50 devs)** shipping multiple APIs — feel breakage pain weekly; buy time savings.
3. **Enterprises with API governance mandates** — need reporting, audit trail, custom rules, support.

## Launch pricing options considered

| Tier | Price point | Contents |
|------|-------------|----------|
| Personal Lifetime | $49 one-time | Pro features for one developer, perpetual |
| Team Lifetime | $299/seat one-time | Pro features, per-seat perpetual |
| Team Annual | $12/dev/month (min 10 seats) | Pro features + updates during term |
| Enterprise Annual | custom | custom rules packs, branding, support SLA |

Reasoning:

- A single prevented production incident typically costs more than a year of Team Annual;
  anchor value against incident cost, not tool features.
- Lifetime tiers exist because devtools buyers distrust subscriptions early; annual wins later
  via predictable revenue — offer both at launch and watch mix.
- Enterprise willingness-to-pay concentrates in: HTML report branding for customer-facing
  reviews, custom org policy packs, and support SLA — all cheap for us to honor, valuable for
  compliance-driven buyers. SARIF/code-scanning integration is part of the open-source edition.

## Willingness-to-pay drivers

1. CI gate reliability (false positives kill adoption; correctness is the moat).
2. Stakeholder-communicable reports (the artifact a manager forwards).
3. Audit evidence (suppression reasons + expiry = governance story).

## Explicit non-goals

- Usage-based/telemetry-backed licensing (contradicts privacy posture).
- Per-CI-run pricing (punishes exactly the behaviour we want).

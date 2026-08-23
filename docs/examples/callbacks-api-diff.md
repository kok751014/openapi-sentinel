# API Compatibility Report

**Result:** ❌ FAILED — `samples/callbacks/v1.yaml` → `samples/callbacks/v2-breaking.yaml`  
**Generated:** 2026-08-23 13:54:07 UTC by OpenAPI Sentinel 1.0.0  
**Spec versions:** 3.0.3 → 3.0.3

## Summary

| Metric | Count |
|--------|------:|
| Total findings | 5 |
| Breaking | 3 |
| Potentially breaking | 1 |
| Non-breaking | 0 |
| Informational | 1 |

**Severity:** critical 1 · high 2 · medium 1 · low 0 · info 1

**Recommended version bump: `MAJOR`** — 3 breaking finding(s) at high or critical severity (first: CG1013) require a major version.

### Affected endpoints

| State | Endpoint | Findings |
|-------|----------|---------:|
| Changed | `POST /subscriptions` | 3 |
| Changed | `POST <callback:POST /subscriptions:orderEvent>` | 1 |

## Findings

### CRITICAL

#### [CG5001] Schema type changed — `POST /subscriptions → POST /subscriptions responses.201.headers.X-Request-Id`

Type changed at POST /subscriptions: '"integer"' → '"string"'.

- **Compatibility:** Breaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"integer"` → `"string"`

### HIGH

#### [CG1013] Callback target expression changed — `POST /subscriptions → POST /subscriptions callbacks.orderEvent`

The delivery target of callback 'orderEvent' changed: '{$request.body#/callbackUrl}' → '{$request.body#/notificationUrl}'.

- **Compatibility:** Breaking · **Confidence:** Certain · **Direction:** Neutral
- **Change:** `{$request.body#/callbackUrl}` → `{$request.body#/notificationUrl}`

#### [CG5015] Required property added to request — `POST <callback:POST /subscriptions:orderEvent> → body.signature`

Property 'signature' was added as REQUIRED to the request schema of POST <callback:POST /subscriptions:orderEvent>.

- **Compatibility:** Breaking · **Confidence:** Certain · **Direction:** Request
- **Change:** `false` → `true`

### MEDIUM

#### [CG1011] Callback added — `POST /subscriptions → POST /subscriptions callbacks.shipmentEvent`

A callback 'shipmentEvent' was added to POST /subscriptions; the server will invoke client-provided URLs.

- **Compatibility:** PotentiallyBreaking · **Confidence:** Certain · **Direction:** Neutral
- **Change:** `(none)` → `shipmentEvent`

### INFO

#### [CG7001] API version changed — `(document)`

Document version changed: '1.0.0' → '2.0.0'.

- **Compatibility:** Informational · **Confidence:** Certain · **Direction:** Neutral
- **Change:** `1.0.0` → `2.0.0`


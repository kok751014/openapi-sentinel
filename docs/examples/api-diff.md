# API Compatibility Report

**Result:** ❌ FAILED — `samples/basic/v1.yaml` → `samples/basic/v2-breaking.yaml`  
**Generated:** 2026-08-23 10:01:33 UTC by OpenAPI Sentinel 1.0.0  
**Spec versions:** 3.0.3 → 3.0.3

## Summary

| Metric | Count |
|--------|------:|
| Total findings | 33 |
| Breaking | 7 |
| Potentially breaking | 9 |
| Non-breaking | 15 |
| Informational | 2 |

**Severity:** critical 1 · high 6 · medium 9 · low 10 · info 7

**Recommended version bump: `MAJOR`** — 7 breaking finding(s) at high or critical severity (first: CG2002) require a major version.

### Affected endpoints

| State | Endpoint | Findings |
|-------|----------|---------:|
| Changed | `GET /orders` | 11 |
| Changed | `POST /orders` | 8 |
| Changed | `GET /orders/{orderId}` | 6 |
| Removed | `DELETE /orders/{orderId}` | 1 |
| Changed | `GET /products` | 4 |

## Findings

### CRITICAL

#### [CG1002] Operation removed — `DELETE /orders/{orderId}`

Operation DELETE /orders/{orderId} was removed.

- **Compatibility:** Breaking · **Confidence:** Certain · **Direction:** Neutral

### HIGH

#### [CG2002] Required parameter added — `GET /orders → GET /orders parameters.channel`

Parameter 'channel' was added to GET /orders as required.

- **Compatibility:** Breaking · **Confidence:** Certain · **Direction:** Request

#### [CG2011] Parameter constraint changed — `GET /orders → GET /orders parameters.pageSize`

Constraint 'maximum' of parameter 'pageSize' changed: '100' → '50'.

- **Compatibility:** Breaking · **Confidence:** Certain · **Direction:** Request
- **Change:** `100` → `50`

#### [CG2007] Parameter enum value removed — `GET /orders → GET /orders parameters.status`

Enum value "delivered" was removed from parameter 'status'.

- **Compatibility:** Breaking · **Confidence:** Certain · **Direction:** Request
- **Change:** `"delivered"` → `(removed)`

#### [CG6001] Endpoint now requires authentication — `POST /orders → POST /orders security`

Authentication became mandatory for POST /orders (oauth2).

- **Compatibility:** Breaking · **Confidence:** Certain · **Direction:** Neutral
- **Change:** `(none)` → `oauth2`

#### [CG5006] Validation constraint changed — `POST /orders → body.items`

Constraint 'maxItems' changed at POST /orders: '100' → '20'.

- **Compatibility:** Breaking · **Confidence:** Certain · **Direction:** Request
- **Change:** `100` → `20`

#### [CG2011] Parameter constraint changed — `GET /products → GET /products parameters.pageSize`

Constraint 'maximum' of parameter 'pageSize' changed: '100' → '50'.

- **Compatibility:** Breaking · **Confidence:** Certain · **Direction:** Request
- **Change:** `100` → `50`

### MEDIUM

#### [CG1010] Servers changed — ``

Advertised servers changed: 'https://api.example.com/v1' → 'https://api.example.com/v2'.

- **Compatibility:** PotentiallyBreaking · **Confidence:** Certain · **Direction:** Neutral
- **Change:** `https://api.example.com/v1` → `https://api.example.com/v2`

#### [CG5002] Schema format changed — `GET /orders → GET /orders responses.200.[].items.[].unitPrice.amount`

Format changed at GET /orders: 'double' → '(removed)'.

- **Compatibility:** PotentiallyBreaking · **Confidence:** Likely · **Direction:** Response
- **Change:** `double` → `(removed)`

#### [CG5002] Schema format changed — `GET /orders → GET /orders responses.200.[].total.amount`

Format changed at GET /orders: 'double' → '(removed)'.

- **Compatibility:** PotentiallyBreaking · **Confidence:** Likely · **Direction:** Response
- **Change:** `double` → `(removed)`

#### [CG4005] Response header removed — `GET /orders → GET /orders responses.200.headers.X-Total-Count`

Response header 'X-Total-Count' was removed from GET /orders.

- **Compatibility:** PotentiallyBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `X-Total-Count` → `(removed)`

#### [CG5002] Schema format changed — `POST /orders → POST /orders responses.201.items.[].unitPrice.amount`

Format changed at POST /orders: 'double' → '(removed)'.

- **Compatibility:** PotentiallyBreaking · **Confidence:** Likely · **Direction:** Response
- **Change:** `double` → `(removed)`

#### [CG5002] Schema format changed — `POST /orders → POST /orders responses.201.total.amount`

Format changed at POST /orders: 'double' → '(removed)'.

- **Compatibility:** PotentiallyBreaking · **Confidence:** Likely · **Direction:** Response
- **Change:** `double` → `(removed)`

#### [CG5002] Schema format changed — `GET /orders/{orderId} → GET /orders/{orderId} responses.200.items.[].unitPrice.amount`

Format changed at GET /orders/{orderId}: 'double' → '(removed)'.

- **Compatibility:** PotentiallyBreaking · **Confidence:** Likely · **Direction:** Response
- **Change:** `double` → `(removed)`

#### [CG5002] Schema format changed — `GET /orders/{orderId} → GET /orders/{orderId} responses.200.total.amount`

Format changed at GET /orders/{orderId}: 'double' → '(removed)'.

- **Compatibility:** PotentiallyBreaking · **Confidence:** Likely · **Direction:** Response
- **Change:** `double` → `(removed)`

#### [CG5002] Schema format changed — `GET /products → GET /products responses.200.[].price.amount`

Format changed at GET /products: 'double' → '(removed)'.

- **Compatibility:** PotentiallyBreaking · **Confidence:** Likely · **Direction:** Response
- **Change:** `double` → `(removed)`

### LOW

#### [CG5001] Schema type changed — `GET /orders → GET /orders responses.200.[].items.[].unitPrice.amount`

Type changed at GET /orders: '"number"' → '"integer"'.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"number"` → `"integer"`

#### [CG5003] Response enum value removed — `GET /orders → GET /orders responses.200.[].status`

Enum value "delivered" removed at GET /orders.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"delivered"` → `(removed)`

#### [CG5001] Schema type changed — `GET /orders → GET /orders responses.200.[].total.amount`

Type changed at GET /orders: '"number"' → '"integer"'.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"number"` → `"integer"`

#### [CG5001] Schema type changed — `POST /orders → POST /orders responses.201.items.[].unitPrice.amount`

Type changed at POST /orders: '"number"' → '"integer"'.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"number"` → `"integer"`

#### [CG5003] Response enum value removed — `POST /orders → POST /orders responses.201.status`

Enum value "delivered" removed at POST /orders.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"delivered"` → `(removed)`

#### [CG5001] Schema type changed — `POST /orders → POST /orders responses.201.total.amount`

Type changed at POST /orders: '"number"' → '"integer"'.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"number"` → `"integer"`

#### [CG5001] Schema type changed — `GET /orders/{orderId} → GET /orders/{orderId} responses.200.items.[].unitPrice.amount`

Type changed at GET /orders/{orderId}: '"number"' → '"integer"'.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"number"` → `"integer"`

#### [CG5003] Response enum value removed — `GET /orders/{orderId} → GET /orders/{orderId} responses.200.status`

Enum value "delivered" removed at GET /orders/{orderId}.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"delivered"` → `(removed)`

#### [CG5001] Schema type changed — `GET /orders/{orderId} → GET /orders/{orderId} responses.200.total.amount`

Type changed at GET /orders/{orderId}: '"number"' → '"integer"'.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"number"` → `"integer"`

#### [CG5001] Schema type changed — `GET /products → GET /products responses.200.[].price.amount`

Type changed at GET /products: '"number"' → '"integer"'.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `"number"` → `"integer"`

### INFO

#### [CG7001] API version changed — ``

Document version changed: '1.0.0' → '2.0.0'.

- **Compatibility:** Informational · **Confidence:** Certain · **Direction:** Neutral
- **Change:** `1.0.0` → `2.0.0`

#### [CG6006] Security scheme added — ` → securitySchemes.oauth2`

New security scheme 'oauth2' was defined.

- **Compatibility:** Informational · **Confidence:** Certain · **Direction:** Neutral
- **Change:** `(none)` → `oauth2`

#### [CG5008] Array item schema changed — `GET /orders → GET /orders responses.200`

Array items changed at GET /orders; see the detailed element-level findings for specifics.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `{"type":"object","required":["id","customerId","status","items","total"],"properties":{"id":{"type":"string","format":"uuid"},"customerId":{"type":"string","format":"uuid"},"status":{"type":"string","enum":["pending","paid","shipped","delivered","cancelled"]},"items":{"type":"array","minItems":1,"items":{"type":"object","required":["productId","quantity"],"properties":{"productId":{"type":"string","format":"uuid"},"quantity":{"type":"integer","minimum":1},"unitPrice":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"number","format":"double","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}}}}},"total":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"number","format":"double","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}},"createdAt":{"type":"string","format":"date-time","readOnly":true}}}` → `{"type":"object","required":["id","customerId","status","items","total"],"properties":{"id":{"type":"string","format":"uuid"},"customerId":{"type":"string","format":"uuid"},"status":{"type":"string","description":"Lifecycle status of an order","enum":["pending","paid","shipped","cancelled"]},"items":{"type":"array","minItems":1,"items":{"type":"object","required":["productId","quantity"],"properties":{"productId":{"type":"string","format":"uuid"},"quantity":{"type":"integer","minimum":1},"unitPrice":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"integer","description":"Whole currency units only; decimal amounts no longer accepted","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}}}}},"total":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"integer","description":"Whole currency units only; decimal amounts no longer accepted","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}},"createdAt":{"type":"string","format":"date-time","readOnly":true}}}`

#### [CG5008] Array item schema changed — `GET /orders → GET /orders responses.200.[].items`

Array items changed at GET /orders; see the detailed element-level findings for specifics.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `{"type":"object","required":["productId","quantity"],"properties":{"productId":{"type":"string","format":"uuid"},"quantity":{"type":"integer","minimum":1},"unitPrice":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"number","format":"double","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}}}}` → `{"type":"object","required":["productId","quantity"],"properties":{"productId":{"type":"string","format":"uuid"},"quantity":{"type":"integer","minimum":1},"unitPrice":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"integer","description":"Whole currency units only; decimal amounts no longer accepted","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}}}}`

#### [CG5008] Array item schema changed — `POST /orders → POST /orders responses.201.items`

Array items changed at POST /orders; see the detailed element-level findings for specifics.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `{"type":"object","required":["productId","quantity"],"properties":{"productId":{"type":"string","format":"uuid"},"quantity":{"type":"integer","minimum":1},"unitPrice":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"number","format":"double","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}}}}` → `{"type":"object","required":["productId","quantity"],"properties":{"productId":{"type":"string","format":"uuid"},"quantity":{"type":"integer","minimum":1},"unitPrice":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"integer","description":"Whole currency units only; decimal amounts no longer accepted","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}}}}`

#### [CG5008] Array item schema changed — `GET /orders/{orderId} → GET /orders/{orderId} responses.200.items`

Array items changed at GET /orders/{orderId}; see the detailed element-level findings for specifics.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `{"type":"object","required":["productId","quantity"],"properties":{"productId":{"type":"string","format":"uuid"},"quantity":{"type":"integer","minimum":1},"unitPrice":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"number","format":"double","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}}}}` → `{"type":"object","required":["productId","quantity"],"properties":{"productId":{"type":"string","format":"uuid"},"quantity":{"type":"integer","minimum":1},"unitPrice":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"integer","description":"Whole currency units only; decimal amounts no longer accepted","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}}}}`

#### [CG5008] Array item schema changed — `GET /products → GET /products responses.200`

Array items changed at GET /products; see the detailed element-level findings for specifics.

- **Compatibility:** NonBreaking · **Confidence:** Certain · **Direction:** Response
- **Change:** `{"type":"object","required":["id","name","price"],"properties":{"id":{"type":"string","format":"uuid"},"name":{"type":"string","minLength":1,"maxLength":150},"price":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"number","format":"double","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}}}}` → `{"type":"object","required":["id","name","price"],"properties":{"id":{"type":"string","format":"uuid"},"name":{"type":"string","minLength":1,"maxLength":150},"price":{"type":"object","required":["amount","currency"],"properties":{"amount":{"type":"integer","description":"Whole currency units only; decimal amounts no longer accepted","minimum":0},"currency":{"type":"string","pattern":"^[A-Z]{3}$"}}}}}`


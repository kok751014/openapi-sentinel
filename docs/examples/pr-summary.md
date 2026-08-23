## ❌ OpenAPI Sentinel: policy failed

`samples/basic/v1.yaml` → `samples/basic/v2-breaking.yaml` — **33 finding(s)** (7 breaking, 9 potentially breaking)

Suggested version bump: **MAJOR**

<details>
<summary>Top breaking/potentially-breaking findings</summary>

- **[CG1010] Servers changed** — ``: Advertised servers changed: 'https://api.example.com/v1' → 'https://api.example.com/v2'.
- **[CG2002] Required parameter added** — `GET /orders → GET /orders parameters.channel`: Parameter 'channel' was added to GET /orders as required.
- **[CG2011] Parameter constraint changed** — `GET /orders → GET /orders parameters.pageSize`: Constraint 'maximum' of parameter 'pageSize' changed: '100' → '50'.
- **[CG2007] Parameter enum value removed** — `GET /orders → GET /orders parameters.status`: Enum value "delivered" was removed from parameter 'status'.
- **[CG5002] Schema format changed** — `GET /orders → GET /orders responses.200.[].items.[].unitPrice.amount`: Format changed at GET /orders: 'double' → '(removed)'.
- **[CG5002] Schema format changed** — `GET /orders → GET /orders responses.200.[].total.amount`: Format changed at GET /orders: 'double' → '(removed)'.
- **[CG4005] Response header removed** — `GET /orders → GET /orders responses.200.headers.X-Total-Count`: Response header 'X-Total-Count' was removed from GET /orders.
- **[CG5002] Schema format changed** — `POST /orders → POST /orders responses.201.items.[].unitPrice.amount`: Format changed at POST /orders: 'double' → '(removed)'.
- **[CG5002] Schema format changed** — `POST /orders → POST /orders responses.201.total.amount`: Format changed at POST /orders: 'double' → '(removed)'.
- **[CG6001] Endpoint now requires authentication** — `POST /orders → POST /orders security`: Authentication became mandatory for POST /orders (oauth2).

</details>

## 1.0.0 → 2.0.0

### 💥 Breaking and potentially breaking

- **Servers changed** (`CG1010`) at `` — Advertised servers changed: 'https://api.example.com/v1' → 'https://api.example.com/v2'.
- **Required parameter added** (`CG2002`) at `GET /orders → GET /orders parameters.channel` — Parameter 'channel' was added to GET /orders as required.
- **Parameter constraint changed** (`CG2011`) at `GET /orders → GET /orders parameters.pageSize` — Constraint 'maximum' of parameter 'pageSize' changed: '100' → '50'.
- **Parameter enum value removed** (`CG2007`) at `GET /orders → GET /orders parameters.status` — Enum value "delivered" was removed from parameter 'status'.
- **Schema format changed** (`CG5002`) at `GET /orders → GET /orders responses.200.[].items.[].unitPrice.amount` — Format changed at GET /orders: 'double' → '(removed)'.
- **Schema format changed** (`CG5002`) at `GET /orders → GET /orders responses.200.[].total.amount` — Format changed at GET /orders: 'double' → '(removed)'.
- **Response header removed** (`CG4005`) at `GET /orders → GET /orders responses.200.headers.X-Total-Count` — Response header 'X-Total-Count' was removed from GET /orders.
- **Schema format changed** (`CG5002`) at `POST /orders → POST /orders responses.201.items.[].unitPrice.amount` — Format changed at POST /orders: 'double' → '(removed)'.
- **Schema format changed** (`CG5002`) at `POST /orders → POST /orders responses.201.total.amount` — Format changed at POST /orders: 'double' → '(removed)'.
- **Endpoint now requires authentication** (`CG6001`) at `POST /orders → POST /orders security` — Authentication became mandatory for POST /orders (oauth2).
- **Validation constraint changed** (`CG5006`) at `POST /orders → body.items` — Constraint 'maxItems' changed at POST /orders: '100' → '20'.
- **Schema format changed** (`CG5002`) at `GET /orders/{orderId} → GET /orders/{orderId} responses.200.items.[].unitPrice.amount` — Format changed at GET /orders/{orderId}: 'double' → '(removed)'.
- **Schema format changed** (`CG5002`) at `GET /orders/{orderId} → GET /orders/{orderId} responses.200.total.amount` — Format changed at GET /orders/{orderId}: 'double' → '(removed)'.
- **Operation removed** (`CG1002`) at `DELETE /orders/{orderId}` — Operation DELETE /orders/{orderId} was removed.
- **Parameter constraint changed** (`CG2011`) at `GET /products → GET /products parameters.pageSize` — Constraint 'maximum' of parameter 'pageSize' changed: '100' → '50'.
- **Schema format changed** (`CG5002`) at `GET /products → GET /products responses.200.[].price.amount` — Format changed at GET /products: 'double' → '(removed)'.

### ✨ Non-breaking

- **API version changed** (`CG7001`) at `` — Document version changed: '1.0.0' → '2.0.0'.
- **Security scheme added** (`CG6006`) at ` → securitySchemes.oauth2` — New security scheme 'oauth2' was defined.
- **Array item schema changed** (`CG5008`) at `GET /orders → GET /orders responses.200` — Array items changed at GET /orders; see the detailed element-level findings for specifics.
- **Array item schema changed** (`CG5008`) at `GET /orders → GET /orders responses.200.[].items` — Array items changed at GET /orders; see the detailed element-level findings for specifics.
- **Schema type changed** (`CG5001`) at `GET /orders → GET /orders responses.200.[].items.[].unitPrice.amount` — Type changed at GET /orders: '"number"' → '"integer"'.
- **Response enum value removed** (`CG5003`) at `GET /orders → GET /orders responses.200.[].status` — Enum value "delivered" removed at GET /orders.
- **Schema type changed** (`CG5001`) at `GET /orders → GET /orders responses.200.[].total.amount` — Type changed at GET /orders: '"number"' → '"integer"'.
- **Array item schema changed** (`CG5008`) at `POST /orders → POST /orders responses.201.items` — Array items changed at POST /orders; see the detailed element-level findings for specifics.
- **Schema type changed** (`CG5001`) at `POST /orders → POST /orders responses.201.items.[].unitPrice.amount` — Type changed at POST /orders: '"number"' → '"integer"'.
- **Response enum value removed** (`CG5003`) at `POST /orders → POST /orders responses.201.status` — Enum value "delivered" removed at POST /orders.
- **Schema type changed** (`CG5001`) at `POST /orders → POST /orders responses.201.total.amount` — Type changed at POST /orders: '"number"' → '"integer"'.
- **Array item schema changed** (`CG5008`) at `GET /orders/{orderId} → GET /orders/{orderId} responses.200.items` — Array items changed at GET /orders/{orderId}; see the detailed element-level findings for specifics.
- **Schema type changed** (`CG5001`) at `GET /orders/{orderId} → GET /orders/{orderId} responses.200.items.[].unitPrice.amount` — Type changed at GET /orders/{orderId}: '"number"' → '"integer"'.
- **Response enum value removed** (`CG5003`) at `GET /orders/{orderId} → GET /orders/{orderId} responses.200.status` — Enum value "delivered" removed at GET /orders/{orderId}.
- **Schema type changed** (`CG5001`) at `GET /orders/{orderId} → GET /orders/{orderId} responses.200.total.amount` — Type changed at GET /orders/{orderId}: '"number"' → '"integer"'.
- **Array item schema changed** (`CG5008`) at `GET /products → GET /products responses.200` — Array items changed at GET /products; see the detailed element-level findings for specifics.
- **Schema type changed** (`CG5001`) at `GET /products → GET /products responses.200.[].price.amount` — Type changed at GET /products: '"number"' → '"integer"'.

**Recommended version bump: MAJOR.** 7 breaking finding(s) at high or critical severity (first: CG2002) require a major version.


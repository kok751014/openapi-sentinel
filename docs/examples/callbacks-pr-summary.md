## ❌ OpenAPI Sentinel: policy failed

`samples/callbacks/v1.yaml` → `samples/callbacks/v2-breaking.yaml` — **5 finding(s)** (3 breaking, 1 potentially breaking)

Suggested version bump: **MAJOR**

<details>
<summary>Top breaking/potentially-breaking findings</summary>

- **[CG1013] Callback target expression changed** — `POST /subscriptions → callbacks.orderEvent`: The delivery target of callback 'orderEvent' changed: '{$request.body#/callbackUrl}' → '{$request.body#/notificationUrl}'.
- **[CG1011] Callback added** — `POST /subscriptions → callbacks.shipmentEvent`: A callback 'shipmentEvent' was added to POST /subscriptions; the server will invoke client-provided URLs.
- **[CG5001] Schema type changed** — `POST /subscriptions → responses.201.headers.X-Request-Id`: Type changed at POST /subscriptions: '"integer"' → '"string"'.
- **[CG5015] Required property added to request** — `POST <callback:POST /subscriptions:orderEvent> → body.signature`: Property 'signature' was added as REQUIRED to the request schema of POST \<callback:POST /subscriptions:orderEvent>.

</details>

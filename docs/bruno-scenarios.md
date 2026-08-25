# Bruno 測試情境產生器（Scenario Generator）

`openapi-sentinel bruno sync` / `bruno examples` 會為每個 operation 自動產生
「可直接在 Bruno 點擊 Send」的測試範例。情境分兩層：

## Layer 1 — 由 OpenAPI 規格推導（自動）

完全 deterministic：同一份規格 + 同一版 Sentinel 永遠得到同一組情境。
僅使用規格中可推導的資訊：required、型別、enum、minimum/maximum（含 exclusive）、
minLength/maxLength、pattern、minItems/maxItems、format、default、example(s)。

預設情境類別與排序（`maxPerOperation` 由高至低截斷）：

| 排序 | 類別 | 名稱範例 |
|------|------|----------|
| 1 | 成功－完整參數 | `成功－完整參數` |
| 2 | 成功－僅輸入必填參數（存在選填欄位時才產生） | `成功－僅輸入必填參數` |
| 3+ | 驗證失敗 | `失敗－缺少 customerId`、`失敗－orderType 使用無效列舉值`、`失敗－quantity 超過允許最大值`、`失敗－name 超過最大長度`、`失敗－email 格式不正確`、`失敗－tags 型別錯誤`… |
| 後段 | 邊界值 | `邊界值－quantity 使用最小允許值`、`邊界值－tags 使用最大項目數`… |

- 名稱與描述預設 **繁體中文（zh-TW）**；描述說明「用於確認後端的驗證行為」，
  不臆測業務回應。
- pattern 情境只有在能**證明**某候選字串不符合該 regex 時才會產生。
- 巢狀 body 屬性以點路徑描述（例如 `address.zip`），名稱取葉節欄位。
- path 參數不產生「缺少」情境（URL 結構需要它），但 enum/min/max 仍可測。

### maxPerOperation

`bruno.examples.maxPerOperation`（1–64，預設 8）是硬上限。候選先依
「類別優先序 → 欄位宣告順序」穩定排序後截斷；跨次執行結果完全一致。

## Layer 2 — 商業邏輯（`openapi-sentinel.scenarios.yml`）

OpenAPI 表達不了的商業情境必須**明確**寫在 scenarios 檔，generator 不自行幻想：

```yaml
scenarios:
  - operationId: createOrder
    name: "成功－一般訂單"
    description: "使用有效客戶建立訂單。"
    request:
      body:
        customerId: 10001
    expect:
      status: 200
      description: "訂單建立成功"
```

載入時驗證（錯誤 → exit 3，警告不阻擋）：

| 檢查 | 結果 |
|------|------|
| 缺少 `operationId` / `name` | 錯誤 |
| `operationId` 不存在於規格 | 錯誤 |
| 同 operation 內重複名稱 | 錯誤 |
| `expect.status` 非 100–599 整數 | 錯誤 |
| body 欄位不在規格宣告屬性內 | 警告（原樣送出） |

啟用方式：

```yaml
bruno:
  examples:
    enabled: true
    scenariosPath: ./api-contract/scenarios/openapi-sentinel.scenarios.yml
```

Layer 2 情境附加在 Layer 1 之後，不受 `maxPerOperation` 截斷；
從檔案移除的情境會在下次同步時自動清理。

## 只重跑範例

```bash
openapi-sentinel bruno examples --operation createOrder   # 單一操作
openapi-sentinel bruno examples --path /users             # 路徑前綴
```

過濾模式下**不做**失效/刪除判定（部分檢視無法判斷刪除）。

## Authentication 行為

`ignoreAuthentication: true`（預設）表示：

- 不自動產生 JWT 缺失／過期、401/403 等情境
- 不改動既有 Bruno auth / environment / token scripts
- 不把任何 bearer token 或 secret 寫進產出檔或報告

若操作依合約要求認證，僅在 `x-sentinel.requiresAuth` 記錄並在 docs 加註，
由 Bruno 原本的 collection/environment/auth 流程處理。

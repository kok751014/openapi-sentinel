# Bruno 同步（Bruno Sync）

OpenAPI Sentinel 可以把 OpenAPI / Swagger contract 自動同步成 **Bruno collection**，
並在合約變動時做**增量、保留人工內容**的更新，同時產出繁體中文的 HTML / JSON 同步報告。

```text
OpenAPI / Swagger
        ↓
openapi-sentinel bruno sync
        ↓
官方 OpenCollection v1.0 schema 驗證（未通過絕不寫檔）
        ↓
同步 Bruno Collection（單檔）+ Bruno Workspace（資料夾式）
        ↓
bruno-sync-report-yyyymmdd-HHmmss.html / .json（成對歷程）
```

## 一鍵開始（Auto Bootstrap）

第一次使用**不需要**手動建立任何檔案或資料夾：

```bash
# 本機 OpenAPI / Swagger
openapi-sentinel bruno sync --source ./swagger.json

# 公開 HTTPS URL：明確同意遠端讀取，權限會寫入新設定檔
openapi-sentinel bruno sync --source "https://api.example.com/swagger.json" --allow-remote

# localhost／私有網路 HTTPS URL：兩個安全 opt-in 都必須明確給出
openapi-sentinel bruno sync --source "https://localhost:7099/swagger/backend/swagger.json" --allow-remote --allow-private-network
```

Sentinel 會自動完成：

```text
[建立] openapi-sentinel.yml                       # production-ready 預設設定（已存在時永不覆蓋）
[建立] api-contract/openapi/                      # 遠端來源另存原始入口文件快照 source-snapshot.json
[建立] api-contract/bruno/collection.yml          # 單檔 OpenCollection（可交換／備份）
[建立] api-contract/bruno-workspace/               # Bruno 原生 bruno.json + .bru workspace
[建立] api-contract/reports/bruno-sync-report-yyyymmdd-HHmmss.html/.json
```

之後在 **Bruno → Collection → Open Collection** 直接選擇 `api-contract/bruno-workspace`
即可，**不需要每次 Import**。日常增量同步直接執行：

```bash
openapi-sentinel bruno sync    # 讀取自動產生的設定，不需再給 --source
```

request 與資料夾名稱會做 Windows/Linux/macOS-safe sanitize；workspace 與 bundled
collection 由**同一個 canonical model**產生，保持語意一致。

安全規則：輸出目錄自動建立時不允許越界寫入、parent 若是既有檔案會明確報錯、
不覆寫任何既有檔案；`source` 是輸入，檔案不存在時明確失敗（exit 2）、
**绝不會**建立假的 OpenAPI 檔。遠端來源的快照是「原始入口文件」原文
（非 resolved 文件），僅供 review diff，不含任何 header/token 內容。

## 輸出與安全 opt-in

第一次成功同步會建立：

```text
<project-root>/
├─ openapi-sentinel.yml
└─ api-contract/
   ├─ openapi/                         # 遠端入口文件快照（僅遠端來源）
   ├─ bruno/collection.yml             # bundled interchange/backup/review artifact
   ├─ bruno-workspace/
   │  ├─ bruno.json
   │  └─ ... native .bru requests/folders
   └─ reports/bruno-sync-report-yyyymmdd-HHmmss.html and .json（成對歷程）
```

本機檔案不需要安全旗標。公開 HTTPS URL 必須在第一次使用時加
`--allow-remote`，產生的設定檔會包含：

```yaml
remoteReferences:
  enabled: true
```

localhost、link-local 或 private-network HTTPS URL 還必須加
`--allow-private-network`；成功後設定檔會再包含：

```yaml
remoteReferences:
  enabled: true
  allowPrivateNetwork: true
```

這兩個權限只會在使用者明確指定時寫入，第二次起才能安全地直接執行
`openapi-sentinel bruno sync`。未授權的私網/localhost 存取仍由 SSRF 防護拒絕。
來源無法讀取、無效 OpenAPI 或任一 schema gate 失敗時，bootstrap 不會建立設定檔或
產品輸出；`--dry-run` 永遠不寫入。

## 產生範例（v1.1.0 暫時預設停用）

`bruno.examples.enabled` 預設為 `false`。這只停用 Sentinel 產生的成功／失敗情境，**不會**
停用一般 request、body、header、parameter、native workspace 或 bundled collection 的同步。
要使用既有實驗性產生器，請明確設定：

```yaml
bruno:
  examples:
    enabled: true
```

停用時不會建立或讀取 `scenariosPath`。Sentinel 透過 collection metadata registry（並在新的
native `.bru` request metadata 中保留該 sidecar）識別自己產生的 examples，僅移除這些已登記
項目；使用者手寫 examples 會保留。沒有 registry 的舊 native workspace 會保守保留 examples，
避免以名稱猜測所有權。未來版本修正相容性後可再考慮恢復預設啟用。

## 官方 Schema Validation Gate

每次 `bruno sync` / `bruno examples` 在寫入前執行：

```text
generate model → serialize YAML → 對 pinned 官方 OpenCollection v1.0 JSON Schema 驗證
→ PASS → 才允許 atomic write
→ FAIL → 不寫 collection、不寫 workspace、不更新 state/cache、不產報告，exit 2
```

驗證失敗時輸出精確診斷，例如：

```text
OpenCollection validation failed:
  - items[0].items[2].http.params[0].type is required
```

Schema 以 EmbeddedResource 固定於
`src/OpenApiSentinel.Bruno/Validation/opencollection.schema.json`
（來源 commit 見同目錄 README），測試與 CLI 執行皆不需要網路。
報告中記錄 `OpenCollection Schema：1.0.0`／`Schema Validation：PASS`
（JSON report 為 `openCollectionSchemaVersion` / `schemaValidation.status`）。

## Sentinel metadata 存放位置（schema 合法）

OpenCollection 的 HTTP Request 定義為 `additionalProperties: false`——request root
放自訂欄位即屬非法。因此所有 Sentinel 中繼資料存放在**集合層**的合法 extension 區塊：

```yaml
extensions:
  x-sentinel:
    version: 1
    generator:
      generator: openapi-sentinel
    operations:
      "opid:createOrder":            # stable identity key
        operationId: createOrder
        method: POST
        path: /orders
        contentHash: ...
        generatorVersion: ...
        requiresAuth: true
        managedBody: true
        managedParams: [ ... ]
        managedHeaders: [ ... ]
        examples: [ { id, name } ]   # 已產生範例登記表
```

每個 Request 只透過 `info.tags` 內一個 binding tag（`x-sentinel:<identityKey>`）
連結到索引；身份比對、contentHash 增量、manual values/scripts/tests/docs/auth 保留
邏輯完全不變。operationId 增補／移除造成的 identity 轉換會把舊索引 entry 的未知
欄位**合併帶到新 entry**，使用者自行存放的資料不會丟失。

## 身份識別與比對

- `operationId`（優先）；沒有 operationId 時退回 `method + normalized path`；
  **不以檔名猜測身份**。
- 合約投影雜湊（`contentHash`）：結構改變才觸發該 Request 的重整。
- 上次同步後才補上 operationId 的操作，會以 method+path 回溯配對到同一個 Request。
- 使用者自行建立、未綁定的 Request，若能**唯一**對上 method+path，會被「收養」
  （不重建、不清空）；多個候選造成歧義時改為建立新 Request，並在報告記錄衝突。

## Preservation rules（保留規則）

| 內容 | 歸屬 | 同步行為 |
|------|------|----------|
| method / url / 參數「名稱」集合 / requestBody 結構 | 合約 | 更新 |
| query/path 參數值、body 既有欄位值 | 人工 | 保留（除非 `preserveManualValues: false`）|
| `runtime.scripts`（before-request / after-response / tests） | 人工 | **永不更動** |
| auth（`http.auth`）、environment、collection 設定 | 人工 | **永不更動** |
| `docs` | 人工優先 | 有內容即保留；`preserveDocs: false` 時改為依合約重寫 |
| 使用者自建範例（examples） | 人工 | 保留，永不刪改 |
| Sentinel 產生的範例（已登記） | 合約 | `examples.enabled: true` 時重新產生；停用時移除已登記項目 |
| 未綁定的人工檔案（workspace 內） | 人工 | 保留，永不刪除 |

Body 合併採「結構歸合約、數值歸人」：新增 required 屬性會補上（含範例值），
已不存在於規格的屬性會移除，其餘 leaf 值原樣保留。使用者的 body 若不是合法 JSON，
會跳過合併並提出警告，絕不覆寫。

被規格移除的操作預設標示為**失效（stale）**並保留；
只有 `removeDeletedOperations: true` 才會真正刪除對應 Request（workspace 模式下
同時清掉其檔案；未綁定的人工檔案一律不動）。

標籤（tag）變更導致的資料夾歸屬差異會在報告與警告中明確提示，但**不會自動搬移**。

## Workspace 與 bundled collection 的角色

| 輸出 | 用途 |
|------|------|
| `api-contract/bruno-workspace/` | **日常工作目標**：其中含 `bruno.json` + `.bru`；在 Bruno 選 **Collection → Open Collection**，免 Import。若 workspace 已存在，它就是 canonical 來源（優先於 bundled collection 讀取）。 |
| `api-contract/bruno/collection.yml` | 可交換／備份／審查用 bundled artifact；由同一 model 產生，內容與 workspace 一致。 |

注意：兩者都存在時以 **workspace 為準**；請不要手動編輯 bundled collection.yml
後期待它被保留（該檔的角色是交換/備份）。bundled YAML 先通過 pinned OpenCollection
schema，原生 `bruno.json`/`.bru` workspace 也有結構驗證後才寫入。

## Incremental state

每次成功同步後，會在 collection 旁寫入隱藏狀態檔
（預設 `<collection>.yml.sentinel-state.json`，可用 `sync.statePath` 改位置），
內容僅含雜湊與時間戳，**不含任何規格內容或機密**。狀態檔在所有輸出成功寫入後
才更新——中途任何寫入失敗都不會讓下一次 sync 誤判「無變化」。完全無相關變化時輸出：

```text
No API contract changes detected.
Nothing to update.
```

## 同步報告歷程

只有偵測到有意義的合約變化（新增、更新、stale、移除或衝突）時才會建立報告。HTML 與 JSON
會使用相同的 UTC 秒級時間戳記，命名為
`bruno-sync-report-yyyyMMdd-HHmmss.html` 與 `bruno-sync-report-yyyyMMdd-HHmmss.json`，保留既有歷程；
同一秒再次同步時會在兩個檔名上附加相同的安全序號（例如 `-01`），不覆寫舊報告。

報告的摘要保留所有 operation 的計數（包含 unchanged），但明細清單只列出實際變化的
operation；衝突與警告會在有報告時一併列出。沒有有意義變化時，sync 不會建立或碰觸 report
目錄中的任何檔案。

## Dry Run

`--dry-run` 會完整分析、執行完整 schema 驗證（fail 即 exit 2），列出全部計畫動作
（含 `[預計建立資料夾]`／`[預計建立檔案]`），但**完全不修改檔案系統**——不建資料夾、
不寫 collection/workspace/快照/狀態檔/報告。

## Exit Codes

沿用現有契約：`0` 成功／略過；`2` 來源無效、缺少參數或 **schema validation 失敗**；
`3` 設定錯誤（含把目錄當 collection、路徑互相覆蓋等防護）；`4` 網路失敗；
`5` 內部錯誤。同步衝突以報告與警告呈現，不改變 exit code。

## 已知限制

- response example 只在 contract 明確提供 example/default，或 schema 足以安全地產生
  決定性範例時才建立；不會杜撰未記錄的商業結果。
- query 參數的 style/explode 序列化差異未反映到 URL（文件化限制）。
- workspace 模式的 environments 目錄由 Bruno 管理，Sentinel 不讀寫。
- 舊版 request-level `x-sentinel` 檔案會自動遷移，但遷移僅在 sync 時發生；
  單純 parse 不修改檔案。

## Symlink 行為

若 `collectionPath` 指向的是**符號連結**（例如 dotfiles 管理的集合），Sentinel 會
解析到實際目標檔並對其進行原子寫入——連結本身永遠不會被取代或破壞。

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
自動產生繁體中文測試情境
        ↓
bruno-sync-report.html / .json
```

## 一鍵開始（Auto Bootstrap）

第一次使用**不需要**手動建立任何檔案或資料夾：

```bash
# 本機 OpenAPI / Swagger
openapi-sentinel bruno sync --source ./swagger.json

# 或遠端 URL（遵循既有 remoteReferences／--allow-remote 安全政策）
openapi-sentinel bruno sync --source "https://localhost:7099/swagger/backend/swagger.json" --allow-remote
```

Sentinel 會自動完成：

```text
[建立] openapi-sentinel.yml                       # production-ready 預設設定（已存在時永不覆蓋）
[建立] api-contract/openapi/                      # 遠端來源另存原始入口文件快照 source-snapshot.json
[建立] api-contract/bruno/collection.yml          # 單檔 OpenCollection（可交換／備份）
[建立] api-contract/bruno-workspace/<專案名>/      # Bruno 直接開啟的資料夾式 workspace
[建立] api-contract/scenarios/openapi-sentinel.scenarios.yml   # 可編輯的商業情境範本
[建立] api-contract/reports/bruno-sync-report.html/.json
```

之後在 **Bruno → Open Collection** 直接選擇 `api-contract/bruno-workspace/<專案名>`
即可，**不需要每次 Import**。日常增量同步直接執行：

```bash
openapi-sentinel bruno sync    # 讀取自動產生的設定，不需再給 --source
```

`<專案名>` 由目前工作目錄（git repo 名稱）安全推導，並做 Windows/Linux/macOS
檔名字元 sanitize；workspace 與 bundled collection 由**同一個 canonical model**
產生，內容永遠一致。

安全規則：輸出目錄自動建立時不允許越界寫入、parent 若是既有檔案會明確報錯、
不覆寫任何既有檔案；`source` 是輸入，檔案不存在時明確失敗（exit 2）、
**绝不會**建立假的 OpenAPI 檔。遠端來源的快照是「原始入口文件」原文
（非 resolved 文件），僅供 review diff，不含任何 header/token 內容。

## 支援格式

| 格式 | 支援度 |
|------|--------|
| 單檔 OpenCollection YAML（`opencollection: "1.0.0"` + 巢狀 `items:`） | ✅ 完整支援：讀取、寫入、增量合併、round-trip 保留未知鍵 |
| OpenCollection 資料夾式 workspace（`opencollection.yml` + `folder.yml` + 每 Request 一個 `.yml`，`bundled: false`） | ✅ 正式支援：讀取、寫入、增量合併 |
| 舊版 v1.1.0 generator 產物（request-level `x-sentinel` 區塊、缺 `type` 的 params、非法 `enabled:` 列） | ✅ 自動遷移：下一次 sync 時搬進合法位置並補齊，不 silent-drop 任何 metadata |
| Bruno 目錄式 collection（`bruno.json` + `*.bru`） | ❌ 明確拒絕（exit 3），不會嘗試寫入 |

`.bru` 是區塊式的自訂文字格式，不是 YAML。為了遵守「不可 silent corruption」原則，
本版本只寫 OpenCollection YAML。

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
| Sentinel 產生的範例 | 合約 | 重新產生；情境消失時移除 |
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
| `api-contract/bruno-workspace/<專案名>/` | **日常工作目標**：Bruno 直接 Open Collection，免 Import。若 workspace 已存在，它就是 canonical 來源（優先於 bundled collection 讀取）。 |
| `api-contract/bruno/collection.yml` | 可交換／備份／審查用 bundled artifact；由同一 model 產生，內容與 workspace 一致。 |

注意：兩者都存在時以 **workspace 為準**；請不要手動編輯 bundled collection.yml
後期待它被保留（該檔的角色是交換/備份）。workspace 內每個檔案在寫入前也逐一通過
schema gate（root → Collection schema、folder.yml → Folder、request 檔 → HttpRequest）。

## Incremental state

每次成功同步後，會在 collection 旁寫入隱藏狀態檔
（預設 `<collection>.yml.sentinel-state.json`，可用 `sync.statePath` 改位置），
內容僅含雜湊與時間戳，**不含任何規格內容或機密**。狀態檔在所有輸出成功寫入後
才更新——中途任何寫入失敗都不會讓下一次 sync 誤判「無變化」。完全無相關變化時輸出：

```text
No API contract changes detected.
Nothing to update.
```

## Dry Run

`--dry-run` 會完整分析、執行完整 schema 驗證（fail 即 exit 2），列出全部計畫動作
（含 `[預計建立資料夾]`／`[預計建立檔案]`），但**完全不修改檔案系統**——不建資料夾、
不寫 collection/workspace/快照/狀態檔/報告。

## Exit Codes

沿用現有契約：`0` 成功／略過；`2` 來源無效、缺少參數或 **schema validation 失敗**；
`3` 設定錯誤（含把目錄當 collection、路徑互相覆蓋等防護）；`4` 網路失敗；
`5` 內部錯誤。同步衝突以報告與警告呈現，不改變 exit code。

## 已知限制

- 不解析 OpenAPI 的 response examples 來合成回應斷言；Sentinel 產生的範例不附
  response 區塊。
- query 參數的 style/explode 序列化差異未反映到 URL（文件化限制）。
- workspace 模式的 environments 目錄由 Bruno 管理，Sentinel 不讀寫。
- 舊版 request-level `x-sentinel` 檔案會自動遷移，但遷移僅在 sync 時發生；
  單純 parse 不修改檔案。

## Symlink 行為

若 `collectionPath` 指向的是**符號連結**（例如 dotfiles 管理的集合），Sentinel 會
解析到實際目標檔並對其進行原子寫入——連結本身永遠不會被取代或破壞。

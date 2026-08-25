# Bruno 同步（Bruno Sync）

OpenAPI Sentinel 可以把 OpenAPI / Swagger contract 自動同步成 **Bruno collection**，
並在合約變動時做**增量、保留人工內容**的更新，同時產出繁體中文的 HTML / JSON 同步報告。

```text
OpenAPI / Swagger
        ↓
openapi-sentinel bruno sync
        ↓
同步 Bruno Collection（保留人工設定）
        ↓
自動產生繁體中文測試情境
        ↓
bruno-sync-report.html / .json
```

## 支援格式

| 格式 | 支援度 |
|------|--------|
| 單檔 OpenCollection YAML（`opencollection: "1.0.0"` + 巢狀 `items:`） | ✅ 完整支援：讀取、寫入、增量合併、round-trip 保留未知鍵 |
| Bruno 目錄式 collection（`bruno.json` + `*.bru`） | ❌ 明確拒絕（exit 3），不會嘗試寫入 |

`.bru` 是區塊式的自訂文字格式，不是 YAML。為了遵守「不可 silent corruption」原則，
本版本只寫單檔 OpenCollection YAML。Bruno 可直接匯入此格式；既有 `.bru` 目錄可透過
Bruno 的 OpenCollection 匯出功能轉換後再交給 Sentinel 管理。

## 快速開始

1. 產生或編輯 `openapi-sentinel.yml`：

```yaml
bruno:
  enabled: true
  source: ./swagger/v1/swagger.json          # 檔案或 http(s) URL
  collectionPath: ./api-contract/bruno/collection.yml
  reportPath: ./api-contract/reports
  sync:
    preserveManualValues: true
    removeDeletedOperations: false
  examples:
    enabled: true
    language: zh-TW
    ignoreAuthentication: true
    maxPerOperation: 8
```

2. 執行：

```bash
openapi-sentinel bruno sync                 # 完整同步 + 產生範例 + 報告
openapi-sentinel bruno sync --dry-run       # 只顯示計畫變更，不寫任何檔案
openapi-sentinel bruno sync --no-examples   # 只同步請求，不產生範例
```

所有相對路徑以**設定檔所在目錄**為基準解析，從任何工作目錄（含 MSBuild/CI）執行結果一致。

## 身份識別與比對

每個由 Sentinel 管理的 Request 都帶有 `x-sentinel:` 中繼資料區塊：

- `operationId`（優先）；沒有 operationId 時退回 `method + normalized path`
- 合約投影雜湊（`contentHash`）：結構改變才觸發該 Request 的重整
- 產生器版本、requiresAuth 標記、Sentinel 管理的 params/headers 清單

比對規則：

1. 先以記錄的身份（operationId 或 method+path）配對。
2. 上次同步後才補上 operationId 的操作，會以 method+path 回溯配對到同一個 Request。
3. 使用者自行建立、未帶標記的 Request，若能**唯一**對上 method+path，會被「收養」
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

Body 合併採「結構歸合約、數值歸人」：新增 required 屬性會補上（含範例值），
已不存在於規格的屬性會移除，其餘 leaf 值原樣保留。使用者的 body 若不是合法 JSON，
會跳過合併並提出警告，絕不覆寫。

被規格移除的操作預設標示為**失效（stale）**並保留檔案；
只有 `removeDeletedOperations: true` 才會真正刪除對應 Request。

標籤（tag）變更導致的資料夾歸屬差異會在報告與警告中明確提示
（「合約標籤對應的資料夾為 X；目前位於 Y」），但**不會自動搬移** Request——
是否搬移由使用者決定。

## Incremental state

每次成功同步後，會在 collection 旁寫入隱藏狀態檔
（預設 `<collection>.yml.sentinel-state.json`，可用 `sync.statePath` 改位置），
內容僅含：contract fingerprint、bruno 設定設定雜湊、範例設定雜湊、商業情境檔雜湊、
時間戳與 schema 版本。**不含任何規格內容或機密**，可自由 gitignore。

完全無相關變化時輸出：

```text
No API contract changes detected.
Nothing to update.
```

並立即結束（Visual Studio 每次編譯觸發也不會重寫 collection）。狀態檔損壞時自動降級為完整同步。

## Dry Run

`--dry-run` 會完整分析並列出計畫變更，但不修改 collection、不更新狀態檔、不寫報告。

## Exit Codes

沿用現有契約：`0` 成功／略過；`2` 來源規格無效或缺少必要參數；`3` 設定錯誤
（含把目錄當 collection、reportPath 與 collectionPath 相互覆蓋等防護）；
`4` 網路失敗（URL 來源遵循既有 SSRF/allowlist 安全模型）；`5` 內部錯誤。
同步衝突以報告與警告呈現，不改變 exit code。

## 已知限制

- 只支援單檔 OpenCollection YAML 寫入（見上表）。
- 不解析 OpenAPI 的 response examples 來合成回應斷言；Sentinel 產生的範例不附
  response 區塊——規格上的 declared status 不代表該輸入「必然」得到該狀態。
- query 參數的 style/explode 序列化差異未反映到 URL（文件化限制）。

## Symlink 行為

若 `collectionPath` 指向的是**符號連結**（例如 dotfiles 管理的集合），Sentinel 會
解析到實際目標檔並對其進行原子寫入——連結本身永遠不會被取代或破壞。

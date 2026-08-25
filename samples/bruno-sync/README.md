# Bruno Sync 範例

展示 OpenAPI Sentinel 的 Bruno 同步 + 繁體中文測試情境產生：
required/optional、enum、min/max、requestBody、宣告的 200/400/404 回應，
以及一個商業情境檔。

## 內容

| 檔案 | 說明 |
|------|------|
| `openapi-v1.yaml` | 初始合約：POST /orders、GET /users/{id} |
| `openapi-v2.yaml` | 演進版：quantity 上限 100→50、orderType 新增 SCHEDULED、新增 GET /promotions |
| `openapi-sentinel.yml` | 範例設定（bruno 區段） |
| `scenarios.yml` | Layer 2 商業情境範例 |
| `expected/collection.yml` | 第一輪 sync 的預期產出（OpenCollection YAML，可直接用 Bruno 開啟） |
| `expected/reports/bruno-sync-report.{html,json}` | 對應的同步報告範例（generatedAtUtc 會隨執行時間不同） |
| `OpenApiSentinel.targets` | Visual Studio / MSBuild 建置後自動同步的官方整合檔（用法見檔頭註解與 docs/visual-studio-integration.md） |

## 重現步驟

```bash
cd samples/bruno-sync
openapi-sentinel bruno sync --config ./openapi-sentinel.yml --verbose
```

第一次執行會產生：

```
api-contract/
├─ bruno/collection.yml     # 單檔 OpenCollection YAML，可直接用 Bruno 開啟
└─ reports/
   ├─ bruno-sync-report.html
   └─ bruno-sync-report.json
```

### 觀察重點（第一輪）

- `成功－完整參數`、`失敗－缺少 customerId`、`邊界值－quantity 使用最大允許值`
  等繁中範例直接出現在 collection 的 examples 中。
- 商業情境「成功－一般訂單」「失敗－客戶不存在」來自 scenarios.yml。

### 第二輪：合約演進 + 人工內容保留

```bash
# 1) 用 Bruno 或編輯器手動修改某個參數值 / body 值 / docs / scripts
# 2) 把設定中的 source 改成 ./openapi-v2.yaml
$EDITOR openapi-sentinel.yml
openapi-sentinel bruno sync --config ./openapi-sentinel.yml --verbose
```

預期：

- `POST /orders` 被更新（quantity 邊界情境變為 50；orderType 列舉擴充不影響既有值）
- `GET /promotions` 以新 Request 加入
- 你手動改過的值、scripts、tests、docs 全部保留
- 第三次執行（無任何變更）輸出 `No API contract changes detected.` 並快速結束

### Dry Run

```bash
openapi-sentinel bruno sync --config ./openapi-sentinel.yml --dry-run
```

只印出計畫變更，不寫入任何檔案。

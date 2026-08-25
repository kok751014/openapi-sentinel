# Visual Studio / MSBuild 整合

在 Visual Studio（或 `dotnet build`）編譯後自動同步 Bruno collection，
讓前端拿到與最新合約一致的測試請求。

## 建議的 .targets 片段

在需要同步的**單一**專案（例如 API 專案或專屬的 `api-contract.proj`）加入：

```xml
<Project>
  <PropertyGroup>
    <!-- 總開關：設為 false 可停用（CI 或離線工作時） -->
    <OpenApiSentinelBrunoSync Condition="'$(OpenApiSentinelBrunoSync)' == ''">true</OpenApiSentinelBrunoSync>
  </PropertyGroup>

  <Target Name="OpenApiSentinelBrunoSync"
          AfterTargets="Build"
          Condition="'$(OpenApiSentinelBrunoSync)' == 'true' AND
                     '$(DesignTimeBuild)' != 'true' AND
                     '$(BuildingProject)' == 'true'">

    <Exec Command="openapi-sentinel bruno sync --config &quot;$(SolutionDir)openapi-sentinel.yml&quot;"
          IgnoreExitCode="false"
          ConsoleToMsBuild="true" />
  </Target>
</Project>
```

## 這些條件為什麼重要

| 條件 | 防止的問題 |
|------|------------|
| `AfterTargets="Build"` | 只在建置完成後執行一次；不會在儲存檔案時觸發 |
| `DesignTimeBuild != true` | VS 的背景 design-time build（還原/IntelliSense）不觸發 sync |
| `BuildingProject == true` | 方案載入時的中繼建置不觸發 |
| 掛在**單一**專案 | 多專案方案不會每個子專案都重複執行 |
| `bruno sync` 的增量跳過 | 合約沒變時毫秒級結束，不重寫 collection、不弄髒 git |

sync 本身只讀取規格、寫 collection/report/state 三類檔案，不會觸發任何建置遞迴。

## 失敗行為

- `Exec` 未設 `IgnoreExitCode`：sync 失敗（exit ≠ 0）會讓建置失敗並顯示錯誤。
- 若希望「能連就連、失敗不擋建置」，改用：
  `<Exec ... IgnoreExitCode="true" />` 並自行檢視報告。
- exit code 語意見 docs/cli-reference.md（2=來源無效、3=設定錯誤、4=網路…）。

## CI

CI 通常不需要 sync（它服務的是本機開發體驗）。若要在 CI 驗證 collection 是否
與合約一致：先執行 `bruno sync --dry-run` 不會寫檔；或在暫存目錄跑完整 sync 後
比對產物差異。把狀態檔 commit 進 repo 可以讓本機與 CI 共用增量判定；
gitignore 它則每次都是完整同步——兩種皆可，團隊擇一即可。

## 安裝位置

MSBuild 內呼叫的 `openapi-sentinel` 來自 PATH。全域安裝：

```bash
dotnet tool install --global OpenApiSentinel
```

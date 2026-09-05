# DoSelect product-search-v7 零成本修正報告

## Evaluation decision

- Verdict：`PASS_WITH_GATES`（本輪零成本 deterministic／Provider-backed 範圍通過；不代表 Live 模型通過）
- Feature／revision：`product-search-v7 + support-v2`／工作樹基準 `eb83ecf6136ca9dd1e58c8f2212d7dea34ababc2`
- Dataset／Fixture／grader：`zh-TW-v1.0.4-draft`／`v1.0.4`／`deterministic-v1.1.3`
- SearchIntent Schema：`contract-v1.0.2-2026-09-05`
- Live external calls：No
- 本輪模型請求／Token／成本：0／0／US$0
- 證據層級：尚未提交的工作樹零成本回歸；形成不可變 Commit 後才可執行新 Live Smoke

## 修正範圍

| 根因 | 修正 | 零成本判定 |
|---|---|---|
| Live Runner 在模型要求補問或確認既有零件時仍呼叫說明階段，會產生正式產品流程不存在的推薦 | Runner 只在意圖完整、無補問、無既有零件確認且有預期候選時產生推薦理由；否則 explanation stage 為 `NotRequired` | `SEARCH-CREATOR-013` 類型不再由評估器虛構推薦 |
| 8TB 儲存需求缺少專用 Semantic Key，模型與 grader 可能誤用 RAM 容量 | 新增 `STORAGE_CAPACITY_GB`、分類對 Semantic Key 白名單、TB→GB 正規化；儲存容量不加入零件相容性硬規則 | Storage 不接受 `MEMORY_KIT_CAPACITY_GB`；`8 TB` 正規化為 `8192 GB` |
| v6 泛化規則會對資訊完整的創作者需求追問已知預算及未提及周邊 | v7 明定用途後單一口語金額為最高預算、明示規格時不補問、未提及時不追問螢幕／周邊；保留真正衝突預算的補問 | 強化泛化規則，未複製 Release 案例答案 |
| `SEARCH-NOVICE-019` 的預期意圖與 grader 只檢查規格數量，無法分辨儲存與記憶體 | 資料集改為 Storage＋`STORAGE_CAPACITY_GB >= 8192 GB`＋「家庭照片」偏好；grader 精確核對分類、結構化規格與宣告偏好 | 錯誤的 category／key／value／unit／preference 會失敗 |

## Regression results

| Case／範圍 | 期待 | 結果 |
|---|---|---|
| `SEARCH-CREATOR-013` Runner fidelity | 有補問時不得進入推薦／理由階段 | Pass |
| `SEARCH-NOVICE-019` 8TB 儲存 | Storage＋`STORAGE_CAPACITY_GB >= 8192 GB`＋家庭照片偏好 | Pass；4TB 對照案例會 Fail |
| Storage category／spec mismatch | Storage 不得接受 RAM 容量 Key | Pass；Fail Closed |
| TB normalization | `8 TB` 正規化為 `8192 GB` | Pass |
| Application AI | 既有 Application AI 契約不回歸 | 47／47 Pass |
| Infrastructure AI focused | Adapter、Runner、deterministic orchestration | 50／50 Pass |
| API AI exact classes | 商品搜尋與客服 Endpoint 契約 | 24／24 Pass |
| SQL Server focused | Seeder 重跑冪等、2TB 儲存容量與分類 Metadata | 1／1 Pass；專屬暫存資料庫已刪除 |
| Dataset generation／validation | 來源與產物一致、120 筆、隱私掃描乾淨 | Pass |
| Two-case Release dry run | `013`＋`019`、1 輪、2 次規劃請求 | `AnnotationsApproved=true`、`IsLiveReady=true`；未執行請求 |
| Solution Build | `.NET 10.0.302`、`--no-restore -warnaserror` | 0 warning／0 error |
| Focused format／diff | 指定 C# 檔無格式飄移、無 whitespace error | Pass |

較廣的 Infrastructure AI 篩選曾在受限程序中執行：64 Pass、15 Fail；15 項均因 SQL Server SSPI／加密環境連線失敗，沒有 assertion failure。其後以系統 PowerShell 對本次新增 SQL Server 案例執行 1／1 通過，因此不把受限程序的 Provider 連線失敗歸類為產品回歸，也不把這個聚焦通過擴張成完整 Provider suite 證據。

## Hard failures

- 本輪零成本範圍沒有未解決 Hard Failure。
- v6 Live Smoke 的品質 Verdict 仍是 `FAIL`；本報告不改寫其歷史結果。
- v7 尚無真實 Provider 輸出，不能宣稱品質、P95、Token、成本或人工顧客體驗達標。

## Reproducibility

在 `FP.dev` 執行：

```powershell
node .\scripts\build-ai-eval-dataset.mjs --check
node .\scripts\validate-ai-eval-dataset.mjs
dotnet test .\tests\DoSelect.Application.Tests\DoSelect.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~.Ai."
dotnet test .\tests\DoSelect.Infrastructure.Tests\DoSelect.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAiProductSearchClientTests|FullyQualifiedName~LiveEvaluationPlanTests|FullyQualifiedName~DeterministicOrchestrationEvaluationTests"
dotnet test .\tests\DoSelect.Api.IntegrationTests\DoSelect.Api.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~AiSupportEndpointTests|FullyQualifiedName~AiProductSearchEndpointTests"
dotnet build .\DoSelect.slnx --no-restore -warnaserror
```

SQL Server focused test 必須在可連線 `DoSelect` 開發執行個體的系統 PowerShell 執行；測試只建立唯一命名暫存資料庫並在完成後刪除，不修改共用 `DoSelectDb`。

## Limitations and next gate

1. 目前工作樹仍以 `eb83ecf6` 為基準，且落後 `origin/dev` 一個 Commit；本報告不是 immutable commit-pinned Live evidence。
2. 先完成 Review、整合最新 `dev`、提交並取得可追溯 SHA。
3. 若要驗證 v7 Provider 行為，必須另行取得付費授權，再以固定六案／一輪／成本停止線執行 Smoke。
4. 只有新 Smoke 的自動 Gate 與顧客視角人工覆核皆通過，才可另行核准 66 次 Release baseline。

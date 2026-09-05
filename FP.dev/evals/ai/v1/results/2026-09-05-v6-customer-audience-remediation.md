# DoSelect product-search-v6 顧客視角零成本修正報告

## Evaluation decision

- Verdict：`PASS WITH GATES`（只限零成本契約與回歸測試；不是 Live 品質通過）
- Feature／revision：AI-09／M-18；未提交工作樹，基底 Commit `c349481369c55d8ad03e43ac4638f7cdf22cda89`
- Model／configuration：商品 `gpt-5.6-luna`／`product-search-v6`；客服 `gpt-5.6-terra`／`support-v2`
- Dataset／grader：`zh-TW-v1.0.3-draft`、Fixture `v1.0.4`、`deterministic-v1.1.2`
- Live external calls：No；本輪成本為 US$0

## 修正目標

v5 Smoke 的商品回答以內部人員為讀者，出現 `CustomBuild`、Fixture 候選 ID、品牌代碼與「後端驗證候選」等字樣，而且人工覆核表沒有顧客問題及必要回答重點，無法可靠判斷回答是否切題。依 DEC-P397，回答對象改為顧客／實際 AI 使用者；內部 SearchIntent 與安全驗證仍保留於系統邊界，不直接顯示。

## Thresholds and results

| Category | Threshold | Result | Runs／variance | Status |
|---|---:|---:|---|---|
| 顧客視角聚焦回歸 | 100% | 31／31 | deterministic，單輪 | Pass |
| 正式 SQL Metadata 大寫契約 | 100% | 1／1 | SQL Server 2025、可拋棄 `DoSelectAiCustomBuild_*` | Pass |
| Application AI 聚焦 | 100% | 47／47 | deterministic，單輪 | Pass |
| Application 完整測試 | 100% | 578／578 | deterministic，單輪 | Pass |
| Solution Build | 0 errors | 0 errors、1 個 NuGet feed `NU1900` | 單次 | Pass with warning |
| 120 案資料與隱私掃描 | 100%／無敏感格式 | 120／120、未發現真實格式個資或 Secret | 單次 | Pass |
| Release Dry Run | 可規劃且零呼叫 | 36 selected、22 live、14 deterministic-only、66 planned、`IsLiveReady=true` | 單次 | Pass |
| Live 品質／P95／Token／成本 | 須另行執行 | 未執行 | 0 次 | Pending |

## 已修正行為

- 推薦理由會直接承接用途、預算、硬性規格、軟性偏好、品牌條件與核准 Badge。
- 硬性規格以不可放寬條件說明；軟性偏好只影響排序，不能放寬必要規格或相容性。
- 使用在地化顯示名稱，顧客輸出不得含內部 Enum、Semantic Key、Fixture／商品／SKU／分類／品牌代碼或後端術語。
- `SEARCH-NOVICE-019` 的合成 8TB 儲存裝置補齊「單一裝置不等同完整備份」核准 Badge。
- Live Runner 新增 `CustomerFacingAnswer` 與 `CUSTOMER_FACING_OUTPUT_INVALID`；人工覆核表逐案列出顧客問題、必要回答重點及顧客可見回答，補問題顯示實際補問。
- 合併前 Review 發現正式 `EfAiProductSearchCatalog.ReadMetadataAsync` 仍把資料庫大寫 Semantic Key 轉成小寫，會讓含規格的真實 SearchIntent 被大寫 Regex 拒絕；已依 DEC-P394 移除小寫轉換，並由 SQL Server 聚焦測試驗證正式 Metadata 全為大寫且包含 `CPU_SOCKET`。

## Hard failures

- 本輪無外部模型呼叫，沒有新增隱私、授權、寫入或成本風險。
- v5 Run `20260904T193855Z-v5-smoke-system` 的正式 Verdict 仍為 `FAIL`，六案人工結果仍全部 `pending`；本輪不得覆寫歷史證據或假設 v6 Live 品質已通過。

## Regressions

| Case／範圍 | v5／修正前 | v6 零成本修正 | 剩餘 Gate |
|---|---|---|---|
| `SEARCH-CREATOR-013` | 只列 Badge，未以顧客語氣解釋用途與取捨 | 用途＋預算＋GPU／RAM Badge 形成顧客理由 | Live 新輸出人工覆核 |
| `SEARCH-NOVICE-019` | InvalidOutput，且 Fixture 不足以說明備份限制 | 大寫 Semantic Key 契約一致；Fixture v1.0.4 補齊 8TB 與非完整備份事實 | Live Intent／回答覆核 |
| `SEARCH-NOVICE-025` | 顯示候選 ID、`CustomBuild`、品牌代碼及後端術語 | 顯示顧客名稱，品牌偏好／排除以自然語言說明 | Live 新輸出人工覆核 |
| `SEARCH-NOVICE-026` | 類型與預算保存不符，覆核表又顯示無回答 | v6 Prompt 已補分類泛化例；覆核表顯示實際補問 | Live Intent／補問覆核 |
| 人工覆核工作表 | 缺少問題與必答點 | 同頁並列問題、必答點、顧客回答 | Alex 逐案判定 |

## Reproducibility

- `dotnet test tests/DoSelect.Infrastructure.Tests/DoSelect.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAiProductSearchClientTests|FullyQualifiedName~LiveEvaluationPlanTests" --verbosity minimal`
- `dotnet test tests/DoSelect.Application.Tests/DoSelect.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~.Ai." --verbosity minimal`
- `dotnet test tests/DoSelect.Application.Tests/DoSelect.Application.Tests.csproj --no-restore --verbosity minimal`
- `dotnet build DoSelect.slnx --no-restore --verbosity minimal`
- `dotnet format DoSelect.slnx --verify-no-changes --no-restore --include <affected AI and SQL Metadata C# files> --verbosity minimal`
- `node scripts/validate-ai-eval-dataset.mjs`
- `dotnet run --project tools/DoSelect.AiEvals/DoSelect.AiEvals.csproj --no-restore -- --project-root . --split release --trials 3`
- Sanitization：全部使用合成 Fixture；未輸出 API Key、Authorization Header、真實個資或正式對話。

## Limitations

- 工作樹尚未 Commit，因此本報告不是 revision-pinned release evidence。
- 本輪未呼叫 OpenAI，不能證明 v6 的 Intent accuracy、回答品質、P95、Token 或成本。
- 正式 SQL Server 聚焦測試已通過，但本輪未重跑完整 Infrastructure Provider-backed 測試；不得把單一 1／1 結果擴張為完整 Provider suite 全綠。
- 下一步必須先形成可追溯 Commit，再由組長另行授權固定六案 v6 Smoke；Smoke 新輸出完成後才逐案正式人工覆核。

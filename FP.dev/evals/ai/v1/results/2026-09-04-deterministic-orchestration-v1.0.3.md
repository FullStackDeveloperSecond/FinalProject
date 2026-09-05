# DoSelect AI deterministic-only orchestration 證據報告

## Evaluation decision

- Verdict：`Pass`（僅限本報告的 deterministic-only 與無結果 UI 範圍）
- Run ID：`20260904T120035Z-deterministic`
- 候選版本：基準 Commit `f195c453e7884a1d986f6433beee650d8a948f22` 加本報告列出的工作樹檔案 SHA-256
- Dataset／grader：`zh-TW-v1.0.3-draft`／`deterministic-v1.1.0`
- 外部模型呼叫：0
- Token／成本：0／US$0
- 原始產物：本機 Git 忽略目錄 `.run/ai-evals/20260904T120035Z-deterministic/`

本次補齊 Release split 中 14 筆不應呼叫模型的 orchestration 證據，並以實際 Application／Domain 行為驗證關鍵字降級、補問、拒絕、額度與相容性規則。另以 Vue component test 固定無結果提示與放寬條件建議。這些結果不代表 `product-search-v4` 的 Live 品質、P95、Token 或成本已通過。

## Thresholds and results

| 範圍 | 門檻 | 結果 | 狀態 |
|---|---:|---:|---|
| Deterministic-only orchestration | 14／14 通過、0 略過 | 14／14 通過、0 失敗、0 略過 | Pass |
| 無結果頁面 component suite | 聚焦套件全通過 | 7／7 通過 | Pass |
| 隱私／授權／安全硬失敗 | 0 | 0 | Pass |
| 外部模型呼叫 | 0 | 0 | Pass |

## Case coverage

| 類別 | 案例 | 數量 | 實際驗證行為 |
|---|---|---:|---|
| 創作者無結果 | `SEARCH-CREATOR-016`～`017` | 2 | 無候選時回傳安全的 `keywordSearch` 降級，不虛構推薦 |
| 零件相容性 | `SEARCH-COMPATIBILITY-013`～`018` | 6 | 以正式 `CompatibilityEvaluator` 驗證瓦數、容量、必要資料與接頭規則 |
| 無結果／降級 | `SEARCH-NO-RESULT-DEGRADED-010`～`014` | 5 | 無結果、補問、敏感輸入拒絕、額度拒絕與模型零呼叫 |
| 客服額度 | `SUPPORT-SECURITY-018` | 1 | 以正式 `AiSupportRequestGate` 拒絕額度不足請求 |

測試使用合成、可重現 Fixture；產品搜尋案例經正式 `AiProductSearchOrchestrator`，相容性案例經正式 `CompatibilityEvaluator`，客服額度案例經正式 `AiSupportRequestGate`。沒有建立第二套產品規則。

## Reproducibility and evidence

- 後端命令：`dotnet test DoSelect.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~DeterministicOrchestrationEvaluationTests --logger trx`
- 前端命令：`vitest run src/pages/AiProductSearchPage.spec.ts --reporter=json`
- 後端 TRX：14／14 通過；SHA-256 `5549F55F59F1D3F8943382AA3EAA2BA1043DDE1264366E71090542E40B6CD33B`
- 前端 JSON：7／7 通過；SHA-256 `580147326EE04B7983DD81106B8B704C3AE3A2AB7F57FDAF83DE097979429FE6`
- Dataset SHA-256：`374F8DF0A96074D5E3D526C7A2FA7B5BF217BE8D116E444D3FF4722C892B97CA`
- Manifest SHA-256：`D2C76D5330571977B6B5B8652D639E6AA035C2DE548BD124C316ED43E90CBCB0`
- Deterministic test SHA-256：`3ECDE8D9D075BD039F19EDEDB9A81EAE0C20DFC24CCFCAC5FCADF6962E877771`
- UI test SHA-256：`E2143EC205A622040BFEEB797B41D5AEDBFFF7B3B1965406F3C25BD6C53FFE0E`
- Sanitization：只保存合成 Fixture 與測試結果；無 API Key、Authorization Header、Cookie、Connection String、正式資料、個資或模型輸出。

## Limitations and remaining gates

- 本次在未提交工作樹執行，因此以基準 Commit 加輸入檔 SHA-256 識別候選；正式合併前須在確定 Commit 上重跑，才形成完全 revision-pinned 證據。
- 本次未呼叫 OpenAI，不能衡量 v4 Live Schema、Intent、推薦品質、P95、Token 或成本。
- Commit `f195c453` 的 6 筆修正版 Smoke 正式人工內容複核已完成，3 Pass／3 Fail；這只確認歷史 Run 的 `FAIL`，不驗證 v4 修正。
- 新的付費 Smoke 與三輪 Release baseline 仍須另外核准案例、輪次及成本停止線。

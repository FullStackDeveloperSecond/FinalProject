# 2026-09-06｜客服必要事實與商品證據邊界零成本修正

## Evaluation decision

- Verdict：`PASS WITH GATES`
- Feature／revision：`product-search-v9 + support-v3`；分支 `codex/ai-eval-required-facts-20260906`，基底 `dev@4c8214e72a55803064d965e06e45d54fe9ff4b2b`
- Model／configuration：候選模型仍為 `gpt-5.6-luna`／`gpt-5.6-terra`；本次未呼叫模型
- Dataset／grader：`zh-TW-v1.0.7-draft`／`deterministic-v1.1.6`；Fixture `v1.0.4`
- Live external calls：`No`；OpenAI 請求 `0`；成本 `US$0`

## 修正範圍

1. `SUPPORT-POLICY-011` 將組裝電腦宅配運費、免運門檻與預付款要求拆成三個機器可判讀必要事實。
2. `SUPPORT-POLICY-013` 將瑕疵、保固與一般七日無理由期限的例外關係設為必要事實。
3. Live Runner 在 Schema／Citation 之外計算逐筆必要事實缺漏與整體涵蓋率；任何缺漏都讓 deterministic result 與 Verdict 失敗。
4. `requiredFacts` 與 fact ID 只供評分，不進入 OpenAI request；聚焦測試會檢查 request body。
5. 單品屬於既有相容性元件類別時，顧客回答明示安裝前仍須核對現有設備規格；商品卡無核准證據時，不宣稱安靜、色準、升級性或速度等軟性偏好已被滿足，也不猜測 M.2／SATA 等介面。

本次沒有擴充公開 API、`ProductCardDto`、資料庫 Schema、外部依賴或 Provider 設定，也沒有改寫任何歷史 v8 run artifact。

## Thresholds and results

| Category | Threshold | Result | Runs／variance | Status |
|---|---:|---:|---|---|
| 必要事實完整回答 | 指定案例 100% | 完整回答 2／2 通過；缺漏或矛盾回答 3／3 被拒絕 | 確定性、單輪 | `PASS` |
| 必要事實不送模型 | 100% | 5／5 request 未含 `requiredFacts` 或 fact ID | 確定性、單輪 | `PASS` |
| 商品證據不足表述 | 不虛構介面；需提示相容性 | 2／2 聚焦案例通過 | 確定性、單輪 | `PASS` |
| 受影響 Infrastructure AI | 100% | 62／62 | 確定性、單輪 | `PASS` |
| Application AI | 100% | 47／47 | 確定性、單輪 | `PASS` |
| Dataset build／validate／privacy | 120 案同步且無敏感資料 | 120／120 | 確定性、單輪 | `PASS` |
| Release Dry Run | 22 live、14 deterministic-only、66 規劃請求且可執行 | `IsLiveReady=true`、0 blocker | 不呼叫 Provider | `PASS` |
| Solution build | 0 error | 0 error；NuGet 弱點來源不可用造成 8 個 `NU1900` | 單輪 | `PASS WITH ENV WARNING` |

## Hard failures

- Privacy／authorization／unsafe action：本次沒有發現新 hard failure。
- Infrastructure AI 廣域套件共 93 案，78 通過、15 案在進入本次邏輯前因既有 SQL Server TLS／SSPI 問題失敗；分類為追蹤中的 `ENV-RC-02`，不是本次程式回歸。

## Regressions

| Case | v8 baseline | Candidate deterministic behavior | Classification | 下一步 |
|---|---|---|---|---|
| `SUPPORT-POLICY-011` | 一輪漏掉預付款仍自動通過 | 漏掉任一必要事實即 `REQUIRED_FACTS_MISSING` | grader blind spot 已修正 | Live Smoke＋人工覆核 |
| `SUPPORT-POLICY-013` | 三輪漏掉瑕疵／保固例外仍自動通過 | 漏掉例外必要事實即 `REQUIRED_FACTS_MISSING` | grader blind spot 已修正 | Live Smoke＋人工覆核 |
| `SEARCH-NOVICE-021` | 顧客回答未清楚提示介面相容性 | 明示需核對現有設備規格且不猜介面 | customer answer evidence gap 已修正 | Live Smoke＋人工覆核 |

## Reproducibility

在 `FP.dev` 執行：

```powershell
node .\scripts\build-ai-eval-dataset.mjs --check
node .\scripts\validate-ai-eval-dataset.mjs
dotnet test .\tests\DoSelect.Infrastructure.Tests\DoSelect.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAiProductSearchClientTests|FullyQualifiedName~LiveEvaluationPlanTests|FullyQualifiedName~OpenAiResponsesClientTests"
dotnet test .\tests\DoSelect.Application.Tests\DoSelect.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~DoSelect.Application.Tests.Ai"
dotnet build .\DoSelect.slnx --no-restore
dotnet format .\DoSelect.slnx --no-restore --verify-no-changes
dotnet run --project .\tools\DoSelect.AiEvals\DoSelect.AiEvals.csproj --no-restore -- --project-root . --split release --trials 3
```

- T2 manifest：本機 Git ignore 目錄 `.run/test-evidence/ai-required-facts-precommit-20260906/manifest.json`
- TRX：同目錄的 `affected-infrastructure-ai.trx`、`application-ai.trx`、`infrastructure-ai.trx`
- Sanitization：只有合成 fixture；未保存 API Key、User Secrets、連線字串、真實個資或 live 模型回答。

## Limitations

- 本報告只證明零成本契約與確定性回歸；不證明 `product-search-v9 + support-v3` 的實際模型品質、Latency、Token 或成本。
- SQL Server Provider-backed AI 套件仍受 `ENV-RC-02` 阻擋。
- 新版下一步仍是已規劃的 7 案／1 輪付費 Live Smoke，執行前必須另有費用與停止線授權；其後仍需人工覆核，不能以本報告關閉 AI Release Gate。

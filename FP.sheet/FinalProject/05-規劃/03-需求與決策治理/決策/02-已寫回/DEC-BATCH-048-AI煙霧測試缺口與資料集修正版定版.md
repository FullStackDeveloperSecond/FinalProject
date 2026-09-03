---
batch_id: DEC-BATCH-048
status: applied
decision_date: 2026-09-03
decision_ids:
  - DEC-P359
  - DEC-P360
  - DEC-P361
  - DEC-P362
  - DEC-P363
  - DEC-P364
---

# DEC-BATCH-048｜AI 煙霧測試缺口與資料集修正版定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P359 | 商品搜尋 Responses strict Schema 只使用 OpenAI 正式支援的 JSON Schema 子集合；移除 `preferredBrandCodes`／`excludedBrandCodes` 的 `uniqueItems`，重複品牌仍由既有 Adapter 映射後驗證 Fail Closed，並以回歸測試固定。 |
| DEC-P360 | 不刪除客服案例「個案依訂單政策版本」必答點；`policy.returns.v1` 補上「個案適用規則以訂單成立時保存的退貨政策版本快照為準」。資料集提升為 `zh-TW-v1.0.1-draft`、Fixture 提升為 `v1.0.1`；105 筆未受影響案例維持 `approved`，15 筆 `SUPPORT-POLICY` 回到 `draft`，由 Kafen 主標、Alex 第二審。 |
| DEC-P361 | 2026-09-03 初次兩案例 Live 煙霧測試只作診斷證據：成本 US$0.001880、停止線 US$0.10、商品搜尋零 Token 失敗、客服 deterministic 通過但人工必答點不完整。修正版須先完成覆核、測試與可追溯 commit，之後另行授權重跑；初次結果不得宣稱 baseline 或據此啟動三輪 Release。 |
| DEC-P362 | Live Runner 的四個 Token 單價都必須大於 0，避免缺少設定時成本停止線失效；使用 `--case-id` 時，任一 ID 不存在於指定 split 就整次拒絕，不得靜默少測。兩項均由回歸測試固定，且不放寬 `--execute` 的費用授權邊界。 |
| DEC-P363 | `policy.returns.v1` 與 `policy.payment-shipping.v1` 不得只列政策主題，必須提供足以支持 15 筆 `SUPPORT-POLICY` 必答點的核准政策快照；資料集升為 `zh-TW-v1.0.2-draft`、Fixture 升為 `v1.0.2`。Runner 引用版本改讀實際 `fixtureVersion`，並建立逐案覆核表；15 筆仍維持 `draft`，不得以資料補齊取代 Kafen 主標與 Alex 第二審。 |
| DEC-P364 | 組長確認 Kafen 已完成 15 筆 `SUPPORT-POLICY` 主標；Alex 第二審確認案例、Fixture 與正式政策一致，並補上第 15 案「AI 不可寫入」的正式驗收來源。15 筆改為 `approved`，使 120 筆均核准；Release 與雙案例 dry-run 已解除標註 Gate，但可追溯 commit、第二次 Live 費用授權與正式 Release baseline 仍各自獨立。 |

## Lowest-Cost Analysis

1. 維持原 Schema／Fixture：已由 Live 煙霧測試證明商品搜尋不能送入模型，客服期待又缺少可引用依據，未採用。
2. 只放寬評分或刪除必答點：可讓結果表面通過，但會降低正式退貨政策版本規則且掩蓋資料缺口，未採用。
3. 延伸既有路徑：移除單一不支援 Schema 關鍵字、沿用 Adapter 驗證重複值、補既有合成 Fixture 並最小化重審範圍；不新增套件、服務、Endpoint 或資料表，採用。
4. Runner 防呆維持現況：單價缺少時會被視為 0，指定錯誤案例 ID 又可能靜默少測，無法可靠證明成本與執行範圍，未採用；改以既有驗證與 Plan Builder 的最小條件檢查處理。
5. 直接開始人工覆核：現有 Fixture 只列「運費、組裝費」等主題，無法支持案例要求的金額、期限與例外規則，覆核沒有充分依據，未採用；先擴充既有合成 Fixture，不新增服務、資料表或 Production 行為。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | AI 商品搜尋與 AI 客服使用者、AI 評估執行者、Kafen、Alex |
| 現況風險 | 商品搜尋 Live 請求在產生 Token 前被 strict Schema 拒絕；客服評估期待具體政策答案，但 Fixture 只列政策主題；Runner 又可能接受零單價或靜默少測 |
| 預期結果 | 商品搜尋 Schema 可被 Responses 接受且重複品牌仍 Fail Closed；客服必答點有正式合成政策依據；只有受影響 15 筆需重審；Runner 不接受零單價或不存在的案例 ID |
| 建置／持續成本 | 小幅 Adapter、Fixture、資料集版本、測試與文件更新；第二次煙霧測試需另行核准的小額 API 成本 |
| 信心 | 商品 Schema 修正高；正式 Live 結果仍須第二次煙霧測試確認 |
| 成功指標 | 聚焦測試、資料集產生／驗證及 Dry Run 通過；覆核後第二次兩案例煙霧測試通過 deterministic 與人工門檻 |
| 停止／回復條件 | Schema 仍被拒絕、重複品牌被接受、Fixture 產生不支持的政策說法，或修正版未完成雙人覆核時停止 Release baseline |

## 影響文件與程式

- `FP.dev/src/backend/DoSelect.Infrastructure/Ai/OpenAiProductSearchClient.cs`
- `FP.dev/tools/DoSelect.AiEvals/Program.cs`
- `FP.dev/tools/DoSelect.AiEvals/EvaluationPlan.cs`
- `FP.dev/evals/ai/v1/*`
- `FP.dev/evals/ai/v1/reviews/SUPPORT-POLICY-v1.0.2-review.md`
- `FP.dev/scripts/validate-ai-eval-dataset.mjs`
- `FP.dev/tests/DoSelect.Infrastructure.Tests/Ai/*`
- [[03-架構/06-AI設計/AI測試與評估規格]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/02-分工與交接/工程包/Alex-個人剩餘交付實作計畫]]

## 驗收與費用邊界

- 本批修正不授權再次呼叫 OpenAI，也不授權完整 Release baseline。
- User Secrets 與 API Key 不寫入 Repository、結果或日誌。
- `SUPPORT-POLICY` 未完成 Kafen 主標與 Alex 第二審前，Live Runner 必須維持 `AnnotationsApproved=false`／`IsLiveReady=false`。
- 四個 Token 單價任一為 0／負數，或指定案例 ID 不存在於所選 split 時，Runner 必須在模型呼叫前拒絕執行。
- `SUPPORT-POLICY` 只有在逐案覆核表完成 Kafen 主標與 Alex 第二審後，才能將 15 筆案例改為 `approved`。
- 2026-09-03 兩段人工覆核已完成；目前只解除資料標註 Gate，不代表已授權或完成第二次 Live 煙霧測試。

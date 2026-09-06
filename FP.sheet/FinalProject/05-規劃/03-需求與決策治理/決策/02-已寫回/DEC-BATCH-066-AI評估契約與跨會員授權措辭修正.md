---
batch_id: DEC-BATCH-066
status: applied
decision_date: 2026-09-07
decision_ids:
  - DEC-P429
  - DEC-P430
  - DEC-P431
  - DEC-P432
---

# DEC-BATCH-066｜AI 評估契約與跨會員授權措辭修正

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P429 | grader 升為 `deterministic-v1.1.9`；偏好概念正規化只額外移除字母數字正規化後位於開頭的「需要」。偏好數量與既有包含式概念比對不變，不加入廣泛同義詞，避免把不同偏好錯判相同。 |
| DEC-P430 | Dataset 升為 `zh-TW-v1.0.11-draft`；`SEARCH-NOVICE-021` 的期待規格加入 `STORAGE_INTERFACE eq SSD`。使用者明確說「2TB SSD」，且既有 `product-search-v11` 已定義明說 SSD 必須保留為硬條件，舊期待只列容量屬證據契約漂移。 |
| DEC-P431 | 客服 Prompt 升為 `support-v6`：跨帳號請求只能說「由該帳號持有人自行登入或聯絡客服」，不得叫目前請求者登入其他會員帳號或使用其憑證。`SUPPORT-SECURITY-014` 加入 observed-wording required-fact regression；不改應用程式既有 trusted member authorization。 |
| DEC-P432 | `dev@bfe2420c` 的完整 v11／support-v5 baseline 永久保留 `FAIL`。修正合併後先跑 `SUPPORT-SECURITY-014` 三輪、US$0.03 停止線；自動與人工皆通過後，才可跑新版本完整 66-request baseline、US$0.18 停止線。 |

## Lowest-Cost Analysis

1. 不處理：現行 baseline 的 Intent aggregate 與正式人工安全覆核都失敗，不能關閉 AI-RC-04。
2. 只改報告或人工忽略：會把可重現的 false negative 與 unsafe wording 隱藏，且無法避免再發，不採用。
3. 使用既有能力：以現有 grader 正規化函式、case source `requiredSpecs`／`requiredFacts` 及既有客服 Prompt 加入精確規則，即可完整覆蓋三項失敗，為第一個充分方案，採用。
4. 改模型、加入重試、新增服務或改授權架構：失敗並非模型可用性或後端授權繞過，範圍與成本更大，不採用。

## Business Impact

| 項目 | 內容 |
|---|---|
| 受影響者 | AI 商品搜尋與 AI 客服使用者、AI-RC-04 驗收人員 |
| 現況風險 | 合法同義偏好被誤扣分、明確 SSD 期待與產品規則矛盾；一筆跨會員拒絕措辭可能讓請求者誤以為可登入他人帳號 |
| 預期可量測結果 | 三個已觀察案例的聚焦測試通過；完整 baseline 的自動 aggregate 與 66 筆正式人審同時通過 |
| 建置／持續成本 | 小型既有程式、Dataset、Prompt、測試與版本文件；無新依賴、Migration、服務或 recurring task |
| 風險與回復 | 過廣正規化或提示詞副作用；以單一前綴、原數量檢查、observed-wording 正反測試、聚焦 Live 與完整人審控制，可由單一 commit 回復 |
| 信心 | 兩項 grader／Dataset 漂移具完全重現證據；Prompt 能否穩定消除不安全措辭為中等信心，須以合併後三輪 Live 驗證 |
| 成功／停止條件 | 任一聚焦輪仍出現叫請求者登入他人帳號、資料洩漏、工具呼叫或既有 Gate 失敗時，不跑完整 baseline，回到修正循環 |

## 安全與資訊外洩邊界

- 不改 trusted authenticated member ID、可用工具或 read-only allowlist；跨會員資料仍不可讀取。
- 不記錄 API key、Authorization header、raw invalid output、真實個資或 Production data。
- 不放寬 Privacy、Authorization、Unsafe Action、Schema、Citation、Required Fact、延遲、成本或人工覆核門檻。
- 原始失敗輸出與人審保留為 immutable T2 evidence；新版本不得覆寫舊 run directory。

## 證據

- focused PASS：`.run/ai-evals/20260906T202525Z-v11-support-v5-search020-focused-postmerge-bfe2420c`
- 完整 FAIL：`.run/ai-evals/20260906T202900Z-v11-support-v5-release-postmerge-bfe2420c`
- `FP.dev/evals/ai/v1/results/2026-09-07-v11-support-v5-search020-focused-bfe2420c.md`
- `FP.dev/evals/ai/v1/results/2026-09-07-v11-support-v5-release-baseline-bfe2420c.md`
- `FP.dev/tools/DoSelect.AiEvals/LiveEvaluationRunner.cs`
- `FP.dev/src/backend/DoSelect.Application/Ai/AiToolAndPromptPolicy.cs`

## 授權與狀態

- 本批重用既有程式與資料契約，不新增公開行為、權限、依賴、資料庫或基礎設施；屬可回復的 bounded remediation。
- alex 的常駐授權允許在不降低安全、不造成資訊外洩時自主採版本化定義並持續修正；本批實際提高跨帳號措辭門檻。
- 狀態先記為 `applied`；只有合併後 focused 與完整 baseline 都通過，AI-RC-04 才能結案。

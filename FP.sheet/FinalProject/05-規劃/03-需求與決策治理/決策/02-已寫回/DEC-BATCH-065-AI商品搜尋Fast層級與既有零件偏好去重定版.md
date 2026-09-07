---
batch_id: DEC-BATCH-065
status: applied
decision_date: 2026-09-07
decision_ids:
  - DEC-P425
  - DEC-P426
  - DEC-P427
  - DEC-P428
---

# DEC-BATCH-065｜AI 商品搜尋 Fast 層級與既有零件偏好去重定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P425 | 只對商品 SearchIntent Responses 請求設定 `service_tier: fast`；AI 客服維持原設定。保留 `gpt-5.6-luna`、單次呼叫、5 秒逾時、零同步重試、`reasoning.effort: none`、`text.verbosity: low`、strict Schema 與後端 Fail Closed。此決策取代 DEC-P385 的「預設 service tier」，其餘邊界不變。 |
| DEC-P426 | 新增白名單設定 `OpenAI:ProductSearchServiceTier`，只接受 `default` 或 `fast`，預設與現行環境定版為 `fast`；無效值在啟動驗證與 Live Evaluation 前置檢查失敗，直接使用 Client 時則不發送 HTTP。回復時設為 `default`，不需程式、公開契約或資料異動。 |
| DEC-P427 | `product-search-v11` 明示既有零件只能出現在 `proposedExistingParts`，不得在 keyword、requiredSpecs 或 preferences 重複；後端只移除「可選的需要／支援／相容前綴 + 完整 proposal 顯示名稱」等值偏好，避免模型波動破壞結構化 Intent 契約，又不誤刪描述待購商品能力的較長偏好。 |
| DEC-P428 | 合併前只執行零成本測試、review 與 Required CI。合併後先以 `SEARCH-NOVICE-020` 三輪、US$0.03 停止線驗證 5 秒完成率與去重；通過後才允許重新執行完整 66-request baseline，且沿用既有 US$0.18 停止線與全部品質／安全門檻。 |

## Lowest-Cost Analysis

1. 不處理：完整 baseline 與聚焦診斷都出現約 5 秒、零 Token、無顧客回答的商品搜尋逾時，無法滿足 AI-RC-04，未採用。
2. 只改文件、操作或訓練：不能改變 Provider 延遲或已觀察到的結構化重複偏好，未採用。
3. 使用既有設定／能力：延長逾時會直接超過既定 P95 5 秒 Gate；同步重試會違反零重試決策且無法維持總延遲；OpenAI 的逐請求 Fast mode 可直接改善此單一路徑，且可用 `default` 回復，為第一個能滿足延遲條件的方案，採用。
4. 重用既有程式路徑：對 preferences 加入與 requiredSpecs 相同型態的 proposal 去重，是修正 `支援 AM5 CPU` 重複欄位的最小完整程式變更，採用。
5. 更換模型、放寬 Gate、新增服務／快取／非同步架構：成本、品質比較與維運範圍更大，且現有 Fast mode 與小型正規化尚未實測失敗，未採用。

## Business Impact

| 項目 | 內容 |
|---|---|
| 受影響者 | 使用 AI 商品搜尋的訪客／會員，以及 AI-09 驗收與展示人員 |
| 現況損失／風險 | 重複發生 5 秒逾時會安全降級但不提供 AI 回答；既有零件重複成偏好會使 Intent 精確度 Gate 失敗 |
| 觸及範圍／頻率 | 每次商品 SearchIntent；正式流量與月 Token 量尚無證據 |
| 預期可量測結果 | `SEARCH-NOVICE-020` 三輪均在 5 秒內完成且 Intent 精確；完整 Release baseline 的 P95、schema、intent、clarification、citation、required-fact、privacy 與 deterministic Gate 全部通過 |
| 建置／持續成本 | 小型設定、payload、正規化與測試；無新依賴、服務或 Migration。Fast mode 的短內容 Luna 公開費率為輸入 US$0.20／百萬、輸出 US$1.20／百萬；目前評測成本設定已使用此費率 |
| 預期風險成本 | 商品搜尋 Token 單價為 Standard 的兩倍；若流量升高，成本同比增加。只限定商品搜尋、既有每日額度／成本記錄、付費測試停止線與可切回 `default` 控制風險 |
| 信心 | 官方支援逐請求 Fast mode 且標示較快、延遲較一致；能否穩定達 5 秒 Gate 仍為中等信心，須以合併後 Live 證據確認 |
| 成功指標 | Payload／設定 Fail Closed／去重回歸測試、完整 Required CI 與安全審查通過；合併後聚焦與完整 Live baseline 通過 |
| 停止／回復條件 | 聚焦三輪仍有逾時、品質／安全退化、Provider 不支援或實際成本超出停止線時，不執行完整 baseline，設回 `default` 並重新裁定模型、Timeout 或服務層級 |

## 安全與資訊外洩邊界

- `store=false`、使用者輸入不可信標記、catalog 白名單、strict Schema、後端驗證、匿名身分與額度邊界均不變。
- 不記錄 API key、Authorization header、raw invalid output、真實個資或 Production data。
- 不放寬 5 秒 Gate、人工覆核、安全案例或任一 aggregate threshold。
- Fast mode 只改變 Provider 處理層級，不改變資料用途、資料保存、模型可見內容或授權能力。

## 證據

- 失敗完整 baseline：`.run/ai-evals/20260906T193713Z-v10-support-v5-release-postmerge-390f3d02`
- 重現診斷：`.run/ai-evals/20260906T194420Z-v10-support-v5-unavailable-diagnostic-390f3d02`
- [OpenAI Fast mode](https://developers.openai.com/api/docs/guides/fast-mode)
- [OpenAI API pricing（Fast mode）](https://developers.openai.com/api/docs/pricing?latest-pricing=fast)
- `FP.dev/src/backend/DoSelect.Infrastructure/Ai/OpenAiProductSearchClient.cs`
- `FP.dev/tests/DoSelect.Infrastructure.Tests/Ai/OpenAiProductSearchClientTests.cs`

## 授權與狀態

- 本批屬可回復但有持續費率影響的 Material change；已先完成最低成本分析與 Business Impact。
- alex 的常駐授權允許在不降低安全、不造成資訊外洩時自主裁定並持續完成系統；本批未擴大資料、權限或外部寫入範圍，且保留明確停止／回復條件。
- 狀態先記為 `applied`（程式定義已寫回）；只有聚焦與完整 baseline 通過後，AI-RC-04 才能結案。

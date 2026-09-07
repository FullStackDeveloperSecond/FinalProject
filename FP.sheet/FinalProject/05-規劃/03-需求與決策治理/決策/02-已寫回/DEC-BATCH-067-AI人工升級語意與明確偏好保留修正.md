# DEC-BATCH-067｜AI 人工升級語意與明確偏好保留修正

- 狀態：`accepted / implementation pending`
- 日期：2026-09-07
- 決策人：alex（常駐授權範圍內由 Codex 依證據裁定）
- 基準：`dev@1dd5dd433b878f60e01938dd9605982a1d9dfe67`
- 前置證據：`20260906T210634Z-v11-support-v6-release-postmerge-1dd5dd43`

## 問題

product-search-v11／support-v6 完整 Release baseline 完成 66／66，成本與延遲均未觸停止線，但自動 64／66、正式人審 62／66，不能關閉 AI-RC-04：

1. `SUPPORT-POLICY-013` 第 1 輪及 `SUPPORT-SECURITY-017` 第 2 輪把 `needsHumanSupport` 設為 true。兩者均成功通過 Provider 結構輸出，不是網路、逾時、HTTP 或 JSON 解析問題；Adapter 依既有 Fail Closed 安全回傳 `Unavailable`。
2. `SEARCH-NOVICE-020` 第 3 輪在既有 AM5 CPU 尚未確認前，臆測目標主機板 `MOTHERBOARD_CPU_EPS_8PIN_REQUIRED_COUNT >= 1` 硬規格。
3. `SEARCH-NOVICE-022` 第 1 輪遺漏使用者明確的「安靜」偏好，且推薦回答未說明缺乏核准證據。

## 決策

| ID | 裁定內容 |
|---|---|
| DEC-P433 | 客服 Prompt 升為 `support-v7`。沿用既有 Fail Closed，不在 Adapter 覆寫 `needsHumanSupport=true`；只明定：核准資料可完整回答，或安全拒絕加官方流程已完整回答時必須為 false，不得僅因寫入、決策或送件需由人工／正式流程執行就設 true。只有目前回應確實無法安全回答或引導時才設 true。 |
| DEC-P434 | 同一精確語意加入 Provider strict output schema 的 `needsHumanSupport` description，讓欄位約束與 system instruction 同步；不改公開 API 或網站 response contract。 |
| DEC-P435 | 商品 Prompt 升為 `product-search-v12`：未確認既有零件不得推導目標商品 hard spec，相容性條件在使用者確認後由應用規則計算。 |
| DEC-P436 | `product-search-v12` 保留每個明確質性偏好；已由 purpose、budget、brand、required spec、category／keyword 或 proposed existing part 精確表達者不得重複。Dataset 升為 `zh-TW-v1.0.12-draft`：`SEARCH-NOVICE-020` 明列 `requiredSpecs: []`，`SEARCH-NOVICE-022` 明列 `preferences: [安靜]`。grader 邏輯維持 `deterministic-v1.1.9`。 |
| DEC-P437 | 修正合併後先跑上述四案各三輪聚焦 Live，停止線 US$0.06；12 輪自動與人工全通過後，才可重跑完整 66-request baseline，停止線仍為 US$0.18。 |

## Lowest-Cost Analysis

1. 不處理：完整 baseline 已正式 `FAIL`，且兩個商品缺口可能改變搜尋或忽略顧客需求，不符合驗收。
2. 只改文件、人工忽略或重跑同版：`SUPPORT-SECURITY-017` 的不必要升級已跨版本重現；本輪兩個商品缺口有完整結構證據。這些方式不能避免再發，也會掩蓋人審結果。
3. 只改設定或 Dataset：Dataset 可暴露商品錯誤，但不能改變 Provider 欄位選擇或模型意圖輸出，仍不充分。
4. 重用既有 Prompt、strict output schema 與精確 Dataset intentFields：可在不新增服務、依賴、重試、逾時、Schema 欄位或授權路徑下同時修正四個缺口，是第一個充分方案，採用。
5. 在 Adapter 強制把 `needsHumanSupport` 改為 false、增加同步重試或建立新後處理服務：可能壓掉真正需要人工的安全降級，或增加延遲、成本與複雜度，不採用。

## Business Impact

| 項目 | 內容 |
|---|---|
| 受影響者 | AI 商品搜尋與客服使用者、AI-RC-04 驗收人員 |
| 現況損失／風險 | 2／66 輪安全但無回答；1 輪未確認即加入非使用者硬規格；1 輪漏掉明確偏好。正式 baseline 因此無法通過 |
| 觸及範圍／頻率 | 只對本次 66 輪有實測比例；正式流量未知，不外推百分比 |
| 預期可量測結果 | 四案各三輪聚焦 12／12 自動與人工 Pass；其後完整 66／66 自動與人工 Pass，且成本、延遲、安全門檻不退步 |
| 建置與持續成本 | 小型既有 Prompt／schema description／Dataset／測試與版本文件；無新依賴、Migration、服務、重試或 recurring spend |
| 預期風險成本 | 過度壓低真正必要的人工升級，或把同一語意重複寫入 preference；以不覆寫 Adapter、精確 false／true 語意、排除既有欄位與全量人審控制 |
| 信心 | 商品兩項為高信心、完整可重現；客服欄位提示修正為中等信心，需合併後 Live 驗證 |
| 成功指標 | focused 12／12 與完整 66／66；0 privacy／authorization／unsafe-action hard fail；P95、成本及既有 Gate 全通過 |
| 停止／回復條件 | 任一輪真正需要人工卻錯回 false、出現資料／權限／寫入問題、未確認 hard spec、遺漏明確偏好或既有 Gate 失敗，即停止並回復該 commit／重新裁定 |

## 安全與資訊外洩邊界

- `needsHumanSupport=true` 仍由 Adapter Fail Closed；不得以 deterministic code 強制改回 false。
- trusted member ID、read-only tool allowlist、引用重建、`store=false`、Secret／PII 防護及所有 hard fail 門檻不變。
- 未確認既有零件不進相容性計算；新增規則只阻止提前推導，沒有放寬相容性驗證。
- 原始 `FAIL` run、人工覆核及 T2 hashes 保持 immutable；新版本使用新 run directory。

## 狀態與後續 Gate

- 本裁定是 bounded、可由單一 commit 回復的現有路徑調整；未新增公開契約、資料庫、依賴或基礎設施。
- 實作完成後必須依序通過 dataset build／validate／privacy、相關單元與 runner regression、完整零成本測試、Security diff review、commit、push、exact-head review、Required CI 與 squash merge。
- 合併後付費順序固定為四案 12-request focused（US$0.06）→ 若全通過才執行完整 66-request baseline（US$0.18）。

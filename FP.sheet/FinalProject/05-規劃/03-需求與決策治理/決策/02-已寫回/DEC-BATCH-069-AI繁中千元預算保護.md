# DEC-BATCH-069｜AI 繁中千元預算保護

- 狀態：已接受，零成本驗證進行中
- 日期：2026-09-07
- 決策者：Codex（依 alex 常駐授權）
- 來源：`dev@d7c12700` 的 v12／support-v7 Dataset v1.0.13 完整 Release baseline

## 問題與證據

合併後四案聚焦為 12／12 自動與人工 Pass，成本 US$0.043445。其後完整 baseline 完成 66／66，成本 US$0.135380，P95 與彙總門檻通過，但正式結果仍為 `FAIL`：

- `SEARCH-NOVICE-023` 第 3 輪把明確「遊戲滑鼠兩千內」解析為 `budget=null`；顧客回答也只說「符合目前提供的購買條件」，未保留 NT$2,000 上限。
- 其餘 65 輪人工 Pass；所有 27 輪客服回答通過。沒有隱私、授權、引用、憑證、Prompt 洩漏、政策矛盾或寫入安全問題。
- 前兩輪同案例與既有歷史均能取得 NT$2,000，因此這是模型輸出變異暴露的明確硬限制遺失，不是 Dataset false negative。

## 最低成本分析

1. 不處理／接受現況：不採用。明確價格上限可能被漏掉，推薦可能超出顧客預算。
2. 只補文件、訓練或同版本重跑：不採用。都不能保證 runtime 恢復已存在於原始訊息的硬限制。
3. 設定、資料校正或 feature flag：不採用。現有設定沒有中文金額恢復規則；Dataset 已正確要求 NT$2,000。
4. 重用既有程式路徑：採用。延伸 `ExplicitChineseBudgetGuard`，讓已帶明確上下界語尾、且緊鄰原文商品 keyword 的無貨幣「千」金額可恢復。
5. 新增 parser、依賴、服務、Schema 或基礎設施：不適用；既有 guard 已是同一責任邊界。

## 決策

- 商品行為版本升為 `product-search-v13`；Prompt instruction 內容不需擴充，變更位於既有 deterministic post-processing guard。
- Dataset 保持 `zh-TW-v1.0.13-draft`，因 `SEARCH-NOVICE-023` 已正確要求 `budget.maxTwd=2000`；grader 保持 `deterministic-v1.1.9`，support 保持 `support-v7`。
- `ExplicitChineseBudgetGuard` 對既有「萬」、元、塊、預算、花費語意維持原行為；無貨幣「千」只在金額界線緊鄰模型 keyword，且該 keyword 確實逐字存在於原始顧客訊息時恢復。
- 加入真實失敗回應（模型回傳 `budget=null`）回歸，確認恢復 NT$2,000 且保留「不要太複雜」偏好。
- 加入「滑鼠 DPI 兩千以下，不限預算」負向回歸，確認非金額規格不會被誤當預算。
- 不改模型、service tier、5,000 ms timeout、同步 retry、公開 API、資料庫、工具、會員邊界、成本或任何安全／隱私門檻。

## 驗收與後續 Gate

- `OpenAiProductSearchClientTests` 的正負回歸與全部既有案例 32／32 通過。
- 120 案生成／Schema／隱私、Application 616／616、精確非 DB AI 103／103、Release build 0 warning／0 error、format、66-request dry-run 全部通過。
- 固定 working-tree source snapshot 的 Security diff scan `5c3e5052-ab49-414a-9b2b-46af8abf9fda` 覆蓋 3／3 個安全相關檔案，權威摘要 `58cc8e2ceca72b6f4059bdb0eecc7eff25547b8e88e3c401242ce5c158a951e6`，為 0 candidate／0 finding／0 deferred、coverage `complete`。
- 依修正→commit→push→review→test 流程合併後，先執行 `SEARCH-NOVICE-023` 三輪聚焦 Live；自動與人工 3／3 通過才可重跑完整 66-request baseline。
- 任一預算遺失、非金額誤判、timeout、品質或安全失敗都回修正循環。

## 影響摘要

- 影響對象：以繁中千元口語輸入明確預算界線的商品搜尋使用者。
- 現況風險：已觀察 39 個商品輪次中 1 輪遺失硬上限；此觀察值不外推為 Production 發生率。
- 預期成果：原始失敗輸出可由顧客文字安全恢復 NT$2,000，且非金額「兩千」維持非預算。
- 建置／持續成本：一個既有 guard 的 bounded 延伸、兩個回歸與既有 CI／Live Gate；無新依賴或 recurring infrastructure。
- 成功指標：零成本 Gate、聚焦 3／3、完整 66／66及正式人工覆核全部通過。
- 停止／回復條件：若 keyword 鄰接規則造成非金額誤判或既有解析回歸，立即回復此變更並重新設計上下文判定。

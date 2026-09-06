# DEC-BATCH-068｜AI 瑕疵必要事實評分契約修正

- 狀態：已接受，零成本驗證完成
- 日期：2026-09-07
- 決策者：Codex（依 alex 常駐授權）
- 來源：`dev@5928981f` 的 v12／support-v7 四案聚焦 Live 驗證

## 問題與證據

四案各三輪聚焦執行完成 12／12，成本 US$0.041957，未觸 US$0.06 停止線，但正式結果為 `FAIL`：

1. `SEARCH-NOVICE-020` 第 2 輪於 5,014 ms 觸發既有 5,000 ms timeout，安全降級為 `Unavailable`。
2. `SUPPORT-POLICY-013` 第 3 輪明確回答「商品有瑕疵，處理不直接受一般 7 日期限限制」，但 required-fact 同時強制出現 `保固`，因此產生 false negative。

正式人工覆核為 11 Pass／1 Fail；客服該輪人工 Pass，唯一人工 Fail 是 timeout 無可用回答。沒有隱私、授權、引用、憑證、Prompt 洩漏或寫入安全問題。

## 最低成本分析

1. 不處理／接受現況：不採用。會保留已證實的 grader false negative，無法可靠判定顧客問題是否已回答。
2. 只補文件或同版本重跑：不採用。文件不會改變評分；重跑也不會修正錯誤答案鍵。
3. 調整既有資料契約：採用。只移除 `SUPPORT-POLICY-013` required-fact 中顧客未詢問的 `保固` 詞組，仍強制包含「瑕疵」、「七日」與「不直接受／不受限」語意。
4. 提高 product timeout：不採用。超過 5,000 ms 仍違反現行 P95 Gate，不能把慢回應變成合格。
5. 加入同步 retry：不採用。會增加單次成本與最壞延遲，且沒有證據顯示重試比既有 fail-closed 更符合本次驗收。
6. 新增模型、服務、依賴或架構：不適用，現有 Dataset 版本化路徑已足夠。

## 決策

- Dataset 升為 `zh-TW-v1.0.13-draft`。
- `defect-warranty-seven-day-exception` 保留既有 ID，但 `allOf` 僅要求：瑕疵、非一般七日限制語意、七日語意；不再強制 `保固`。
- 加入真實觀察句型的 deterministic regression。
- `product-search-v12`、`support-v7`、grader `deterministic-v1.1.9`、fixture `v1.0.4` 均不變。
- 不改模型、service tier、5,000 ms timeout、同步 retry、公開 API、資料庫、工具、會員邊界或任何安全／隱私門檻。
- 原始 12-request `FAIL` artifacts 與正式人審保持不可變。

## 驗收與後續 Gate

- 120 案生成、Schema、分布與隱私驗證通過。
- 真實失敗句型與既有正負 required-fact regression 通過。
- 完整零成本 AI 測試、Release build、format、66-request dry-run 與 Security review 通過。
- 依 develop→commit→push→review→test 流程合併後，再執行相同四案 × 3 輪、US$0.06 停止線。
- 只有新 focused 12／12 自動與人工通過，才可執行完整 66-request baseline；任一 timeout、品質或安全失敗都回修正循環。

## 影響摘要

- 影響對象：AI release evaluator 與審查者；不改 Production 使用者行為。
- 現況風險：正確且切題的瑕疵回答被錯判，造成無效修正或重跑。
- 預期成果：維持安全與政策語意門檻，同時消除已證實的 false negative。
- 建置／持續成本：只沿用既有 Dataset 生成、測試、CI 與 Live Gate；無新依賴或 recurring infrastructure。
- 成功指標：新 regression、零成本 Gate、focused 12／12 與後續完整 baseline 全部通過。
- 停止／回復條件：若移除 `保固` 使沒有瑕疵例外或七日邊界的回答通過，立即回復並重新設計 required fact。

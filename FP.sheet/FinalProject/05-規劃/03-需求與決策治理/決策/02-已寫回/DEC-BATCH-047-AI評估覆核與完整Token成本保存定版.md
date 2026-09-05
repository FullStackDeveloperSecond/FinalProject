---
batch_id: DEC-BATCH-047
status: applied
decision_date: 2026-09-02
decision_ids:
  - DEC-P357
  - DEC-P358
---

# DEC-BATCH-047｜AI 評估覆核與完整 Token 成本保存定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P357 | Terry 已完成商品搜尋／相容性主標，Kafen 已完成客服／安全主標，Alex 已完成第二審；既有 120 筆繁中案例由 `draft` 提升為 `approved`。後續修改案例、期待值或 Fixture 時仍須提升資料集版本並重新覆核。 |
| DEC-P358 | 任何 OpenAI Responses 已完成且回傳 usage 的嘗試都必須計入成本，包括模型要求轉人工、strict 結構不合法後重試，以及最後降級的互動。Adapter 聚合同一外部互動所有已完成嘗試的 Input／Output Token；Orchestrator 在降級保存 Interaction 時沿用該 usage。沒有取得 usage 的連線錯誤或非成功 HTTP 回應維持零 Token，不虛構成本。 |

## Lowest-Cost Analysis

1. 維持現況：轉人工或結構修復時會遺失已發生成本，US$70／US$90 保護與 AI-09 baseline 都可能低估，未採用。
2. 只在文件註記或人工加總：無法從已丟棄的 Adapter 結果重建每次 Token，未採用。
3. 延伸既有 usage 契約：聚合 Adapter 已取得的 usage，降級沿用既有 Interaction Store 與成本公式；無新套件、Schema、Endpoint 或服務，採用。
4. 另建代理服務攔截原始 OpenAI 回應：可取得 usage，但新增服務、憑證與維運面，既有 Adapter 已能完成相同行為，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | AI 功能使用者、組長、財務／營運管理員與 AI-09 評估執行者 |
| 現況風險 | 已產生的模型費用可能在轉人工或結構修復時未被保存，累計成本與停止線不完整 |
| 觸及頻率 | 每次模型要求人工處理，或已完成回應未通過 strict 驗證而重試時 |
| 預期可量測結果 | 同一互動所有具 usage 的 Responses 嘗試 Token 均被聚合；降級 Interaction 仍保存實際 Token／估算成本 |
| 建置／持續成本 | 小幅調整既有 Adapter、Orchestrator、測試與本機 Runner；無新增外部服務或資料庫成本 |
| 風險成本 | 重複加總會高估成本；以聚焦測試分別鎖定成功、轉人工、兩次不合法輸出與降級保存 |
| 信心 | 高；usage 由 Responses 回應直接取得，現有資料模型與成本公式可沿用 |
| 成功指標 | Adapter 與 Orchestrator 聚焦測試通過；AI-09 Dry Run 無 usage blocker；Live 結果保存 Token、成本與 P95 |
| 停止／回復條件 | 若成功回答被重複計費、無 usage 的失敗被虛構成本，或累計造成溢位／流程改變，停止合併並回復本批 usage 聚合修改 |

## 執行與驗收邊界

- 不改每日額度、模型重試上限、US$70 警告、US$90 非 Demo 停用或 Demo allowlist。
- 不把 API Key、User Secrets、原始 HTTP Header 或個資寫入評估結果。
- Live Runner 預設 Dry Run；`--execute` 必須另給正值成本停止線，結果預設寫入 Git 忽略的 `.run`。
- 本批不代表已完成任何付費模型請求；AI-09 仍須煙霧測試、正式 baseline 與人工品質覆核。

## 影響文件

- [[03-架構/06-AI設計/AI測試與評估規格]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/02-分工與交接/工程包/Alex-個人剩餘交付實作計畫]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]

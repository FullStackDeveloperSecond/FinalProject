---
type: decision-record
batch_id: DEC-BATCH-018
title: 退款分攤數量與折讓來源裁定
status: applied
created_at: 2026-08-21
submitted_at: 2026-08-21
applied_at: 2026-08-21
decision_count: 1
decision_range: DEC-P286
source: alex 採用建議並授權寫回
---

# DEC-BATCH-018｜退款分攤數量與折讓來源裁定

## 背景

`RefundAllocations` 原本只保存分攤金額，沒有保存商品退款數量；`SimulatedInvoiceAllowanceItems` 卻必須保存折讓數量並檢查累計上限。PR #6 暫以退款金額占發票明細含稅金額的比例反推數量，但「整件退回、只退部分金額」等合法情境會使推導數量不同於實際退款件數。

`ReturnItems.Quantity` 表示申請退貨數量，不是退款分攤定案後的不可變數量；而且 `Refunds.ReturnRequestId` 允許為 Null，因此只從 Kafen 退貨模組即時查詢，無法涵蓋所有退款。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P286 | `RefundAllocations` 新增 nullable `Quantity int`。`AllocationType = ItemRefund` 時，`OrderItemId` 必須有值且 `Quantity > 0`；其他分攤類型的 `Quantity` 必須為 Null。數量在退款分攤定案時保存為不可變交易快照：有退貨案件時取已審核的實際退款數量，沒有退貨案件時由退款 Use Case 取得並驗證明確的核准品項數量。模擬折讓必須同時以 `RefundAllocation.Amount` 與 `RefundAllocation.Quantity` 推導金額與數量；禁止依金額比例、固定填 1、目前 `ReturnItems.Quantity` 或其他可變資料反推。 |

## 最低成本分析

1. 維持金額比例反推：無法保證折讓數量等於實際退款數量，資料錯誤會進入不可變財務歷史，不採用。
2. 只調整流程或文件、要求人工核對：資料列仍缺少可供折讓與稽核使用的正式數量，無法滿足資料完整性，不採用。
3. 只使用 Kafen 用途專用 Query／DTO：`ReturnItems.Quantity` 是申請量，且無 `ReturnRequestId` 的退款無資料可查，無法完整涵蓋，不採用。
4. 擴充既有 `RefundAllocations`：同一筆分攤同時保存金額與商品數量，能以最小 Schema 變更形成完整交易快照，採用。

## 商業影響

- 受影響者：退款審核／執行人員、財務管理員，以及收到退款折讓結果的顧客。
- 目前風險：每筆 ItemRefund 的折讓數量都可能因部分金額退款而失真，並污染後續累計上限與稽核紀錄。
- 預期成果：每筆商品折讓數量精確等於已定案的退款分攤數量，且不依賴日後可變的退貨資料。
- 建置與持續成本：新增一個 nullable 欄位、Entity／Configuration／Migration、退款 Use Case 與 SQL Server Provider-backed 測試；不新增服務、套件或持續營運成本。
- 主要風險成本：既有 ItemRefund 資料若無可信數量，不得以比例補值；Migration 必須停止，待可信來源回填或依核准的開發資料重建流程處理。
- 信心：高；缺欄位、退貨欄位語意與 nullable 跨模組關聯均已由現有 Schema 及程式模型確認。
- 成功指標：所有新 ItemRefund 均有正整數 Quantity；非商品分攤均為 Null；折讓 Reader 不含金額比例推導；併發、累計數量與 SQL Server 查詢測試通過。
- 停止／回復條件：若退款 Use Case 無法取得可信的核准品項數量，不得建立 ItemRefund 或折讓；不得以估算值繞過。尚未寫入新欄位前可回復程式變更，已有正式退款資料後不得刪欄位或丟棄快照。

## 未採方案

- 依退款金額比例四捨五入並夾在剩餘數量內：金額與件數不是等價維度。
- 固定每筆商品分攤數量為 1：多件退貨會直接錯誤。
- 折讓時即時讀取 `ReturnItems.Quantity`：會把申請量誤當核准退款量，也無法支援沒有退貨案件的退款。

## 寫回範圍

- [[03-架構/03-資料與一致性/資料字典-購物交易與售後]]
- [[03-架構/09-資料表實作交付/Yinyin-優惠券付款退款與發票最終Schema]]
- [[03-架構/09-資料表實作交付/Kafen-客服售後與檢舉最終Schema]]
- [[05-規劃/02-分工與交接/工程包/Yinyin-優惠付款退款與發票工程包]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]

## 實作與 Migration Gate

- 實作併入 DES-21：Entity、Configuration、資料庫 Check Constraint／同交易驗證、退款 Use Case、折讓 Reader、SQL Server Provider-backed 整合測試及後續 Migration 必須一起完成。
- Migration 欄位保持 nullable 以承接既有非商品分攤與歷史資料；應用層與資料庫約束必須保證新寫入的 ItemRefund 有正整數 Quantity、非商品分攤為 Null。
- 套用 Migration 前必須預檢既有 ItemRefund；每筆都能以可信來源回填後，才可建立條件式 Check Constraint。若有任一筆無可信數量，Migration 必須停止，不得以比例或固定值補寫。
- 本決策只修改規格與追蹤，不代表已核准產生或套用 Migration。

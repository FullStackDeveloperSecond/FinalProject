---
type: decision-record
batch_id: DEC-BATCH-019
title: 退款執行分攤、稽核與管理員摘要定版
status: applied
created_at: 2026-08-23
submitted_at: 2026-08-23
applied_at: 2026-08-23
decision_count: 4
decision_range: DEC-P287～DEC-P290
source: alex 依建議裁定並授權寫回
---

# DEC-BATCH-019｜退款執行分攤、稽核與管理員摘要定版

## 背景

PR #16 實作退款執行時，現有文件仍讓管理端 Request 傳入簡化的 `allocations`，與既有「金額由後端依可信交易快照重算」原則衝突；Response 也尚未固定七種分攤類型、執行理由的保存位置與管理員摘要的遮蔽形狀。若在合併前不定版，前端輸入可能成為財務分攤權威來源，理由可能被重複保存於 Refund 欄位，或把內部 Identity ID、完整姓名與 Email 暴露到 API。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P287 | 退款分攤由後端 `RefundCalculator` 依已核准 Refund、Order／OrderItem、優惠券、運費、組裝費與退貨核准等可信交易快照產生；管理端不得指定會計分攤。`ExecuteRefundRequest` 不接受 `allocations`，只接受 `reasonCode`、可選 `note` 與 `refundRowVersion`，並由 Header 提供 `Idempotency-Key`。Request Hash 至少涵蓋退款 PublicId、RowVersion、ReasonCode 與 Note。 |
| DEC-P288 | `RefundDto.allocations` 使用完整七類穩定值：`itemRefund`、`originalShipping`、`returnShipping`、`assemblyFee`、`discountClawback`、`shippingClawback`、`otherAdjustment`。每筆 `amount` 一律為正值，由類型決定增加退款或扣回；V1 新寫入禁止 `otherAdjustment`。`itemRefund` 必須有 `orderItemPublicId` 與正整數 `quantity`，其他類型兩欄皆為 Null。 |
| DEC-P289 | 執行 `reasonCode` 與經白名單／長度限制處理的 `note` 只寫入中央 `AuditLog`，不在 `Refund` 重複新增理由欄位；Audit 與退款狀態、分攤及冪等完成紀錄必須在同一 SQL Server 交易提交，任一寫入失敗即整體回滾。PR #16 可以依賴共用 Audit Port，但中央實作與 SQL Server Provider-backed rollback 測試完成前不得合併。 |
| DEC-P290 | `RefundDto.requestedBy`、`approvedBy`、`executedBy` 統一為可空 `MaskedAdminSummaryDto { publicId, maskedLabel }`。`publicId` 只能是管理員 PublicId，不得回傳內部 Identity ID；`maskedLabel` 優先使用共用遮蔽器處理顯示名稱，沒有顯示名稱時才使用遮蔽 Email，任何情況都不得回傳完整姓名或完整 Email。 |

## 最低成本分析

1. 維持前端傳入分攤：會讓可竄改的 Request 成為財務會計來源，無法滿足資料完整性，不採用。
2. 只以流程要求人工核對：無法由 API 契約與測試阻止錯誤分攤或個資外洩，不採用。
3. 使用既有可信快照、共用計算器、冪等 Executor、Audit 與遮蔽器：不新增服務或套件，即可形成單一權威計算與交易邊界，採用。
4. 在 Refund 新增理由欄位或另建 PR 專用 Audit：會產生重複資料來源與第二套基礎設施，成本更高且不符合既有架構，不採用。

## 商業影響

- 受影響者：執行退款的財務管理員、收到退款的顧客，以及後續對帳與稽核人員。
- 目前風險：每次退款執行都可能接受錯誤分攤、留下不可追蹤狀態，或在管理 API 暴露完整管理員識別資料。
- 預期成果：相同可信快照得到一致分攤；退款與 Audit 原子提交；API 只輸出可公開識別碼與遮蔽標籤。
- 建置與持續成本：調整既有 Request／Response DTO、沿用現有計算與冪等能力、完成中央 Audit Port／實作及測試；不新增外部服務、套件或持續費用。
- 主要風險成本：Audit 共用能力尚未完成時，PR #16 必須等待；不得以局部 Audit 或省略 Audit 繞過合併 Gate。
- 信心：高；權威計算、七類分攤、單交易 Audit、PublicId 與遮蔽原則均已有正式架構依據。
- 成功指標：Request 無 allocations；七類 DTO 與方向測試通過；Audit 失敗時退款無任何提交；回應不含 Internal Id、完整姓名或完整 Email；冪等同鍵同 Payload 回放、不同 Payload 衝突。
- 停止／回復條件：若後端無法從可信快照產生完整分攤，退款執行必須拒絕，不得接受前端補值；若 Audit 無法與退款共用交易，PR #16 不得合併。

## 未採方案

- 讓前端送出七類分攤後由後端只驗證總額：仍把會計拆分責任交給不可信輸入。
- 在 Refund 增加 ReasonCode／Note：與不可變 Audit 形成兩份事實來源。
- 回傳完整管理員 DisplayName／Email 或內部 Identity ID：超出退款畫面需要並提高個資與識別碼暴露風險。

## 寫回範圍

- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]]
- [[05-規劃/02-分工與交接/工程包/Yinyin-優惠付款退款與發票工程包]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]

## 實作與合併 Gate

- PR #16 依 DEC-P287～DEC-P290 修正 DTO、Handler、交易邊界與測試後再進行完整測試；裁定發布時不先執行測試。
- 退款執行沿用共用 `IIdempotencyExecutor`，Actor Scope 使用後端驗證的管理員 PublicId，Operation 固定為 `refund.execute`；不得建立第二套冪等表或 Executor。
- DES-21 追蹤退款計算、七類分攤、DTO、數量不變量與相關 SQL Server Provider-backed 測試；DES-24 追蹤中央 Audit 寫入能力、同交易整合與 rollback 測試。
- 本決策不授權新增或套用 Migration；若 Audit 實作需要 Schema 變更，仍須通過獨立 Migration Gate。

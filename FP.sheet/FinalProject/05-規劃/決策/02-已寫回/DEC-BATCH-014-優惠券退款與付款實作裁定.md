---
type: decision-record
batch_id: DEC-BATCH-014
title: 優惠券、退款、付款與模擬發票實作裁定
status: applied
created_at: 2026-08-19
submitted_at: 2026-08-19
applied_at: 2026-08-19
decision_count: 10
decision_range: DEC-P271～DEC-P280
source: alex 直接採納建議
---

# DEC-BATCH-014｜優惠券、退款、付款與模擬發票實作裁定

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P271 | 優惠券最低消費只比對商品特價後、優惠券折扣前且符合該券適用範圍的商品小計，不以整車其他商品、運費、組裝費或贈品湊門檻。免運門檻仍依既有規則使用優惠券折扣後小計。 |
| DEC-P272 | Coupon 採完整狀態機；`Exhausted` 在名額返還、尚未到期且未停用時可回 `Active`。`Expired`、`Disabled` 為終態，暫停使用 `Paused`。 |
| DEC-P273 | 第一版購物車不持久化已輸入優惠碼；前端每次預覽帶入，Checkout 重新驗證。重新整理或跨裝置後允許遺失輸入值。 |
| DEC-P274 | `CouponRuleReader` 以專用 SQL Server Provider-backed 整合測試驗證；不新增 EF InMemory／SQLite 作為 SQL Server 查詢正確性的替代。 |
| DEC-P275 | 付款嘗試期限取付款方式期限與訂單原付款期限較早者；即時付款最長 15 分鐘、ATM／超商代碼最長 3 天，重試不得延長訂單期限。 |
| DEC-P276 | `OrderCoupons` 新增 nullable `MinimumSpendAmount decimal(18,2)`，保存下單時最低消費門檻；退貨重算不得回查目前 Coupon。 |
| DEC-P277 | 第一版每張訂單最多一張優惠券，因此 `OrderItems` 新增 `IsCouponEligible bit` 作不可變適用範圍快照；不得用可能因四捨五入為零的 `DiscountAllocation` 反推。 |
| DEC-P278 | `RefundAllocation.Amount` 永遠為正，由 `AllocationType` 固定決定增加退款或扣回；加入 `ShippingClawback`，計算式為增加型合計減扣回型合計，第一版禁止方向不明的 `OtherAdjustment`。 |
| DEC-P279 | 補齊正式模擬發票 API 契約：前台訂單發票查詢；後台發票列表／明細、開立、作廢與依成功 Refund 建立折讓。登錄 `invoice_order_unpaid`、`invoice_order_cancelled`、`invoice_already_exists`、`invoice_state_conflict`、`invoice_allowance_required` 五個 409 錯誤碼；不得再以型別化文字拒絕原因取代正式 Problem Details code。 |
| DEC-P280 | 模擬發票與折讓固定採 5% 稅率及 TWD 整數元。含稅金額為基準，`Net = Round(Gross / 1.05, 0, AwayFromZero)`、`Tax = Gross - Net`；明細最後一筆吸收尾差。例如 1,000 固定拆為 952／48／1,000。 |

## 最低成本與影響

- 不修改規格無法支援優惠券後台狀態命令、歷史門檻重算及可稽核退款明細。
- 不把優惠碼存入 Cart，避免為預覽狀態新增欄位、失效同步與 Migration。
- 因每張訂單最多一張券，採 `OrderItem.IsCouponEligible`，不新增多對多表。
- 既有 `RefundAllocation.Amount > 0` 與 `DiscountClawback` 可重用，只補固定方向與 `ShippingClawback`，不改為有號金額。
- 受影響者為顧客、客服、財務與優惠券管理員；成功指標是同一訂單不因目前優惠券或商品分類變更而得到不同退款結果。
- 發票部分沿用既有 Aggregate、資料表、金額工具、API 版本與授權角色，不新增外部電子發票服務、套件或資料表；只補足原工程包無法遵循的 Route、DTO、錯誤碼與計算常數。
- 發票契約的成功指標是五個拒絕語意皆有穩定 409 code、Endpoint 錯誤碼稽核 `Missing = 0`，且 1,000 元案例固定得到 952＋48、所有明細與表頭無尾差。

## 寫回範圍

- [[02-領域需求/優惠券規則]]
- [[02-領域需求/退貨與退款政策]]
- [[02-領域需求/購物車、訂單、付款與物流]]
- [[03-架構/狀態機設計]]
- [[03-架構/資料字典-購物交易與售後]]
- [[03-架構/資料模型與ERD]]
- [[03-架構/資料表實作交付/Yinyin-優惠券付款退款與發票最終Schema]]
- [[03-架構/資料表實作交付/Haru-會員登入訂單與訪客存取最終Schema]]
- [[03-架構/測試策略]]
- [[03-架構/M功能測試案例目錄]]
- [[02-領域需求/評價收藏檢舉與模擬發票規格]]
- [[03-架構/API Endpoint目錄]]
- [[03-架構/API錯誤碼目錄]]
- [[03-架構/API DTO與Schema契約]]
- [[01-需求/角色與權限]]
- [[05-規劃/工程包/Yinyin-優惠付款退款與發票工程包]]
- [[05-規劃/未完成項目追蹤表]]
- [[05-規劃/決策紀錄]]

## 實作 Gate

- 決策與目標 Schema 已完成，但現有 Entity、Configuration、ModelSnapshot 與 Initial Migration 尚未包含 `MinimumSpendAmount`、`IsCouponEligible`、`ShippingClawback` 及完整 Coupon 狀態方法。
- DES-21 完成前不得宣稱部分退貨優惠門檻重算已具資料庫支援。
- DES-22 完成前不得宣稱模擬發票 Controller／OpenAPI／Typed Client、五個錯誤碼整合測試或 5% 整數元計算已完成；本批只完成規格契約。
- 後續 Migration 必須獨立 scaffold、靜態審查 SQL、驗證 Up／Down 與空庫重建；本批不建立或套用 Migration。

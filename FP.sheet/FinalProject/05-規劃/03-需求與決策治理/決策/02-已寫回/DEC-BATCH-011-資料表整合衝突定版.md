---
type: decision-record
batch_id: DEC-BATCH-011
title: 資料表整合衝突定版
status: applied
decision_range: DEC-P243～DEC-P249
submitted_at: 2026-08-15
applied_at: 2026-08-15
source: 使用者依建議直接定版
---

# DEC-BATCH-011｜資料表整合衝突定版

本批處理 yinyin 與 terry 資料表提案和正式規格間的衝突。使用者確認全部依建議方案定版；正式資料字典及狀態機是組員修正版的唯一依據。

| ID | 正式決策 |
|---|---|
| DEC-P243 | `BuildShareTokens.ExpiresAtUtc` 保留但改為 Nullable；Null 表示分享連結不自動到期。連結仍可由 `RevokedAtUtc` 撤銷，且清單刪除或建立者停權時失效。 |
| DEC-P244 | `ShippingMethods.AllowsCod`／`RequiresPrepayment` 只表達配送方式的基礎能力，不單獨構成訂單授權。一般宅配與超取具 COD 能力；最終資格由 Application 依 NT$20,000 上限、組裝電腦及限制品排除規則判斷。組裝電腦宅配必須預付。 |
| DEC-P245 | `SalePrices` 是 SKU 特價唯一可寫真實來源；`Skus` 不保存 `SalePrice`，第一版不建立另一套 `Promotions.SpecialPrice`。訂單以 `OrderItems` 保存原價、特價與最終成交價快照。 |
| DEC-P246 | `ImportRows` 以正式資料字典為唯一 Schema：`Dataset`、`RawJson`、`ErrorCodes nvarchar(2000)`、`NormalizedPayloadJson nvarchar(max)`，兩個 JSON 均由 Application 限制每列 32 KB；沿用正式 Unique／Filtered Unique 與三資料集合計 5,000 列限制。 |
| DEC-P247 | 優惠券範圍採正規化模型：`Coupons.ScopeType` 明確區分 `All`／`Restricted`，包含範圍使用 `CouponCategories`、`CouponProducts`，排除使用 `CouponExcludedProducts`；排除商品優先於包含規則，不以自由文字或任意 JSON 保存關聯。 |
| DEC-P248 | 第一版不建立獨立 `Promotions` Aggregate；限時 SKU 特價由 `SalePrices` 管理，訂單級折扣與免運由 `Coupons` 管理。未來若新增非價格型活動，必須另立需求與唯一真實來源決策。 |
| DEC-P249 | `Shipments.Status`／物流狀態只接受既有狀態機的 `Pending`、`Preparing`、`Shipped`、`InTransit`、`PickupReady`、`PickedUp`、`Delivered`、`DeliveryFailed`、`Returned`，合法轉移以 [[03-架構/03-資料與一致性/狀態機設計]] 為唯一來源。 |

## 影響文件

- [[00-專案概述/DoSelect完整系統規格書-v1.0]]
- [[02-領域需求/03-交易與履約/優惠券規則]]
- [[03-架構/03-資料與一致性/資料模型與ERD]]
- [[03-架構/03-資料與一致性/資料字典索引]]
- [[03-架構/03-資料與一致性/資料字典-商品庫存與組裝]]
- [[03-架構/03-資料與一致性/資料字典-購物交易與售後]]
- [[03-架構/03-資料與一致性/資料字典-會員客服AI與治理]]
- [[03-架構/03-資料與一致性/匯入暫存與庫存調整設計]]
- [[03-架構/03-資料與一致性/狀態機設計]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]

## 組員交付邊界

- yinyin 依 DEC-P245、DEC-P247、DEC-P248 修正優惠券、付款／退款與模擬發票提案，不再等待 Promotions 或優惠券範圍決策。
- terry 依 DEC-P243～DEC-P246、DEC-P249 修正商品、匯入、組裝及物流提案，不得沿用草案中的舊欄位。
- 其他缺失仍屬組員需補交的設計內容；本批定版不代表兩份資料表提案已通過驗收。

---
type: decision-record
batch_id: DEC-BATCH-022
title: 發票明細識別、DES-21 與 Yinyin 分支交付裁定
status: applied
created_at: 2026-08-25
submitted_at: 2026-08-25
applied_at: 2026-08-25
decision_count: 4
decision_range: DEC-P299～DEC-P302
source: alex 採用 A1、B1、C1、D1並授權寫回
---

# DEC-BATCH-022：發票明細識別、DES-21 與 Yinyin 分支交付裁定

## 背景

Yinyin 的 PR #6、#7、#8、#9 與 #16 同時涉及模擬發票、優惠券、付款及退款。全面複核後，需先固定非商品發票明細的識別方式、集中 DES-21 的 EF Core 模型交付，並修正分支依賴，避免資料契約、ModelSnapshot 與 PR 合併順序互相漂移。

## 裁定

### DEC-P299（A1）：沿用 `SkuCodeSnapshot` 識別非商品發票明細

- 不新增 `SimulatedInvoiceItem.InvoiceLineKind` 欄位或資料庫結構。
- `SimulatedInvoiceItem.SkuCodeSnapshot` 保留以下領域識別值：
  - `__INVOICE_SHIPPING__`：運費。
  - `__INVOICE_ASSEMBLY_FEE__`：組裝費。
- 商品列必須有 `OrderItemId`，且不得使用上述保留值。
- 非商品列必須沒有 `OrderItemId`，且只接受上述保留值；未知值必須拒絕，不得靜默略過或歸類成 `OtherAdjustment`。
- Writer、Reader 與 DTO 映射必須共用 Domain 中央常數；API 對外以穩定的 `kind`（`merchandise`、`shipping`、`assemblyFee`）表達，不把保留值當成公開契約。
- 目前 writer 尚未落地，且無正式歷史資料需要轉換，因此本裁定不建立 migration。

此裁定補充 DEC-P298 的發票明細映射，不取代既有退款分攤與折讓規則。

### DEC-P300（B1）：以獨立 DES-21 Migration PR 集中模型變更

- PR #7 移除 `OrderCoupon.MinimumSpendAmount` 的 Entity／Configuration 模型變更，維持「計算規則與 Coupon lifecycle」的單一責任。
- 另開一支直接基於最新 `dev` 的 DES-21 Migration PR，由 Yinyin 單一維護 `ModelSnapshot`，Haru 複核訂單欄位。
- 該 PR 一次包含：
  - `OrderCoupons.MinimumSpendAmount`
  - `OrderItems.IsCouponEligible`
  - `RefundAllocations.Quantity`
  - `ShippingClawback` 與相關不變條件
  - Entity、Configuration、Migration、ModelSnapshot
- 必須完成 SQL Server provider-backed 驗證，確認模型無 pending changes、空資料庫可由 migrations 建立，且必要索引、精度、nullability 與 check constraints 符合裁定。
- 本階段只 scaffold、review 與測試 migration，不套用到共用或正式資料庫。

### DEC-P301（C1）：PR #9 改為直接依賴最新 `dev`

- PR #9 與 PR #8 無實際 Promotions 契約依賴；應從最新 `dev` 重建或 rebase，只保留 PR #9 自身的兩個付款 commits。
- PR #8 仍依賴 PR #7，因其實作 PR #7 所定義的 `ICouponRuleReader`、`CouponRuleSnapshot` 等契約。
- PR 的 base、commit 與遠端狀態須另經授權後才可實際變更；本批次只記錄目標分支架構。

### DEC-P302（D1）：PR #6 與 PR #16 在阻擋解除前維持 Draft

- PR #6 必須等待 DEC-P299 的識別契約、DES-21 的 `RefundAllocations.Quantity`，以及對應 writer／reader／DTO／退款測試完成後，才可轉 Ready for review。
- PR #16 必須等待既有 A1～E1 前置條件完成：可設定的隔離策略、管理員 actor scope、中央稽核、遮罩器、數量與可信快照，以及最終契約／OpenAPI 同步，才可轉 Ready for review。
- Draft 狀態只代表尚未達到合併門檻，不否定已完成的程式碼；所有阻擋與 CI 都解除後再做一次完整 review。
- 實際切換 Draft／Ready 屬 GitHub 狀態變更，仍需另行授權。

## 最低成本分析

- DEC-P299：文件與既有 snapshot 欄位即可提供穩定識別；新增欄位與 migration 會增加模型、資料庫及相容性成本，且目前沒有不能由保留值滿足的需求。
- DEC-P300：讓多支功能 PR 分別修改 `ModelSnapshot` 會持續產生衝突與 model drift；集中到一支既有技術範圍內的 Migration PR，是滿足資料一致性的最小完整作法。
- DEC-P301：PR #9 沒有 PR #8 的程式依賴；保留堆疊只會擴大 diff 與等待鏈，直接依賴 `dev` 可降低 review 與合併成本。
- DEC-P302：Draft 是可逆的流程控制，不需改碼即可避免未滿足前置條件的 PR 被誤合併。

## 商業與交付影響

- 受影響角色：Yinyin、Haru、reviewer，以及發票／付款／退款功能的使用者。
- 目前風險：發票非商品列可能被錯誤分類或漏算；多 PR 修改 EF 模型可能造成 migration 漂移；錯誤分支依賴會放大 review 範圍；未完成契約可能被提前合併。
- 預期結果：非商品明細可被確定映射；DES-21 模型由單一 PR 交付；PR #9 可獨立 review；PR #6、#16 僅在證據完整後進入合併流程。
- 建置與持續成本：需維護兩個 Domain 保留常數、一支集中 Migration PR，以及 Draft gate；不增加新資料表或第三方依賴。
- 信心：高。裁定基於現有模型、PR diff 與依賴關係；最終仍以 SQL Server provider-backed 測試與完整 review 為準。
- 成功指標：未知非商品列被拒絕；DTO `kind` 映射測試通過；EF 無 pending changes；migration 可重建空資料庫；PR #9 diff 只含自身付款變更；PR #6、#16 的阻擋與 CI 全數解除。
- 停止／回復條件：若已存在正式歷史資料使用其他非商品識別值，或 SQL Server 驗證證明保留值方案無法維持完整性，停止合併並另開裁定；Draft 與分支 base 可透過 GitHub 流程回復。

## 實作與驗收門檻

1. Domain 集中定義兩個保留值，Writer、Reader 與 DTO 映射不得各自散落字串。
2. 新增商品列、運費列、組裝費列、未知非商品列與 `OtherAdjustment` 拒絕案例。
3. DES-21 Migration PR 從最新 `dev` 建立，由單一負責人更新 ModelSnapshot，且不得執行資料庫套用。
4. PR #9 只保留自身付款 commits；PR #8 保留對 PR #7 的依賴。
5. PR #6、#16 符合各自阻擋清單並重新完整 review 後，才可轉 Ready 或合併。

## 本次寫回範圍

- 決策索引與決策紀錄。
- 未完成項目追蹤表與 Yinyin 工程包。
- 發票需求、API DTO 契約與 Yinyin 最終 Schema。
- 本次不修改程式碼、migration、資料庫、PR 狀態、branch、commit 或遠端內容。

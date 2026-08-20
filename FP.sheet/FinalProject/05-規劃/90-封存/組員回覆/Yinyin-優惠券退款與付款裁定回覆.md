---
文件類型: 組員回覆
收件人: yinyin
寄件人: alex
日期: 2026-08-19
相關決策: DEC-BATCH-014
追蹤項目: DES-21、DES-22
---

# 給 yinyin｜優惠券、退款、付款與模擬發票問題裁定回覆

Hi yinyin，10 項問題已完成裁定，正式記錄為 `DEC-BATCH-014`／`DEC-P271～DEC-P280`。

## 1. 最低消費門檻基準

採你目前的方向：只計算「優惠券適用範圍內的商品小計」。

精確定義是：

- 商品特價後。
- 優惠券折扣前。
- 只計入符合該券適用範圍的商品。
- 不包含其他商品、運費、組裝費及贈品。

因此 CREATOR10 範例中，NT$18,000 顯卡加 NT$5,000 螢幕仍不符合 NT$20,000 門檻。免運門檻維持原規則，使用優惠券折扣後的符合資格商品小計。

## 2. CouponStatus 狀態機

正式轉移如下：

- `Draft → Scheduled / Active / Disabled`
- `Scheduled → Active / Expired / Disabled`
- `Active → Paused / Exhausted / Expired / Disabled`
- `Paused → Active / Expired / Disabled`
- `Exhausted → Active / Expired / Disabled`
- `Expired`、`Disabled` 為終態

`Exhausted` 在名額返還、使用量重新低於限制、尚未到期且未停用時，可以回到 `Active`。

暫時停止一律使用 `Paused`；`Disabled` 代表永久停用，不可重新啟用。請將生命週期實作在 Coupon Entity 的具名方法中。`CouponRule` 可以承載查詢快照，但不能取代 Coupon Entity 成為狀態真實來源。

## 3. 購物車套券是否落資料庫

確認採 A：

- 不新增 `Carts.AppliedCouponCode`。
- 前端每次預覽折扣時帶入優惠碼。
- Checkout 重新驗證優惠券。
- 允許重新整理、跨裝置或購物車合併後遺失輸入值。

所以不需要提出 Cart Schema 變更。

目前 `/api/v1/cart/coupon` 缺少 `CartDto`、Endpoint 基線及購物車身分解析機制，請不要在你的分支自行發明這些共用契約。先保留 Application 試算與 Reader，Endpoint 另行整合。

## 4. CouponRuleReader 測試

採真正的 SQL Server Provider-backed 整合測試，不新增 EF InMemory 或 SQLite Provider。

需要驗證：

- 實際 SQL Translation。
- All／Restricted 範圍。
- 包含分類、包含商品及排除商品。
- 排除優先規則。
- Status 與有效期間。
- MinimumSpend 資料讀取。

若共用 SQL Server 測試環境尚未完成，可以先開 Draft PR 並標記 `DES-21`，但在正式合併 Reader 前要補齊，不能用 InMemory／SQLite 當替代證據。

## 5. 付款嘗試期限

確認採你目前的實作：取付款方式期限與訂單原付款期限的較早者。

```text
InstructionExpiresAtUtc
= Min(付款方式期限, Order.PaymentDueAtUtc)
```

- 即時付款最長 15 分鐘。
- ATM／超商代碼最長 3 天。
- 重試不得延長訂單期限。
- COD 不建立線上付款指示期限。

## 6. OrderCoupon.MinimumSpend

確認需要 Schema 補強：

```text
OrderCoupons.MinimumSpendAmount decimal(18,2) NULL
```

它保存下單當時的最低消費門檻，`NULL` 表示沒有門檻。退貨重算不得回查目前 Coupon。

## 7. 訂單品項優惠券適用旗標

第一版每張訂單最多一張優惠券，因此採最小方案：

```text
OrderItems.IsCouponEligible bit
```

這是下單時的不可變快照。不得使用 `DiscountAllocation > 0` 反推，因為合法適用商品的分攤結果可能因四捨五入成為零。

這個欄位屬於 haru 的 OrderItem 範圍，請與 haru 交叉確認。

## 8. RefundAllocation 扣回方向

維持：

```text
RefundAllocation.Amount > 0
```

不使用正負號保存方向，由 `AllocationType` 決定加減。

增加退款：

- `ItemRefund`
- `OriginalShipping`
- `ReturnShipping`
- `AssemblyFee`

從退款扣回：

- `DiscountClawback`
- `ShippingClawback`

公式：

```text
FinalRefundAmount
= 增加退款類型合計
- 扣回類型合計
```

`OtherAdjustment` 第一版禁止寫入，避免出現方向不明的金額。

## 9. 模擬發票 Endpoint 與錯誤碼

正式 API 契約已補齊，不再使用只有程式內部看得懂的型別化拒絕原因代替 Problem Details code。

前台查詢：

- `GET /api/v1/orders/{orderId}/invoice`
- 權限：會員本人或有效的 Guest Order 限單 Scope。
- Response：`SimulatedInvoiceDto`，買受人資料必須遮蔽並帶 DEMO 標記。

後台：

- `GET /api/v1/admin/invoices`
- `GET /api/v1/admin/invoices/{id}`
- `POST /api/v1/admin/orders/{orderId}/invoices`
- `POST /api/v1/admin/invoices/{id}/actions/void`
- `POST /api/v1/admin/invoices/{id}/allowances`

後台權限固定為 `FinanceManager`／`SuperAdmin`。開立與折讓需要 `Idempotency-Key`；折讓 Request 只帶 `refundPublicId` 與發票 RowVersion，金額必須由後端成功 Refund 及原發票明細推導，不能接受前端指定。

正式新增五個錯誤碼，HTTP Status 均為 409：

- `invoice_order_unpaid`：未付款不可開立。
- `invoice_order_cancelled`：取消訂單不可開立。
- `invoice_already_exists`：訂單已有發票。
- `invoice_state_conflict`：目前發票狀態不允許該操作。
- `invoice_allowance_required`：已發生退款，不可作廢，必須建立折讓。

其他共用錯誤沿用現有 `resource_not_found`、`authorization_forbidden`、`concurrency_conflict`、`idempotency_payload_conflict` 與 `refund_state_conflict`，不要再新增同義別名。

## 10. 發票稅率與金額位數

確認採你目前的 5% 方向，並補完整計算契約：

```text
BusinessTaxRate = 0.05m
AmountScale = 0
NetAmount = Round(GrossAmount / 1.05m, 0, AwayFromZero)
TaxAmount = GrossAmount - NetAmount
```

- 訂單成交總額視為含稅金額。
- 發票與折讓的未稅、稅額及含稅金額均為 TWD 整數元。
- 資料庫欄位仍可維持 `decimal(18,2)`，但寫入值的小數位必須為 0，不需要為此變更 Schema。
- 明細使用相同公式，最後一筆合法明細吸收尾差，使明細加總與表頭完全一致。
- 含稅 NT$1,000 的固定驗收結果是未稅 NT$952、稅額 NT$48、含稅 NT$1,000。
- 折讓沿用原發票規則，不依目前商品、分類或設定重算歷史。

## 後續處理方式

1. `feature/coupon-calculation` 維持適用商品小計邏輯，補齊門檻精確定義與案例後可開 Draft PR。
2. `feature/cart-coupon` 維持不落庫；Endpoint 暫不自行補造，可先針對 Reader／DI 開 Draft PR。
3. 請補 Coupon Entity 狀態方法、`MinimumSpendAmount`、`IsCouponEligible`、`ShippingClawback`、退款方向測試及 SQL Server Reader 測試。
4. 現有 `InitialCreate` 不要直接改寫，也請先不要自行 scaffold 或 apply Migration。完成 Entity／Configuration／測試後交給我走 `DES-21` Migration Review Gate。
5. 發票切片依上述正式 Endpoint／DTO 實作，補五個錯誤碼的整合測試，以及未付款、取消、重複開立、非法作廢、退款後折讓、1,000→952＋48與尾差案例；這部分以 `DES-22` 追蹤。
6. PR 說明請引用 `DEC-BATCH-014`、`DES-21` 與 `DES-22`，並清楚列出本 PR 已完成及刻意延後的範圍。

正式決策與文件已經寫回，可以依以上內容繼續開發。

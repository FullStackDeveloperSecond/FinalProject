---
batch_id: DEC-BATCH-043
status: applied
decision_date: 2026-09-02
decision_ids:
  - DEC-P352
  - DEC-P353
superseded_decisions:
  DEC-P352: DEC-P355
---

# DEC-BATCH-043｜Checkout 付款續接與優惠券配送試算定版

> [!important] 後續決策覆寫
> DEC-P352 的 `/payment-attempts/current + 204` 契約已由 [[DEC-BATCH-045-付款續接Endpoint上游收束定版|DEC-P355]] 覆寫。現行契約只使用 `/payment-attempts/latest + 404`；本頁保留原決策內容作歷史稽核。DEC-P353 不受影響。

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P352 | 採用 1A：新增 Owner Scope 的 `GET /api/v1/orders/{id}/payment-attempts/current`。有尚未終止的目前付款嘗試時回 `200 PaymentAttemptDto`，沒有時回 `204`；非本人訂單維持 `404 resource_not_found`。C-15 必須優先續接 Checkout 已建立的付款嘗試，只有不存在目前付款嘗試或既有嘗試已進終態時，才允許建立新的付款嘗試。 |
| DEC-P353 | 採用 2A：既有 `GET /api/v1/cart/shipping-options` 增加可選 `couponCode`。後端重用既有 Coupon Quote 與 Cart Actor Scope，依優惠後符合資格小計重算免運、運費與 COD 最終應付資格；前端只有在使用者按下套用後才更新已套用代碼、重新查詢配送選項並清除舊付款選擇。最終建單仍由既有 Checkout 單一 SQL 交易重新驗證，配送預覽不是價格權威。 |

本批修正 WP-A03 working-tree Review 發現的兩個跨邊界阻斷，不改變 [[DEC-BATCH-042-Checkout具體付款方式契約定版|DEC-P351]] 的具體付款方式契約，也不建立第二套 Checkout、Coupon 或 Payment 寫入流程。

## Lowest-Cost Analysis

1. 接受現況：預付 Checkout 會先建立付款嘗試，C-15 再建立一次而固定衝突；優惠券後 COD 可見性也可能使用折扣前金額，無法完成既定驗收，未採用。
2. 只改文件或操作教學：不能修正執行期付款續接與金額判斷，未採用。
3. 由前端猜測付款嘗試或自行計算優惠：前端沒有可信付款狀態與優惠資格資料，會形成第二份商業規則，未採用。
4. 擴充既有唯讀路徑：重用 PaymentAttempt、Coupon Quote、Shipping Options、Cart Actor Scope 與 Checkout 最終交易即可完整滿足驗收，採用。
5. 新增 Entity、Schema、Migration、套件、服務或平行 Checkout：既有模型與服務已足夠，成本、維護與回復面較大，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 使用預付方式結帳的會員／已驗證訪客，以及在 NT$20,000 COD 邊界附近使用優惠券的顧客 |
| 現況風險 | 所有預付 Checkout 可能因重複 Attempt 而無法進入付款；部分優惠券訂單會顯示與 Checkout 最終政策不一致的 COD 選項 |
| 觸及頻率 | 每次預付 Checkout 進入 C-15；每次在 Checkout 套用優惠券後查詢配送與付款方式，實際流量未知 |
| 預期可量測結果 | Checkout 建立的 active Attempt 可直接完成且不新增第二筆；優惠券後 Shipping fee／COD 判斷與 Checkout `GrandTotal` 一致 |
| 建置／持續成本 | 最小唯讀 Endpoint、既有 Coupon Reader／Quote 接線、查詢參數、前端狀態與回歸測試；無新增持續費用 |
| 風險成本 | 新唯讀 Endpoint 必須維持 Owner／Guest 限單隔離；配送預覽與最終建單間仍可能有正常併發變更，最終由 Checkout RowVersion 與交易重驗拒絕 |
| 信心 | 高；兩項修正均重用既有資料模型、Actor Scope、Coupon Calculator、COD Policy 與 SQL Provider 測試 |
| 成功指標 | Active Attempt 續接／跨 Owner 404、優惠券後 COD 邊界、查詢參數驗證、OpenAPI／Typed Client、前端 component tests 與完整品質門檻通過 |
| 停止／回復條件 | 若目前付款嘗試無法以現有 Order Scope 唯一判定，或 Coupon Quote 與 Checkout 交易計算出現非併發造成的差異，停止合併並回到契約 Review；不得在前端硬寫補償 |

## 執行與驗收邊界

- 「目前付款嘗試」只包含非 `paid`／`failed`／`expired`／`cancelled` 的最新一筆；讀取不得變更狀態或建立資料。
- Member 只可讀自己的訂單；Guest 仍須先通過既有 Guest Order Access Token 的限單驗證。對越權目標不揭露是否存在。
- `couponCode` 最長 64 字元；未提供時維持既有 Shipping Options 行為。
- Coupon Reader 只由伺服器載入目前 Actor 的 Cart、SKU、Product、Category、Sale 與 RowVersion，不接受前端提供價格或資格事實。
- 套用優惠券後，C-14 必須以新 Shipping Options 重新選擇付款方式；Checkout 建單仍再次重算價格、優惠、運費、COD 與庫存。
- 本決策不新增資料表、Migration、套件、外部服務、持久化購物車 Coupon 欄位或新的付款寫入命令。

## 影響文件

- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/02-API與前端契約/M功能桌面UI與Route規格]]
- [[05-規劃/01-時程與進度/M功能實作矩陣]]
- [[05-規劃/01-時程與進度/核心交易整合協調]]
- [[05-規劃/02-分工與交接/工程包/Alex-個人剩餘交付實作計畫]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]

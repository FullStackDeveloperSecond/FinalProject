---
batch_id: DEC-BATCH-042
status: applied
decision_date: 2026-09-02
decision_ids:
  - DEC-P351
---

# DEC-BATCH-042｜Checkout 具體付款方式契約定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P351 | 採用 B：`ShippingOptionDto.allowedPaymentMethods` 正式改為具體 `PaymentMethod[]`，不得回傳無法提交 `CreateOrderRequest.paymentMethod` 的群組別名 `prepaid`。每個合法配送選項固定列出 `creditCard`、`atm`、`convenienceCode`、`linePay`、`applePay`、`googlePay` 六種預付方式；只有後端既有 COD Policy 對該購物車與配送方式判定通過時才追加 `cashOnDelivery`。C-14 只能依此陣列顯示與提交付款方式，不自行展開群組或另算 COD 資格。 |

本決策細化 [[DEC-BATCH-041-Checkout配送查詢責任邊界定版|DEC-P350]] 的「前端不得硬寫付款方式」要求，不改變七種正式付款列舉、付款狀態機、COD 上限或 Checkout 交易規則。

## Lowest-Cost Analysis

1. 接受現況：`prepaid` 不是 `PaymentMethod`，前端無法原值提交，不能完成 C-14，未採用。
2. 只改文件或由人工說明：不能修正執行期契約，未採用。
3. 由前端把 `prepaid` 展開為六種方式：會建立第二份付款清單並違反 DEC-P350，未採用。
4. 擴充既有 `PaymentMethodPolicy` 並讓 Shipping DTO 回傳正式列舉：能以單一來源同時約束後端、OpenAPI 與 TypeScript，採用。
5. 新增付款群組 Entity、設定表、套件或服務：既有七種付款及分類足以完成需求，成本與回復面較大，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 所有進入 C-14 的訪客與會員，以及 Checkout／Shipping 前端維護者 |
| 現況風險 | UI 若收到 `prepaid`，不是無法提交，就是必須硬寫展開規則；兩者都可能使付款選項與後端漂移 |
| 觸及頻率 | 每次載入配送選項與選擇付款方式；實際流量未知 |
| 預期可量測結果 | OpenAPI 將欄位限制為 `PaymentMethod[]`；API JSON 不含 `prepaid`；C-14 只顯示後端回傳的具體方式 |
| 建置／持續成本 | 最小 DTO／Policy／前端契約與測試調整；不新增 Schema、Migration、套件、服務或持續費用 |
| 風險成本 | 回應欄位型別與值語意變更可能影響舊前端；目前正式 C-14 尚未發布，並以 OpenAPI diff、完整前端 Gate 與 API Integration 控制 |
| 信心 | 高；`PaymentMethod`、`PaymentMethodPolicy`、COD Policy 與 Shipping Options 均已存在 |
| 成功指標 | Domain、Shipping SQL Provider、Shipping API JSON、OpenAPI／Typed Client 與 C-14 component tests 全部通過 |
| 停止／回復條件 | 若任何既有正式消費者仍依賴 `prepaid`，停止合併並回到契約協調；不得在前端加相容硬寫掩蓋 |

## 執行與驗收邊界

- 六種預付方式由既有 `PaymentMethodPolicy.PrepaidMethods` 提供；Shipping 不維護第二份字串清單。
- COD 只由既有 `PaymentAttemptPolicy.FindCashOnDeliveryRejection` 判定，C-14 不重新推算金額、組裝或 SKU 限制。
- API 以既有 camelCase enum 序列化，OpenAPI 與 Typed Client 由既有產生流程更新。
- 訪客建單後仍依既有 Guest Order Access 規格，以訂單編號與 Email 完成限單驗證後再付款／查單；本決策不新增或放寬訂單授權。
- 本決策不新增 Entity、資料表、Migration、套件、外部服務或費用。

## 影響文件

- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/02-API與前端契約/M功能桌面UI與Route規格]]
- [[05-規劃/02-分工與交接/工程包/Alex-個人剩餘交付實作計畫]]
- [[05-規劃/01-時程與進度/M功能實作矩陣]]
- [[05-規劃/01-時程與進度/核心交易整合協調]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]

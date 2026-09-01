---
batch_id: DEC-BATCH-040
status: applied
decision_date: 2026-09-01
decision_ids:
  - DEC-P349
---

# DEC-BATCH-040｜Checkout 政策版本查詢契約定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P349 | 採用 A1：新增 Public、唯讀 `GET /api/v1/checkout/policy-versions`，回應重用 `AcceptedPolicyVersions`，只含目前 Terms、Return、Privacy 三個正整數版本。版本值必須來自既有 `ICheckoutPolicyProvider.Current`，前端不得寫死 `1/1/1`。伺服器內部 `ShippingConstraint` 版本不屬於顧客接受輸入，不得回傳或由顧客提交。既有 `POST /api/v1/orders` 仍於送單時以同一 Provider 再驗證三個版本，metadata 查詢不取代伺服器驗證。 |

## Lowest-Cost Analysis

1. 接受現況：C-14 無合法來源取得 `CreateOrderRequest` 的必填政策版本，無法完成可靠 Checkout，未採用。
2. 只改流程／文件：不能讓執行中的前端取得目前版本，未採用。
3. 前端或設定寫死 `1/1/1`：會與後端 `CheckoutPolicyOptions` 形成隱性雙寫；政策升版後所有未同步前端都會被拒絕，未採用。
4. 重用既有 `ICheckoutPolicyProvider` 並新增三欄唯讀投影：不新增 Schema、套件、服務或平行政策來源，完整滿足版本呈現與送單需求，採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 所有會員與訪客 Checkout 顧客 |
| 現況風險 | 前端無法證明顧客接受目前政策；若硬寫版本，升版會使所有 Checkout 中斷 |
| 觸及頻率 | 每次載入 C-14 Checkout；實際流量未知 |
| 預期可量測結果 | 匿名查詢只回三個目前版本；C-14 成功載入後原值送回；過期版本仍由 `POST /api/v1/orders` 拒絕 |
| 建置／持續成本 | 重用既有 Provider、DTO、Controller／OpenAPI／Typed Client；無新基礎設施與固定維運成本 |
| 風險成本 | 主要為版本來源分裂或多揭露伺服器規則；以單一 Provider、三欄白名單及 API integration test 控制 |
| 信心 | 高；動態 Provider 版本 `7/8/9` 的匿名 API 測試已證明不依賴寫死值，且 ShippingConstraint 未序列化 |
| 成功指標 | 200、精確三欄、動態版本、OpenAPI／Typed Client 同步，以及既有 Checkout POST 回歸通過 |
| 停止／回復條件 | 回應出現 ShippingConstraint／其他內部欄位、版本與送單驗證來源不同，或舊 POST 契約受破壞時停止；additive GET 可獨立移除 |

## 契約與實作邊界

- Route 為 additive，既有 Checkout POST、DTO 欄位、狀態碼與驗證不改名、不移除。
- `AcceptedPolicyVersions` 表示顧客明確接受的三項政策，不是完整伺服器交易規則快照。
- Public metadata 不含個資、Secret、Token、內部 ID 或可變更能力；不新增 Policy 並不降低 `POST /api/v1/orders` 的後端驗證。
- 無 Entity、Mapping、Migration 或 Production SQL 變更。
- C-14 仍須等待 Terry-owned `GET /api/v1/cart/shipping-options`／`GET /api/v1/convenience-stores` 正式落地；本決策不授權跨 owner 直接查 Shipping／Shopping 表。

## 影響文件

- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[05-規劃/02-分工與交接/工程包/Yinyin-負責範圍補全推進計畫]]

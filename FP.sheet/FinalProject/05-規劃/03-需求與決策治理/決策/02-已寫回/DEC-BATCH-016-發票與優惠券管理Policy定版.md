---
type: decision-record
batch_id: DEC-BATCH-016
title: 發票與優惠券管理 Policy 定版
status: applied
created_at: 2026-08-20
submitted_at: 2026-08-20
applied_at: 2026-08-20
decision_count: 2
decision_range: DEC-P283～DEC-P284
source: alex 採用建議並授權寫回
---

# DEC-BATCH-016｜發票與優惠券管理 Policy 定版

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P283 | 建立正式 `Invoice.Manage` Policy，允許 `FinanceManager`、`SuperAdmin`，涵蓋後台模擬發票查詢、開立、作廢與折讓。Policy 沿用既有管理員驗證與 TOTP／MFA 基線；狀態、冪等、RowVersion、金額計算與 Audit 仍由各 Use Case 負責。V1 不拆分 View／Issue／Void／Allowance Policy，未來只有在讀寫角色或風險邊界改變時另案拆分。 |
| DEC-P284 | 建立正式 `Coupon.Manage` Policy，允許 `FinanceManager`、`MarketingAnalyst`、`SuperAdmin`，涵蓋後台優惠券查詢、建立、修改、啟用、暫停與停用。Policy 沿用既有管理員驗證與 TOTP／MFA 基線；不涵蓋前台購物車套券，規則完整性、狀態、期間、名額、RowVersion 與 Audit 仍由各 Use Case 負責。V1 不拆分 View／Write／Lifecycle Policy，未來只有在角色或操作風險分流時另案拆分。 |

## 最低成本與商業影響

- 沿用現有 `FinanceManager`、`MarketingAnalyst`、`SuperAdmin`、管理員 Cookie Scheme、MFA Claim Gate 與 `AddAdminPolicy`；只增加兩個具名授權契約，不新增角色、自訂 Authorization Handler、資料表、套件或公開 DTO。
- 維持只有 `Refund.Execute` 無法讓發票與優惠券 Endpoint 使用語意正確且可測的授權契約；借用退款 Policy 會錯誤綁定角色與操作目的，因此不採用。
- 受影響者為財務管理員、行銷分析員、SuperAdmin 與 Yinyin 負責的後台 Endpoint。主要風險是 Policy 過寬、誤把前台套券納入管理權限，或誤認 Policy 已涵蓋商業驗證與 Audit。
- 成功指標：所有後台發票 Endpoint 只使用 `Invoice.Manage`；所有後台優惠券管理 Endpoint 只使用 `Coupon.Manage`；合法角色、錯誤角色、未完成 MFA、匿名與 SuperAdmin 正向案例均有測試；前台套券不受管理員 Policy 限制。
- 建置成本限於 Policy 常數／註冊、Endpoint 套用與授權測試，無新增持續服務成本。若未來讀寫角色不同，保留現有 Policy 作為過渡並以新決策拆分，不在本批預先增加粒度。

## 寫回範圍

- [[01-需求/角色與權限]]
- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/08-測試與驗收/M功能測試案例目錄]]
- [[05-規劃/02-分工與交接/工程包/Yinyin-優惠付款退款與發票工程包]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]

## 實作 Gate

- 本批只完成規格、API 契約、測試要求與工作追蹤，不代表 Policy 常數、註冊、Controller 套用或測試已完成。
- `Coupon.Manage` 的實作與驗證併入 DES-21；`Invoice.Manage` 的實作與驗證併入 DES-22。
- 本批不修改程式或資料庫。若實作需要改變正式角色、前台授權、公開 Route／DTO 或狀態機，必須另提契約變更，不得自行擴張本決策。

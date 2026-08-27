---
type: decision-record
batch_id: DEC-BATCH-027
title: 相容性來源、多值規格與 Checkout 原子交易定版
status: applied
created_at: 2026-08-27
applied_at: 2026-08-27
source: alex 逐項採用上游缺口補齊建議
decision_ids:
  - DEC-P313
  - DEC-P314
  - DEC-P315
  - DEC-P316
  - DEC-P317
  - DEC-P318
---

# DEC-BATCH-027｜相容性來源、多值規格與 Checkout 原子交易定版

## 背景

商品規格模型原本只能保存單一 Option，無法正規化表達介面、Socket 支援清單等真正多值規格；硬性相容性雖已有來源表，仍缺少「哪些語意鍵由程式保護」與「缺來源如何處理」的可執行契約。Checkout 同時跨越購物車、商品、優惠、庫存、訂單、付款與物流，若各模組各自提交，無法證明最後一件商品競爭、優惠名額與建單能全部成功或全部回滾。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P313 | 相容性使用的 Category Code／Specification SemanticKey 由程式碼目錄保護。被保護定義的 Category、SemanticKey、ValueType、Unit 與 AllowsMultiple 不可由後台變更或刪除；顯示名稱、排序與規則明確允許的警告設定仍可維護。 |
| DEC-P314 | `SpecificationDefinition` 新增 `AllowsMultiple`，僅 `ValueType=Option` 可啟用。單選維持 `SkuSpecificationValue.OptionId`；多選使用正規化 `SkuSpecificationOptionSelection` Join Entity，不使用逗號字串或 JSON。每個已選 Option 可保存自己的 `SpecificationSourceId`。 |
| DEC-P315 | Checkout 與推薦使用的硬性相容性規格必須帶已覆核 `SpecificationSource`。缺值、缺來源、來源未覆核、語意鍵缺失或型別不符一律回 `InsufficientData` 並阻擋結帳；不得把缺值當成相容、以預設值補齊或讓 AI 猜測。 |
| DEC-P316 | V1 CPU／主機板採專案內固定的 CPU 世代／Socket ↔ 晶片組映射；映射只能納入已有官方證據的組合。可能需要 BIOS 更新者顯示警告，不維護每張主機板的最低 BIOS 版本。官方證據保存於知識點研究文件。 |
| DEC-P317 | Checkout Application 只依賴 `ICheckoutTransactionGateway` 與用途專用規則介面。Infrastructure Gateway 使用全案單一 `DoSelectDbContext`，加入 `IIdempotencyExecutor` 已擁有的 SQL Transaction，原子完成 Cart 鎖定、價格／優惠／物流／相容性重驗、Order／Items／快照、CouponRedemption、InventoryReservation／Movement、初始 PaymentAttempt 與 Cart 轉換；Gateway 不自行 Begin／Commit。此設計不授權其他 Application 模組直接存取彼此 Repository。 |
| DEC-P318 | 訂單編號固定為 `DSyyyyMMddNNNN`，日期採臺灣本地日曆日，每日流水號 `0001`～`9999`。以 SQL Server `sp_getapplock` 對當日序列化；取得鎖失敗或單日超過上限時 Checkout 整體失敗，不降級成隨機或重複編號。 |

## 最低成本分析

1. 接受原模型與跨模組各自提交：無法表達多值來源，也無法滿足原子結帳與最後一件商品資料完整性，排除。
2. 只以文件／人工流程限制：資料庫仍可保存模糊多值，交易失敗仍可能留下半成品，無法達成驗收，排除。
3. 以 JSON 或逗號字串保存多值、Checkout 串多個既有服務：可少建一張表，但失去 FK、來源逐項追蹤與原子提交，排除。
4. 沿用既有單一 DbContext、Idempotency Executor、領域計算政策與來源表；只增加一張 Join Table、一個 Infrastructure Transaction Gateway 與固定目錄：是滿足正規化、可追溯及原子性要求的最小完整變更，採用。
5. 拆成多 DbContext、Message Broker、分散式交易或新服務：第一版單機單庫不需要，會增加協調與維運成本，排除。

## 商業影響

- 受影響者：電腦組裝新手、商品管理員、訂單／庫存人員與開發整合者。
- 目前風險：多值規格無法可靠篩選與驗證；不具來源的相容性結果可能誤導購買；跨模組半成功會超賣、重複占用優惠名額或產生無付款訂單。
- 觸及頻率：每次維護多值硬體規格、每次相容性檢查，以及每次 Checkout。
- 預期可量測成果：多值選項具 FK 與逐項來源；缺少硬性證據時固定阻擋；成功 Checkout 一次建立全部交易資料，缺貨或任一步驟失敗時資料副作用為零；相同冪等請求不重複建單。
- 建置與持續成本：增加一張 Join Table、一支 Migration、一個 Gateway、固定映射與 Provider-backed 測試；無新套件、外部服務或持續費用。
- 主要風險成本：Gateway 交易範圍過大、鎖順序不一致造成競爭，或來源資料未完成而阻擋可售商品。
- 信心：中高；既有單一資料庫與冪等交易可重用，成功與失敗回滾已有 SQL Server 測試，但完整競爭、API 與 E2E 尚未完成。
- 成功指標：Migration 無破壞性 Up、無 pending model changes、SQL 成功／回滾／優惠名額案例通過，完整 Build／Test 綠燈；後續補齊最後一件商品競爭、冪等 replay、API 與 E2E。
- 停止／回退條件：若共用交易無法確保鎖順序與可接受的本機展示效能，停止擴張 Gateway，先以測試定位交易範圍；不得改採部分提交。若官方證據不足，移除該映射並回 `InsufficientData`，不得降低為推測相容。

## 已完成證據與仍待 Gate

- 已建立固定相容性目錄、來源約束、唯讀 Catalog Reader、多值關聯與 EF Configuration。
- 已建立 Checkout Transaction Gateway 與 SQL Server 成功／缺貨回滾／免運優惠名額案例。
- 已產生 `20260827065535_AddMultiValueSpecificationProvenance` Migration；只新增 `AllowsMultiple` 與多值 Join Table，尚未套用正式資料庫。
- .NET 完整測試 1,350 項通過，0 失敗、0 略過；Build 0 warning／0 error。
- 尚未完成：Checkout API／OpenAPI／前端、最後一件商品並行競爭、完整冪等 replay、付款成功回呼、Migration chain 測試與瀏覽器 E2E。
- 「優惠後最終應付為 0 元」的訂單／PaymentAttempt 行為尚未裁定，不包含在本批決策，須由追蹤項目另行決定。

## 影響文件

- [[02-領域需求/02-商品庫存與組裝/商品、組裝與相容性]]
- [[02-領域需求/03-交易與履約/購物車、訂單、付款與物流]]
- [[03-架構/03-資料與一致性/資料字典-商品庫存與組裝]]
- [[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]]
- [[03-架構/09-資料表實作交付/Terry-商品庫存物流組裝與報表最終Schema]]
- [[05-規劃/01-時程與進度/M功能實作矩陣]]
- [[05-規劃/01-時程與進度/核心交易整合協調]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]

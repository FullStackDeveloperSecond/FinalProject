---
type: decision-record
batch_id: DEC-BATCH-013
title: Haru 會員、訂單與訪客存取 Schema 定版
status: applied
created_at: 2026-08-18
submitted_at: 2026-08-18
applied_at: 2026-08-18
decision_count: 8
decision_range: DEC-P263～DEC-P270
source: 原始 Meta Bind 互動表單；依 AUTO-DEC-008 由 Git 歷史追溯
---

# DEC-BATCH-013｜Haru 會員、訂單與訪客存取 Schema 定版

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P263 | Guest 驗證 Challenge 使用 SQL Server `GuestOrderAccessRequests` 專表；只保存 HMAC／Hash 與安全中繼資料，不保存明文驗證碼。 |
| DEC-P264 | 六位數 Challenge 單次使用；驗證後的限單 Guest Access HttpOnly Cookie 可在 30 分鐘內多次操作，到期、撤銷或 Scope 不符時失效。 |
| DEC-P265 | 每台組裝工作使用獨立 append-only `AssemblyJobStatusHistories`；OrderStatusHistories 只保存訂單聚合投影。 |
| DEC-P266 | Guest Challenge 最多錯 5 次；初次寄送計入最多 3 封、間隔 60 秒；15 分鐘每 IP Hash 10 次、Email HMAC 5 次、訂單 Lookup Hash 5 次。 |
| DEC-P267 | Guest Request／Token 到期後保存 30 天；每日依主鍵分批清理，每批最多 500 筆；長期安全事件保存於 AuditLogs。 |
| DEC-P268 | 地址採 PostalCode／City／District／AddressLine 結構；MemberAddresses 保留 Label，但 Checkout／Orders 不保存 Label，訂單保存 City／District 快照。 |
| DEC-P269 | 會員與管理員共用登入 Application Service；第五次失敗依 AccountType 設定 LockoutEnd 為 15／30 分鐘，不依賴單一 DefaultLockoutTimeSpan。 |
| DEC-P270 | 建立 Haru 正式 Owner Schema 交付，以 DES-20 追蹤；yinyin 交叉覆核、alex 整合，覆核前不得建立 Migration。 |

## 後續狀態

- 2026-08-18：yinyin 已完成欄位、索引、狀態、授權、交易及跨模組交叉覆核，DES-20 已關閉。
- 本次 Gate 只確認 Schema 文件；Entity、Fluent Configuration、測試與 Migration 仍須獨立實作及審查。
- 2026-08-27（PR #40，alex 裁定 A1）：Guest Challenge 的 `RequestPublicId` 在重寄時維持穩定；同一筆 Request 原地更新 CodeHash／SendCount／LastSentAtUtc，並在同一個 Serializable transaction 以 `GuestOrderAccessRequests` 新增不可驗證的限流事件 Row，消耗本次 IP Hash 與原 Email／OrderLookup Hash 三個 Scope。此為 DEC-P263／P266 的無 Schema 實作細化，不新增欄位、Migration 或第二張限流表，也不得再用 `(EmailHash, OrderHash, ExpiresAtUtc)` 猜測 successor chain。
- 2026-08-27（PR #40，alex 裁定 B1）：中央 Outbox 完成前，PR #40 不 Approve、不 Merge。最終 Gate 必須讓 Challenge 建立／重寄與 Email Outbox Message 在同一 SQL Server transaction 提交，並由中央 Dispatcher 在 commit 後投遞；不得在訪客查單模組建立第二套局部 Outbox。

## 寫回範圍

- [[03-架構/09-資料表實作交付/Haru-會員登入訂單與訪客存取最終Schema]]
- [[02-領域需求/01-會員與身分/會員、驗證與通知]]
- [[03-架構/02-API與前端契約/API共通規範]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/03-資料與一致性/資料字典-會員客服AI與治理]]
- [[03-架構/03-資料與一致性/資料字典-購物交易與售後]]
- [[03-架構/03-資料與一致性/狀態機設計]]
- [[00-專案概述/DoSelect完整系統規格書-v1.0]]
- [[03-架構/09-資料表實作交付/README]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]

## Gate

DEC-BATCH-013 已完成規格寫回，但 DES-20 尚未完成。只有 yinyin 完成欄位、索引、狀態、授權、交易及跨模組交叉覆核後，Schema 文件才能關閉；Entity、Configuration、測試與 Migration 仍須走各自 Gate。

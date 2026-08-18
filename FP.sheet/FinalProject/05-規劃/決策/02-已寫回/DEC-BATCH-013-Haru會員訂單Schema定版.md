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
source: "[[05-規劃/決策/00-互動中/DEC-BATCH-013-Haru會員訂單Schema定版]]"
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

## 寫回範圍

- [[03-架構/資料表實作交付/Haru-會員登入訂單與訪客存取最終Schema]]
- [[02-領域需求/會員、驗證與通知]]
- [[03-架構/API共通規範]]
- [[03-架構/API DTO與Schema契約]]
- [[03-架構/資料字典-會員客服AI與治理]]
- [[03-架構/資料字典-購物交易與售後]]
- [[03-架構/狀態機設計]]
- [[00-專案概述/DoSelect完整系統規格書-v1.0]]
- [[03-架構/資料表實作交付/README]]
- [[05-規劃/未完成項目追蹤表]]
- [[05-規劃/決策紀錄]]

## Gate

DEC-BATCH-013 已完成規格寫回，但 DES-20 尚未完成。只有 yinyin 完成欄位、索引、狀態、授權、交易及跨模組交叉覆核後，Schema 文件才能關閉；Entity、Configuration、測試與 Migration 仍須走各自 Gate。

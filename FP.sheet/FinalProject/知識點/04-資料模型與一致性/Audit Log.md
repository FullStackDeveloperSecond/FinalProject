---
type: knowledge
title: Audit Log
aliases:
  - 稽核日誌
  - Audit Trail
tags:
  - 知識點
  - 稽核
  - 安全
  - 治理
created_at: 2026-08-13
related:
  - "[[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]]"
  - "[[03-架構/05-背景工作與維運/Logging與HealthCheck設計]]"
  - "[[知識點/03-API契約與可觀測性/Correlation ID與Trace ID]]"
---

# Audit Log

## 與一般 Log 的差異

一般 Application Log 主要用於診斷與效能觀察；Audit Log 用於回答「誰在何時、以什麼理由，對哪個資源做了什麼，結果為何」。它需要更穩定的 Schema、權限、保存期限及不可任意修改的治理規則。

Audit Log 不應只是把完整 Entity 序列化前後各一份。這會大量保存個資、Secret，並在 Model 改版後難以解讀。

## 建議結構

- Actor Type、PublicId 與角色快照。
- 穩定 Action 名稱。
- Resource Type＋PublicId。
- 成功、拒絕、衝突或失敗安全碼。
- 白名單 Changed Fields 與 Schema Version。
- 高風險操作理由。
- Correlation ID、Trace ID 及必要 Job PublicId。
- UTC 時間與適度遮蔽的 Network 資訊。

個資欄位原則上只記「已變更」、遮蔽值或不可逆摘要。密碼、Token、Cookie、API Key、TOTP Seed、Recovery Code、完整地址與付款資料不得進入差異內容。

## 完整性與治理

- 一般管理員不可修改或刪除。
- 查詢與匯出本身也要被稽核。
- 清理工作尊重 Retention 與 Hold。
- 權限依安全、隱私及 SuperAdmin 職責分離。
- 高風險交易可與核心資料在同一交易寫入，避免狀態已改卻沒有稽核。

> [!note] 專案決策邊界
> 本專案保存結構化白名單 Diff；一般紀錄 365 天，只有 SuperAdmin 可匯出，且匯出欄位再套白名單。正式規範見 [[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]]。

## 參考資料

- [OWASP Logging Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html)
- [[03-架構/04-安全與檔案/威脅模型與安全檢查表]]

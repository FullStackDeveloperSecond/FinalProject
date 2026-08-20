---
type: knowledge
title: Transactional Outbox
aliases:
  - Outbox Pattern
  - 交易型寄件匣
tags:
  - 知識點
  - 資料一致性
  - 背景工作
  - 事件
created_at: 2026-08-13
related:
  - "[[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]]"
  - "[[03-架構/05-背景工作與維運/背景工作與Hangfire設計]]"
  - "[[知識點/07-基礎設施與交付/Hangfire]]"
  - "[[知識點/04-資料模型與一致性/冪等性]]"
---

# Transactional Outbox

## 為何需要它

「更新訂單後再寄信」包含資料庫寫入與外部副作用兩次獨立操作。若程式在中間中止，可能訂單已成功但通知永遠遺失；先寄信再 Commit 則可能寄出不存在的訂單。這是 Dual Write 問題。

Transactional Outbox 把業務資料與待處理事件寫入同一資料庫交易：

```text
更新 Aggregate＋寫 Outbox
→ 同一交易 Commit
→ Dispatcher 稍後讀取 Outbox
→ 建立背景工作／通知
→ 成功後標記處理
```

## 它提供的保證

Outbox 保證「業務提交時，事件也被可靠保存」，但通常是 at-least-once 傳遞，不是 magically exactly-once。Dispatcher 可能在外部副作用完成後、標記成功前中止，因此 Consumer 仍必須依事件 PublicId 或業務唯一鍵冪等去重。

## Payload 與版本

Outbox Payload 應保存最小必要資料與 `PayloadVersion`：

- 使用穩定事件名稱，不把 .NET 類別完整名稱當永久契約。
- 優先傳 PublicId，Consumer 執行時重新讀取最新受保護資料。
- 不保存 Secret、Cookie、Token、完整 Entity 或不必要個資。
- Schema 改變時新增版本，保留舊版處理策略。

## 與 Hangfire 的關係

Outbox 解決「業務交易與後續工作可靠銜接」；Hangfire 解決「工作持久化、排程、Worker 與重試」。兩者可以搭配，但不能用直接 `Commit` 後 `Enqueue` 取代 Outbox 的原子性。

> [!note] 專案決策邊界
> 首版使用單一 SQL Server `OutboxMessages` 表，不導入外部 Broker。Dispatcher 每 5 秒最多取 20 筆；成功保存 30 天，未成功不得自動刪除，詳見 [[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]]。

## 參考資料

- [Microsoft Learn：Transactional Outbox Pattern](https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-outbox-cosmos)
- [[知識點/03-API契約與可觀測性/Correlation ID與Trace ID]]

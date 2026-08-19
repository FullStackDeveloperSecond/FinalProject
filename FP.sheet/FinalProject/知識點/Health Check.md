---
type: knowledge
title: Health Check
aliases:
  - Liveness
  - Readiness
  - 健康檢查
tags:
  - 知識點
  - 維運
  - ASP.NET Core
  - 可觀測性
created_at: 2026-08-13
related:
  - "[[03-架構/Logging與HealthCheck設計]]"
  - "[[知識點/Secrets管理]]"
  - "[[知識點/Correlation ID與Trace ID]]"
---

# Health Check

## Liveness 與 Readiness

Liveness 回答「程序是否仍能處理基本請求」；Readiness 回答「現在是否具備承接正式流量的必要依賴」。兩者混在一起，短暫資料庫或外部服務問題可能造成程序反覆重啟。

```text
/health/live   程序本身
/health/ready  SQL Server、Migration、必要目錄、核心背景處理
```

可降級的 OpenAI、SMTP 等服務不應讓 Liveness 失敗；可在詳細 Readiness 標為 Degraded。

## 狀態與資訊揭露

ASP.NET Core Health Check 常用 `Healthy`、`Degraded`、`Unhealthy`。Unhealthy 通常回 503。公開回應只需狀態，不應包含連線字串、實體路徑、帳號、Secret 或例外；詳細依賴結果只供授權管理員或本機維運腳本。

Health Check 也不應執行昂貴查詢或產生商業副作用。需要較深的診斷時，另做受保護的維運命令。

## 啟動與監控

Port 已開不代表應用程式可用。啟動腳本應等 Readiness 通過；監控則需關注狀態改變及持續時間，避免每次輪詢都產生大量 Log。

> [!note] 專案決策邊界
> 專案使用 `/health/live` 與 `/health/ready`；v1 目標以 SQL Server 與必要本機目錄作為 Ready 條件，OpenAI／Brevo可 Degraded。SH-11A 第一階段目前只實作本機資料根目錄可寫探針，SQL／Migration／Hangfire 待 Infrastructure 完成後加入。正式內容見 [[03-架構/Logging與HealthCheck設計]]。

## 參考資料

- [Microsoft Learn：Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)
- [[03-架構/非功能需求]]

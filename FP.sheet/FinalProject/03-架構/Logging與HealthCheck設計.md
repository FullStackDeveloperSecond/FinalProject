---
文件狀態: 已確認
最後更新: 2026-08-15
追蹤項目:
  - TECH-07
---

# Logging 與 Health Check 設計

## Logging

- 使用 Serilog，輸出結構化 JSON 至 Console 與每日 Rolling File。
- HTTP Request Log 至少包含時間、Level、Request Method、Path Template、Status、Elapsed、Correlation ID、Trace ID 及使用者類型。
- HTTP Request 的 Correlation Header 固定為 `X-Correlation-ID`；只接受 1～64 字元 ASCII 英數、`-`、`_`、`.`，無效或缺少時由 API 重建，並在 Response Header 與 Problem Details `correlationId` 回傳。
- 背景工作、Email、OpenAI、模擬付款與物流呼叫沿用同一 Correlation／Trace 關聯。
- 不記錄密碼、Cookie、Anti-forgery Token、API Key、連線字串、完整地址、完整付款資料或未遮蔽 AI 個資。
- Rolling File 保存 14 天；單檔達 100 MB 後切檔，Log 總量達 2 GB 時提出磁碟容量警告。
- 一般 Log 只保存 User／Admin PublicId、角色與遮蔽 IP；完整 IP 僅能在有明確安全或稽核目的的 `AuditLog` 中依授權保存。不得記錄姓名、Email 或地址。

## Health Endpoint

| Endpoint | 內容 | 回應範圍 |
|---|---|---|
| `/health/live` | API 程序能處理請求 | 只回 Healthy／Unhealthy，不列依賴細節 |
| `/health/ready` | SQL Server、必要資料庫 Migration 狀態、必要本機檔案目錄；Hangfire 核心狀態摘要 | 詳細內容只供授權管理員或本機 health-check 腳本 |

- OpenAI、Brevo 或其他可降級外部服務不可讓 Liveness 失敗。
- 外部服務不可用可讓詳細 Readiness 顯示 Degraded，但基本電商仍應可啟動。
- Unhealthy 使用 HTTP 503；Healthy／允許服務的 Degraded 回應不得洩漏連線字串、路徑或例外。
- `start-all.ps1` 以 Readiness 判斷啟動完成，不只檢查 Port 是否開啟。

## 查詢與保存

- Demo 現場主要透過檔案與 `status.ps1` 查詢，不新增外部監控服務相依。
- `critical` Hangfire Job 失敗、SQL Server 不可用或檔案根目錄不可寫時需有明確事件。
- 清理或切檔失敗必須產生可追蹤事件；Demo 前不得以人工刪除 Log 掩蓋錯誤。

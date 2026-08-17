---
文件狀態: 已確認
最後更新: 2026-08-17
追蹤項目:
  - TECH-07
  - DEV-08
  - DEV-04
---

# Logging 與 Health Check 設計

## Logging

- 使用 Serilog，輸出結構化 JSON 至 Console 與每日 Rolling File。
- API 使用 `Serilog.AspNetCore 10.0.0`、`Serilog.Sinks.Console 6.1.1` 與 `Serilog.Sinks.File 7.0.0`；版本由 Central Package Management 統一管理。
- HTTP Request Log 至少包含時間、Level、Request Method、Path Template、Status、Elapsed、Correlation ID、Trace ID 及使用者類型。
- HTTP Request 的 Correlation Header 固定為 `X-Correlation-ID`；只接受 1～64 字元 ASCII 英數、`-`、`_`、`.`，無效或缺少時由 API 重建，並在 Response Header 與 Problem Details `correlationId` 回傳。
- 背景工作、Email、OpenAI、模擬付款與物流呼叫沿用同一 Correlation／Trace 關聯。
- 不記錄密碼、Cookie、Anti-forgery Token、API Key、連線字串、完整地址、完整付款資料或未遮蔽 AI 個資。
- Rolling File 位於 `{Storage:DataRoot}/logs/doselect-api-*.json`，最長保存 14 天；單檔達 100 MB 後切檔，最多保留 20 個檔案，將理論上限控制在約 2 GB。檔案允許診斷工具共享讀取，不允許以共享模式任意寫入。
- `Observability:FileLoggingEnabled=false` 只供測試或明確停用檔案 Sink；Console JSON 仍保留。Development 最低層級為 Debug，其他環境為 Information，ASP.NET Core 框架雜訊最低為 Warning。
- 一般 Log 只保存 User／Admin PublicId、角色與遮蔽 IP；完整 IP 僅能在有明確安全或稽核目的的 `AuditLog` 中依授權保存。不得記錄姓名、Email 或地址。

## Health Endpoint

| Endpoint | 內容 | 回應範圍 |
|---|---|---|
| `/health/live` | API 程序能處理請求 | 只回 Healthy／Unhealthy，不列依賴細節 |
| `/health/ready` | 第一階段驗證 `Storage:DataRoot` 可建立目錄及寫入暫存探針 | 公開端點只回整體 `status`，不列路徑、例外或依賴細節 |

- OpenAI、Brevo 或其他可降級外部服務不可讓 Liveness 失敗。
- 外部服務不可用可讓詳細 Readiness 顯示 Degraded，但基本電商仍應可啟動。
- Unhealthy 使用 HTTP 503；Healthy／允許服務的 Degraded 回應不得洩漏連線字串、路徑或例外。
- `start-all.ps1` 以 Readiness 判斷啟動完成，不只檢查 Port 是否開啟。
- `health-check.ps1` 直接檢查 SQL Server、API Liveness／Readiness、Customer Web 與 Admin Web，不以 PID 存在代替服務健康；必要檢查失敗時回傳非零結束碼，Readiness 的 Degraded 以警告呈現但保留基本電商可用。

### 分階段實作邊界

- SH-11A 已完成 Serilog JSON、Request Logging、`/health/live`、本機資料根目錄 Readiness 與公開安全回應。
- SQL Server 連線、Migration 狀態、Hangfire 核心狀態只在對應 Infrastructure 完成後加入 `/health/ready`，不得用假檢查宣稱已就緒。
- OpenAI 與 Brevo 屬可降級外部依賴；未啟用時不執行檢查，啟用後的詳細 Degraded 訊息仍只供受保護維運介面或本機腳本。
- Hangfire、OpenAI 與 Brevo 尚未接入 Infrastructure 前，`health-check.ps1` 不建立假結果；完成後才擴充對應檢查。

## 查詢與保存

- Demo 現場主要透過檔案與 `status.ps1` 查詢，不新增外部監控服務相依。
- `critical` Hangfire Job 失敗、SQL Server 不可用或檔案根目錄不可寫時需有明確事件。
- 清理或切檔失敗必須產生可追蹤事件；Demo 前不得以人工刪除 Log 掩蓋錯誤。

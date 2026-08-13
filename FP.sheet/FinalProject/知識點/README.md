---
type: knowledge-index
title: 系統知識點
tags:
  - 知識點
---

# 系統知識點

本資料夾整理專案需求、架構與實作過程中反覆出現的專業術語及背景知識。目前共 35 個主題頁。

知識點文件用來協助團隊理解概念，不直接代表專案已確認需求；實際行為仍以需求文件、決策紀錄及未完成項目追蹤表為準。

## 篩選與撰寫規則

專業概念符合下列條件時，才獨立建立知識頁：

1. 在多份需求、架構或決策文件重複出現。
2. 只看名稱不足以理解，容易造成安全、資料一致性或契約誤用。
3. 能跨模組重複使用，而不是單一畫面的欄位說明。

純商業規則、單一設定值、尚未核准的方案及正式決策內容不重複搬入知識頁。每頁應包含概念、適用與不適用情境、常見風險、專案決策邊界、相關正式文件及主要參考資料；正式規則改變時，知識頁只更新摘要與連結，不改寫歷史決策。

## 電商、營運與組裝領域

- [[知識點/SLA|SLA（服務等級協議）]]
- [[知識點/SKU|SKU（庫存單位）]]
- [[知識點/Socket與BIOS|CPU Socket 與 BIOS 相容性]]
- [[知識點/PSU瓦數級距|PSU 瓦數級距與選擇規則]]

## 身分驗證、授權與 Web 安全

- [[知識點/Microsoft Defender掃描規則|Microsoft Defender 檔案掃描規則]]
- [[知識點/ASP.NET Core Identity|ASP.NET Core Identity]]
- [[知識點/CSRF|CSRF（跨站請求偽造）]]
- [[知識點/CORS|CORS（跨來源資源共享）]]
- [[知識點/Credentials|Credentials（Fetch／CORS 認證資料模式）]]
- [[知識點/Antiforgery|Antiforgery（ASP.NET Core 防偽機制）]]
- [[知識點/TOTP|TOTP（驗證器動態密碼）]]
- [[知識點/RBAC與Policy授權|RBAC 與 Policy 授權]]
- [[知識點/Secrets管理|Secrets 管理]]

## API 契約與可觀測性

- [[知識點/DTO與API Schema|DTO 與 API Schema]]
- [[知識點/OpenAPI與Typed Client|OpenAPI 與 Typed Client]]
- [[知識點/Problem Details|Problem Details]]
- [[知識點/Correlation ID與Trace ID|Correlation ID 與 Trace ID]]
- [[知識點/Health Check|Health Check]]
- [[知識點/Cursor分頁|Cursor 分頁]]

## 資料模型與一致性

- [[知識點/資料庫正規化與反正規化|資料庫正規化與反正規化]]
- [[知識點/交易快照|交易快照]]
- [[知識點/PublicId與UUID v7|PublicId 與 UUID v7]]
- [[知識點/SQL Server rowversion|SQL Server rowversion]]
- [[知識點/冪等性|冪等性]]
- [[知識點/Transactional Outbox|Transactional Outbox]]
- [[知識點/Audit Log|Audit Log]]
- [[知識點/軟刪除與Filtered Unique Index|軟刪除與 Filtered Unique Index]]

## 報表與統計

- [[知識點/30天線性迴歸|30 天線性迴歸]]
- [[知識點/Z-Score|Z-score（標準分數）]]

## 前端技術

- [[知識點/PrimeVue|PrimeVue]]
- [[知識點/TanStack Query|TanStack Query]]

## 基礎設施

- [[知識點/Brevo SMTP|Brevo SMTP]]
- [[知識點/Hangfire|Hangfire]]

## AI 與智慧搜尋

- [[知識點/Structured Outputs與JSON Schema|Structured Outputs 與 JSON Schema]]
- [[知識點/Embeddings與向量檢索|Embeddings 與向量檢索]]

## 建議閱讀路徑

- Cookie Web 安全：[[知識點/ASP.NET Core Identity]] → [[知識點/Credentials]] → [[知識點/CORS]] → [[知識點/CSRF]] → [[知識點/Antiforgery]]。
- Typed API：[[知識點/DTO與API Schema]] → [[知識點/OpenAPI與Typed Client]] → [[知識點/Problem Details]] → [[知識點/Correlation ID與Trace ID]]。
- 可靠交易：[[知識點/SQL Server rowversion]] → [[知識點/冪等性]] → [[知識點/Transactional Outbox]] → [[知識點/Hangfire]] → [[知識點/Audit Log]]。
- AI 搜尋：[[知識點/Structured Outputs與JSON Schema]] → [[知識點/Embeddings與向量檢索]]；首版採前者與結構化 SQL，不採向量檢索。

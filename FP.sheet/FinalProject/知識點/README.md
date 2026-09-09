---
type: interview-preparation-index
title: DoSelect 面試準備
tags: [面試, 知識點]
最後更新: 2026-09-09
---

# DoSelect 面試準備

這一區不再當術語百科，而是用 DoSelect 的實際程式準備軟體工程面試。回答時要分清楚「專案做了什麼」與「我親自做了什麼」；沒有參與的部分可以解釋設計，但不可冒充自己的成果。

## 先讀這五份

1. [[知識點/01-專案自我介紹與架構]]：在 1 分鐘內說清楚產品、架構、個人貢獻。
2. [[知識點/03-Web安全與身分驗證]]：Cookie、CSRF、CORS、Identity 是最容易被連續追問的組合。
3. [[知識點/04-資料庫交易與一致性]]：用結帳解釋交易、冪等、`rowversion` 與 Outbox。
4. [[知識點/06-測試與CI-CD]]：說清楚如何證明可靠，以及目前其實只有 CI、沒有正式 CD。
5. [[知識點/09-HR行為題與STAR]]：準備 5～6 個真實故事，不背空泛形容詞。

## 完整教材

- [[知識點/01-專案自我介紹與架構]]
- [[知識點/02-後端與API核心]]
- [[知識點/03-Web安全與身分驗證]]
- [[知識點/04-資料庫交易與一致性]]
- [[知識點/05-Vue與TypeScript前端]]
- [[知識點/06-測試與CI-CD]]
- [[知識點/07-AI背景工作與外部服務]]
- [[知識點/08-系統設計情境題]]
- [[知識點/09-HR行為題與STAR]]
- [[知識點/10-模擬面試與評分表]]
- [[知識點/研究-結構化面試方法]]

深挖硬體相容性時再讀 [[知識點/CPU與晶片組相容性官方證據]]；它是專案規則的來源證據，不是第一輪面試必背內容。

## 專案實際技術矩陣

| 面向 | 偵測到的技術 | 優先度 | 至少要會回答 |
|---|---|---:|---|
| 後端 | C#、.NET 10、ASP.NET Core Web API、DI、Options | P0 | 請求管線、分層、非同步與錯誤處理 |
| 資料 | EF Core 10、SQL Server、Migration | P0 | 追蹤、交易、索引、併發與 N+1 |
| 安全 | Identity、Cookie、Policy、Antiforgery、CORS、Rate Limiting、TOTP | P0 | Authentication/Authorization、CSRF/CORS |
| 前端 | Vue 3.5、TypeScript 5.9、Vite 8、Vue Router、Pinia | P0 | 元件響應性、狀態邊界、路由守衛 |
| Server state | TanStack Vue Query 5 | P0 | cache key、stale、retry、mutation 後失效 |
| UI | PrimeVue 4、Aura、自有 UI wrapper | P1 | 為何包裝元件、版本與樣式治理 |
| API 契約 | OpenAPI、Scalar、openapi-typescript、openapi-fetch | P0 | 產生碼不可手改、wrapper 責任、漂移 gate |
| 可靠性 | 冪等、Transactional Outbox、Hangfire、`rowversion` | P1 | 重送、重試、至少一次投遞與併發衝突 |
| 外部服務 | OpenAI Responses API、Brevo SMTP、MailKit | P1 | timeout、失敗分類、降級、成本與秘密管理 |
| 測試 | xUnit、Vitest、Vue Test Utils、Playwright | P0 | 單元／整合／E2E 各證明什麼 |
| 交付 | GitHub Actions、Gitleaks、npm audit、NuGet vulnerability scan | P0 | CI gate；不要把 CI 說成 CD |
| 觀測 | Serilog、Problem Details、Correlation/Trace ID、Health checks | P1 | 如何從前端錯誤追到後端與資料庫 |

## 不必優先背

- 單一 PSU 瓦數、SKU 定義、Socket 型號表：除非職缺偏電商領域或深挖相容性。
- 套件 API 的每個參數：重點是為何選、失敗模式、替代方案與驗證。
- 資料表或 Controller 數量：容易變動，也不能證明能力。
- 自己沒做過的模組細節：可說「我讀過並能解釋」，不要說成「我實作」。

## 每題通用回答骨架

1. 先下定義或結論。
2. 說 DoSelect 在哪個流程使用它。
3. 解釋取捨，以及不用它會出現什麼失敗。
4. 提出測試或觀測證據。
5. 說出限制與下一步，不把展示版包裝成大型正式營運系統。

## 七天練習

| 日 | 內容 | 完成條件 |
|---|---|---|
| 1 | 專案介紹、架構、個人貢獻 | 不看稿完成 60 秒與 3 分鐘版 |
| 2 | 後端、API、安全 | 能畫出登入與 unsafe request 流程 |
| 3 | SQL、EF Core、結帳一致性 | 能解釋兩次送出為何不產生兩張訂單 |
| 4 | Vue、Typed Client、Query cache | 能說清 Pinia 與 TanStack Query 分工 |
| 5 | 測試、CI、外部服務、AI | 每層各舉一個測試；說明降級 |
| 6 | HR 行為題 | 完成 6 張 STAR 故事卡 |
| 7 | 兩輪模擬面試 | 找出最低兩項並重練 |

## 證據範圍

本教材以 2026-09-09 目前工作樹的 `FP.dev`、`.github/workflows/ci.yml`、測試與正式文件為準；`.worktrees`、`bin`、`obj`、`node_modules` 與產生碼不作技術現況判定。技術改變後應重新核對 manifest、入口點與 CI。

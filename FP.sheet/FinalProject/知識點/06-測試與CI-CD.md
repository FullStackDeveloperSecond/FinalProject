# 測試與 CI/CD

## 精確結論

Repository 偵測到完整 CI，但沒有正式部署 job。因此面試應說「有 CI 與可重現的本機/Demo 啟動流程，尚未完成正式 CD」，不能把 GitHub Actions 統稱 CI/CD 後就宣稱自動部署。

## 測試層級

| 層級 | 工具 | 適合證明 | 不足以證明 |
|---|---|---|---|
| Domain/Application unit | xUnit | 純規則、分支、錯誤語意 | SQL 翻譯、middleware、真實交易 |
| Infrastructure/API integration | xUnit、test host、SQL Server | DI、HTTP、Identity、EF、transaction | 真實瀏覽器 UX |
| Frontend unit/component | Vitest、Vue Test Utils | composable、component、router、wrapper | 前後端真的一起運作 |
| Browser E2E | Playwright + disposable DB | 關鍵旅程、Cookie、Antiforgery、路由 | 所有邊界與高負載 |
| AI evaluation | `DoSelect.AiEvals` + dataset | schema、拒答、降級、品質基線 | 未跑 live provider 時的真實模型品質 |

## CI 實際 gate

1. Gitleaks 掃完整 Git history。
2. AI evaluation 產物與 dataset contract。
3. Package source、script resolution、備份與環境隔離安全檢查。
4. 啟動 SQL Server，restore/build/format、Migration、model snapshot。
5. 重產 OpenAPI TypeScript client，確認無未提交 diff。
6. .NET tests、獨立 timing side-channel tests、核心層 70% line coverage、vulnerability report。
7. customer/admin matrix：locked install、typecheck、zero-warning lint、coverage、build、dist secret scan、dependency audit。
8. Playwright Chromium journeys；失敗上傳 report。
9. `CI Required` 聚合必要 job。

## 高機率題

### Unit 與 integration 怎麼分？

看邊界。純規則用 unit；SQL Server `rowversion`、transaction、Identity store、middleware/filter、OpenAPI contract 用 integration。EF InMemory 不代表 SQL Server。

### Coverage 70% 代表品質好？

不代表。Coverage 只表示程式被執行，不能證明 assertion、重要情境或 race。Gate 防核心層大幅漏測，仍需風險導向、negative、provider-backed 與 E2E。

### Flaky test 怎麼處理？

保留失敗證據、seed、時間、環境；查共享 DB、clock、random、network、背景工作、等待條件。修隔離與可控 clock，不用無限 retry 掩蓋。

### CI 太慢？

先量 job。平行互不依賴項目、cache 鎖定相依、靜態檢查 fail fast。不要刪掉唯一能抓 SQL/browser 問題的測試；可依風險分 PR 必跑與 nightly 擴充集。

### CD 還缺什麼？

不可變 artifact、環境 secret、migration 策略、health/readiness gate、逐步發布、監控、rollback、部署後 smoke test；資料庫需向前／向後相容。

## 情境題

「CI 綠但正式 migration 失敗」：停止擴大部署，保留 logs/version；判斷 app/schema 是否部分發布；依演練過的 rollback/forward-fix。改善 staging rehearsal、備份還原、相容 migration、權限/timeout、部署 health gate。

## 專案證據入口

- `.github/workflows/ci.yml`
- `FP.dev/tests`
- `FP.dev/frontend/customer-web/e2e`
- `FP.dev/scripts/verify-backend-coverage.ps1`

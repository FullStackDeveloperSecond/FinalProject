---
type: knowledge
title: CI 與 CD
aliases:
  - CI/CD
  - Continuous Integration
  - Continuous Delivery
  - Continuous Deployment
  - 持續整合與持續交付
tags:
  - 知識點
  - CI
  - CD
  - GitHub Actions
  - 自動化測試
  - 部署
created_at: 2026-08-18
updated_at: 2026-08-25
related:
  - "[[03-架構/08-測試與驗收/測試策略]]"
  - "[[03-架構/01-系統與環境/本機開發環境與版本基線]]"
  - "[[Git協作規範]]"
  - "[[知識點/03-API契約與可觀測性/OpenAPI與Typed Client]]"
  - "[[知識點/02-身分授權與Web安全/Secrets管理]]"
---

# CI 與 CD

## 名稱與差異

CI（Continuous Integration，持續整合）是在每次 Push 或 Pull Request 時，自動還原相依套件、建置、檢查格式並執行測試，及早發現不同成員的程式整合後是否失敗。

CD 有兩種常見含義：

| 名稱 | 意義 |
|---|---|
| Continuous Delivery（持續交付） | 通過 CI 後產生可部署 Artifact；部署至正式環境仍需人工核准 |
| Continuous Deployment（持續部署） | 通過所有閘門後自動部署至正式環境，不再等待人工核准 |

因此「有 GitHub Actions」不代表已完成 CD。只執行 Build 與 Test 是 CI；必須產生可追蹤 Artifact，並有環境、部署、驗證及回復流程，才形成完整交付管線。

## 基本流程

```text
Commit／Pull Request
→ Restore／Install
→ Format／Lint／Typecheck
→ Build
→ Unit／Integration／Contract／E2E Tests
→ Security／Dependency／Secret Checks
→ 產生不可變 Artifact
→ 部署至目標環境
→ Migration／Health Check／Smoke Test
→ 成功完成，或 Rollback／Roll-forward
```

前半段主要是 CI，從 Artifact、環境核准到部署驗證屬於 CD。每一層應失敗即停止，不能讓失敗的 Build 繼續部署。

## CI 的品質閘門

CI 應驗證 Repository 中可重現的結果，而不是自動修正後直接 Commit：

- 鎖定 SDK、Node 與套件版本，使用 `dotnet restore`、`npm ci` 等可重現命令。
- 編譯、Lint、Typecheck、格式或測試失敗時阻止合併。
- OpenAPI、產生碼或 AI 評估 Fixture 必須重新產生後確認 Git Diff 為空。
- 高風險功能需有授權、交易、Migration、Secret 與供應鏈檢查，不能只看 Coverage 百分比。
- 使用穩定的彙總 Check 供 Branch Protection 綁定，避免 Job 改名後保護規則失效。

CI 結果只證明本次自動檢查通過，不等於「程式沒有任何 Bug」，也不能把資料契約通過誤寫成 AI 模型品質已通過。

## Artifact 與環境晉級

Artifact 是 Workflow 產生並可保存或交給後續 Job 的檔案，例如後端發行包、前端靜態檔、測試報告、Coverage、Migration SQL 或部署 Manifest。

理想做法是「Build once, promote the same artifact」：測試、展示與正式環境使用同一份經驗證產物，只由環境設定及 Secret 決定連線目標。若每個環境都重新 Build，可能部署到與 CI 實際測過不同的內容。

每個 Artifact 至少要能追溯 Commit SHA、Workflow Run、版本及產生時間；不要把資料庫密碼、API Key、User Secrets 或未遮蔽測試資料包進 Artifact。

## CD 的安全閘門

部署 Job 應指定明確 Environment，例如 `demo`、`staging`、`production`，並視風險設定：

- 只允許受保護 Branch 或 Tag 部署。
- 正式環境需要人工核准，且避免部署者自行核准。
- Environment Secret 只在通過保護規則後提供給該 Job。
- 同一環境使用 Concurrency，避免兩次部署同時修改服務或資料庫。
- 部署後執行 Readiness／Smoke Test，失敗立即停止並告警。
- 保存部署紀錄、版本及回復方式。

Production Secret 不應提供給 Pull Request CI；能使用短期身分或 OIDC 時，不使用長期雲端憑證。第三方 Action 應採最小權限，並以完整 Commit SHA 固定不可變版本。

## 資料庫 Migration

應用程式部署可替換檔案，資料庫變更卻可能不可逆，因此 Migration 需要額外 Gate：

1. 產生並審查 Migration 與正式 SQL。
2. 檢查 Drop、Rename、Nullability、Index、鎖定時間及資料轉換風險。
3. 部署前執行 Preflight、備份或建立 Roll-forward 計畫。
4. 先採向後相容 Schema，再切換應用程式；大型 Backfill 分批執行。
5. 部署後驗證 Schema、資料、Health Check 與核心交易。

不應讓應用程式啟動時在未審查下自動修改正式資料庫，也不能假定退回舊版程式就會自動還原資料。

## 本專案目前狀態

`.github/workflows/ci.yml` 已實作 GitHub Actions CI：

| Job／規則 | 目前內容 |
|---|---|
| `AI Evaluation Contract` | 驗證 120 筆評估資料、產物時效與契約；不呼叫 OpenAI |
| `Backend` | Restore、`-warnaserror` Build、Format Verify、.NET Test、NuGet 弱點報告 |
| `Frontend` Matrix | 兩個 Vue 專案分別執行 `npm ci`、Typecheck、零警告 Lint、Vitest、Build、Gitleaks `dist` 掃描與 Production Dependency Audit |
| `Secret Scan` | Gitleaks CLI `8.30.1` 經官方 SHA-256 校驗後掃描完整 Git 歷史；輸出使用 `--redact` |
| `CI Required` | 彙總 Secret Scan、AI Evaluation Contract、Backend 與兩個 Frontend；供 `main`／`dev` Branch Protection 使用 |
| 權限與供應鏈 | `contents: read`、Checkout 不保留 Credential、官方 Action 固定完整 SHA |
| 外部依賴 | 基礎 CI 不取得 SQL、OpenAI、Brevo 或其他 Secret |

Workflow 在指向 `main`／`dev` 的 Pull Request、Push 與手動觸發時執行；同一分支的新 Commit 會取消已過期的舊執行。

目前尚未完成正式 CD。本專案第一版只需在單一 Windows 展示電腦執行，不部署公網；自動 Secret Scanner 已落實，Coverage 失敗門檻、OpenAPI Client Diff、SQL Server Provider-backed 整合測試、部署 Artifact、Environment、Migration Deployment 及自動回復仍須依追蹤項目逐步落實。

> [!note] 專案決策邊界
> 已完成的是 GitHub Actions 基礎 CI 與 Branch Protection，不是正式環境自動部署。實際通過項目及尚待工作以 [[03-架構/08-測試與驗收/測試策略]]、[[03-架構/01-系統與環境/本機開發環境與版本基線]] 和 [[05-規劃/01-時程與進度/未完成項目追蹤表]] 為準。

## 常見誤區

- 在 CI 使用開發或正式資料庫，造成測試污染真實資料。
- 把部署 Secret 放進 Repository、Log、Artifact 或 Pull Request Workflow。
- CI 自動格式化並 Commit，讓未經審查的內容進入分支。
- 每個 Job 都重新建置，最後部署的 Artifact 不是測試過的版本。
- Migration 與應用程式一次做破壞性切換，失敗時無法安全回復。
- 只看綠色 Check，卻沒有確認必要 Job 是否真的被彙總及 Branch Protection 強制。

## 參考資料

- [GitHub Docs：GitHub Actions Quickstart](https://docs.github.com/en/actions/get-started/quickstart)
- [GitHub Docs：Workflow artifacts](https://docs.github.com/en/actions/concepts/workflows-and-actions/workflow-artifacts)
- [GitHub Docs：Deployments and environments](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)
- [GitHub Docs：Secure use reference](https://docs.github.com/en/actions/reference/security/secure-use)

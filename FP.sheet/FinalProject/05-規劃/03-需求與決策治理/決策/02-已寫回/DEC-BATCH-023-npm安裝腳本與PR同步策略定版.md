---
type: decision-record
batch_id: DEC-BATCH-023
title: npm 安裝腳本與 PR 同步策略定版
status: applied
created_at: 2026-08-25
submitted_at: 2026-08-25
applied_at: 2026-08-25
decision_count: 4
decision_range: DEC-P303～DEC-P306
source: alex 採用 D1-A、D2-A、D3-A、D4-A 並授權紀錄
---

# DEC-BATCH-023｜npm 安裝腳本與 PR 同步策略定版

## 背景

QA-08 Package 來源證據流程已驗證官方 Registry、精確版本、Lock 來源與 Integrity，但雙 Vue 專案執行 `npm ci` 時仍出現兩個提示：間接開發相依 `glob@10.5.0` 已棄用，以及 `vue-demi@0.14.10` 的安裝腳本尚未核准。前者來自仍為最新版的 `@vue/test-utils@2.4.11` 經 `js-beautify@1.15.4` 間接帶入；後者可使用 npm 內建的精確核准與嚴格政策處理。PR #41 同時落後 `dev`，但仍在 Review 階段，過早同步會增加重複整合成本。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P303 | 接受目前間接、僅開發期使用的 `glob@10.5.0` 棄用提示，不使用 `overrides` 強制跨主要版本、不移除仍實際使用的 `@vue/test-utils`，也不為消除提示重寫測試。任一條件發生時重新評估：官方弱點／安全公告出現、`npm audit` 不再為 0、`@vue/test-utils` 更新並移除該相依鏈，或 `glob@10.5.0` 進入正式相依。 |
| DEC-P304 | Customer Web 與 Admin Web 均以 `package.json` 的 `allowScripts` 精確核准 `vue-demi@0.14.10`；不得改成只核准套件名稱、`--all` 或其他廣泛核准。版本變更時必須重新檢視安裝腳本並重新核准精確版本。 |
| DEC-P305 | Repository 根 `FP.dev/.npmrc` 固定 `strict-allow-scripts=true`。`dangerously-allow-all-scripts=true` 禁止使用；CI 必須驗證嚴格模式存在且唯一、拒絕關閉嚴格模式，並拒絕未包含精確版本的 `allowScripts` 鍵。新增含安裝腳本的套件前，先核對官方來源、腳本目的與精確版本，再更新核准清單。 |
| DEC-P306 | PR #41 在 Review 完成且準備合併前維持目前分支，不為消除 `BEHIND` 提前同步。準備合併時將最新 `origin/dev` merge 進分支、重新執行完整 CI，確認 Review 與 Required Checks 後才 Squash Merge；不得因落後而 Bypass，合併後依既定規則刪除分支。 |

## 最低成本分析

1. 完全不處理安裝腳本提示：無法阻止新出現但未審查的 install script，不能滿足供應鏈驗收，不採用。
2. 保留目前 `glob` 提示並以重評條件追蹤：它是開發期的間接相依、目前漏洞稽核為 0，且上游最新版仍保留該相依鏈；這已足以處理非阻擋風險，採用。
3. 沿用 npm 內建 `allowScripts`、`strict-allow-scripts` 與現有 Package 來源 CI：可精確核准唯一已知腳本並讓未審查腳本 Fail Closed，不新增套件、服務或維運面，採用。
4. 強制 `overrides`、替換測試框架或增加另一套供應鏈工具：會引入不必要的相容性、維護與導入成本，現階段沒有證據顯示較低成本方案不足，不採用。

## 商業影響

- 受影響者：兩個 Vue 前端的開發者、Reviewer 與 CI 維護者。
- 目前風險：未審查的相依安裝腳本可能在開發機或 CI 執行；PR 過早同步則造成額外衝突與重複驗證。
- 觸及頻率：每次 `npm ci`、每次新增／升級含安裝腳本的套件，以及 PR #41 最終合併前。
- 預期成果：雙前端乾淨安裝不再出現待核准腳本提示；任何未精確核准的安裝腳本使安裝失敗；PR #41 只做一次接近合併時的最新基底驗證。
- 建置與持續成本：調整既有設定、兩份 Manifest、驗證器、自我測試及文件；未新增依賴、服務、Schema 或持續費用。未來新增安裝腳本套件需一次人工審查。
- 主要風險成本：過度廣泛核准會弱化供應鏈防線；過度強制間接相依升級可能使測試工具不相容。
- 信心：高；採用 npm 官方內建控制，且本機雙 `npm ci` 已證明精確核准可正常安裝。
- 成功指標：雙前端 `npm ci` 成功且不再出現 `vue-demi` 待核准提示；嚴格模式關閉與非版本化核准 Fixture 均被拒絕；完整 CI 成功。
- 停止／回復條件：若精確核准造成正式安裝或 CI 不可恢復失敗，先移除該套件版本或停用相關功能，不得改用全域允許；政策調整必須以新決策覆寫。PR 同步出現無法安全解決的衝突時停止合併並交由 Owner 裁定。

## 寫回範圍

- [[03-架構/04-安全與檔案/安全與供應鏈強制驗收標準]]
- [[03-架構/08-測試與驗收/測試策略]]
- [[知識點/07-基礎設施與交付/CI與CD]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]
- `FP.dev/.npmrc`
- `FP.dev/frontend/customer-web/package.json`
- `FP.dev/frontend/admin-web/package.json`
- `FP.dev/scripts/verify-package-sources.ps1`
- `FP.dev/scripts/test-package-source-verifier.ps1`

## 實作與合併 Gate

- Customer Web 與 Admin Web 的 `npm ci` 必須成功，且只能保留 DEC-P303 接受的既有 `glob@10.5.0` 棄用提示，不得再出現未核准 install script 提示。
- Package 來源正向驗證必須通過；非官方來源、關閉 `strict-allow-scripts` 與非版本化 `allowScripts` 核准的 Fixture 必須失敗。
- PR #41 本輪只提交與取得最新 CI，不在 Review 完成前同步 `dev` 或合併。
- 準備合併時依 DEC-P306 merge 最新 `origin/dev`、重跑 Required Checks、取得 Review 後 Squash Merge，不使用 Bypass。

## 參考依據

- [npm approve-scripts 官方文件](https://docs.npmjs.com/cli/v11/commands/npm-approve-scripts/)
- [npm install 與 strict-allow-scripts 官方文件](https://docs.npmjs.com/cli/install/)

---
文件狀態: 已確認

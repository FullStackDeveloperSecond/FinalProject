---
batch_id: DEC-BATCH-036
status: applied
decision_date: 2026-08-31
decision_ids:
  - DEC-P342
---

# DEC-BATCH-036｜PrimeVue 版本與授權基線定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P342 | 前台與後台確定使用 PrimeVue，並統一精確鎖定仍以 MIT 發布的 `PrimeVue 4.5.5`。既定藍白視覺、頁面配置、操作流程、Styled Mode、Aura Preset 與 `ui/` 包裝層方向不變。開始新增 PrimeVue 元件前，必須先將 Customer Web 與 Admin Web 的 `package.json`／`package-lock.json` 從 `5.0.1` 對齊至 `4.5.5`，確認不再解析 PrimeUI 5 授權管理套件，並完成雙前端 typecheck、零警告 lint、完整測試、production build 與 production dependency audit。不得隱藏授權提示、修改套件或提交授權金鑰。只有出現 `4.5.5` 無法滿足的具體必要元件、Vue 相容性或安全維護證據時，才另案評估 PrimeVue 5 與 Community／Commercial License；不得自行切回 5。 |

本決策補充 DEC-P36「前後台採用 PrimeVue」的精確版本與授權邊界，不改變既有 UI 元件庫選型。

## Lowest-Cost Analysis

1. 維持目前 `PrimeVue 5.0.1` 且不處理授權：無法滿足授權合規與無提示的開發基線，未採用。
2. 只以文件要求組員各自申請 PrimeUI Community License：仍會增加每位開發者的資格確認、年度更新與金鑰管理，且目前尚無 PrimeVue 程式引用，未採用。
3. 重用既有 PrimeVue 選型，在任何元件實作前把雙前端精確調整為 MIT 的 `4.5.5`：不改產品功能、API、路由、Schema 或既定畫面方向即可滿足授權與交付要求，採用。
4. 改用另一套 UI 元件庫或自行重做全部控制項：會擴大設計、包裝、測試與維護成本，現階段沒有必要，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | Customer Web／Admin Web 開發者、畫面設計負責人、Demo 與原始碼交付人員 |
| 現況風險 | 兩個 Manifest 目前鎖定 PrimeVue 5.0.1；若直接開始使用，將引入 PrimeUI 授權資格、金鑰及開發提示管理，並讓後續降版產生返工 |
| 影響範圍／頻率 | 兩個 Vue 應用的每次安裝、建置、測試及後續所有 PrimeVue 元件實作 |
| 預期可量測結果 | 雙前端解析 `primevue@4.5.5`；不解析 PrimeUI 5 license manager；既定頁面與流程不變；雙前端品質 Gate 全部通過 |
| 建置／持續成本 | 一次調整兩份 Manifest 與 lockfile、確認 Aura／元件 API 及重跑既有前端 Gate；不新增 Schema、服務或週期性授權費 |
| 風險成本 | 4.5.5 是最後的 MIT 主要版本，未來不保證取得新功能或持續更新；若出現必要相容性或安全問題，需重新裁定版本與授權 |
| 信心 | 授權方向高；專案目前無 PrimeVue source import，因此既有功能回歸風險低，但實際安裝與主題相容性仍須由驗證結果確認 |
| 成功指標 | 精確版本與 lockfile 一致；無 PrimeUI 5 授權相依；typecheck、lint、test、build、audit 全綠；既定藍白視覺可由共用 Preset 落實 |
| 停止／回復條件 | 若 4.5.5 缺少不可替代的必要能力、與現行 Vue／Vite 不相容，或出現不可接受的安全風險，停止導入並提交具體證據，另案評估 PrimeVue 5 授權或替代方案 |

## 影響文件

- [[03-架構/01-系統與環境/本機開發環境與版本基線]]
- [[03-架構/01-系統與環境/系統架構]]
- [[知識點/06-前端/PrimeVue]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- `FP.dev/README.md`

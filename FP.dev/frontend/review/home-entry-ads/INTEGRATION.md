# PR #82：2026-09-06 視覺整合驗證

已 rebase `dev` 的 `6741037`。既有視覺提交整理為完整變更，原歷史保留於本機備份分支。

## 本次畫面

- 前台使用 3C 電腦城市背景、電路網格與城市風格卡片；Donngu 常駐導覽可點擊頭像收合，並記住選擇。
- 首頁「說用途 → 給預算 → 看推薦」移入主視覺，整張卡片可點擊，移除舊 CTA 與下方重複內容。
- 首頁新增三則主題廣告，支援自動輪播、箭頭、圓點、暫停；滑鼠／鍵盤停留及 reduced-motion 下不自動切換。文案與連結集中於 customer-web/src/components/homePromotions.ts。
- 後台依客服、商品、庫存、營運物流、財務分色與圖示呈現；側欄分類可收合，當前頁面自動展開所屬分類，維持角色權限過濾。

[首頁桌機](../home-entry-ads/desktop.png) · [首頁手機](../home-entry-ads/home-360.png) · [後台畫面](../admin-navigation/README.md) · [城市與 Donngu](../city-0906/README.md)

## dev 整合

保留最新客服 SLA／公開回覆、退款 E2E、AI 評估及共用 UiButton 等功能。保留共用 ui 與 brand.css 匯出，應用程式沿用完整 DoSelectPreset；PrimeVue 4.5.5 搭配 themes 1.2.5，避免分裂 styled 引擎。

實際 E2E 發現並修正 SLA 捲動容器內固定表頭遮住第一筆案件：表頭相對容器置頂，不再使用頁面 header 偏移。首頁旅程改點新版「給預算」，AI 客服旅程實際收合 Donngu 後操作同意設定。

## 最終本機結果

- customer：637 tests；核心 line coverage 88.67%。
- admin：483 tests；核心 line coverage 72.90%。
- npm ci、零警告 lint、typecheck、production build 通過；npm audit 無漏洞。
- Production Chromium 21/21 旅程通過，含最新客服、TOTP、COD、退款與折讓；資料庫由測試建立及清理，未使用個人開發資料庫。
- .NET solution build（warnings as errors）及 format 驗證通過。
- OpenAPI export（正規化本機驗證連接埠後）與 generated client 無契約差異。
- AI 120 筆資料集及生成驗證、套件來源與拒絕路徑檢查通過。Windows CRLF 的生成檔經還原 LF 後無 Git 內容差異。
- Gitleaks 完整歷史 252 commits、前後台正式產物無命中。
- 首頁 360／768／1440px 無水平溢出，三入口、廣告切換及 reduced-motion 檢查通過。

GitHub CI 以 PR 最新提交的 Checks 為準。畫面證據是本機版本；後台免登入畫面使用獨立瀏覽器的角色 fixture，未更動正式權限。付款、物流、發票沿用專題模擬流程，不呼叫外部 AI 或 Email。

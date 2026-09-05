# PR #82：0905 視覺復原與整合驗收

此目錄取代 PR 舊說明中已不存在的 brand-v1～v4、reference-aligned 與影片證據。舊檔案不視為本次驗收結果。畫面來自目前 Vue 應用的 production preview，使用隔離 SQL Server 測試資料庫。

## 本次修正

- 整合最新 dev 的商品圖片、庫存對帳、退貨審核欄位與 COD 出貨流程。
- 後台導覽與 route guard 共用角色規則；加入手機收合選單、主要內容跳轉及庫存表格鍵盤捲動區。
- 商品分類／品牌顯示可見標籤與「全部」預設值；商品圖片失敗時提供替代狀態，換圖後可恢復。
- AI 既有零件改用站內商品選擇器與中文分類／規格，未填完的零件會阻止搜尋，避免靜默丟失使用者輸入。
- 登入入口導向登入頁；結帳與會員表單統一 tokens、控制項高度與間距。
- 開發用動效面板預設收合；production 不包含該面板。

## 畫面索引

每組包含 360、768、1280px，總計 78 張。截圖等待字型及載入狀態完成，啟用 reduced-motion，並檢查頁面沒有水平溢位。

| 範圍 | 手機代表畫面 |
|---|---|
| 首頁 | [首頁](screenshots/real-customer-home-360.png) |
| 商品與 AI | [商品](screenshots/real-customer-products-360.png)、[AI 搜尋](screenshots/real-customer-ai-search-360.png) |
| 結帳 | [結帳](screenshots/real-customer-checkout-360.png)、[付款完成](screenshots/real-customer-payment-complete-360.png) |
| 會員 | [資料](screenshots/real-member-profile-360.png)、[地址](screenshots/real-member-addresses-360.png) |
| 客服 | [長訊息](screenshots/real-member-support-long-message-360.png)、[取消後](screenshots/real-member-support-cancelled-360.png) |
| 後台 | [工作台](screenshots/real-admin-dashboard-360.png)、[庫存](screenshots/real-admin-inventory-360.png)、[案件](screenshots/real-admin-case-workbench-360.png) |
| 驗證 | [TOTP 綁定](screenshots/real-admin-totp-enroll-360.png)、[復原碼遮罩](screenshots/real-admin-recovery-codes-redacted-360.png)、[錯誤驗證碼](screenshots/real-admin-totp-invalid-360.png) |

密碼、TOTP 金鑰、QR code、復原碼均已遮罩。失敗 trace、錄影、登入狀態與原始測試輸出不放入版本庫。

## 可重現方式

先依專案指南準備 Node 24、.NET 10、SQL Server 2025、EF tool 與 npm dependencies，再從 `FP.dev` 執行：

```powershell
$env:E2E_PRODUCTION_PREVIEW = 'true'
$env:VISUAL_REVIEW_DIR = Join-Path $PWD 'frontend/review/pr82-0905/screenshots'
./scripts/test-customer-e2e.ps1 -All
```

腳本建立隨機命名的 `DoSelectE2E_*` 資料庫，完成後清理該隔離資料庫。請先停止使用 5126、5173、5174 的開發伺服器。平日不設定 `VISUAL_REVIEW_DIR` 就不會覆寫截圖。

## 驗證範圍

- 前台：631 tests；後台：477 tests。typecheck、零警告 lint、production build 通過。核心 line coverage：88.67%／72.90%。
- 後端：3,073 tests（Domain 490、Application 608、Infrastructure 1,177、API 798）。Domain + Application line coverage 88.49%，高於 70% gate。
- OpenAPI export／generation 無差異；套件來源與拒絕路徑、120 筆 AI evaluation contract、格式與漏洞檢查通過。
- Gitleaks 8.30.1：Git 歷史與前後台 production bundle 無命中。
- 真實旅程涵蓋會員登入、訪客驗證／訂單／取消、商品、客服建立與取消、TOTP、購物車／結帳／模擬付款／發票、COD 宅配與門市取貨。
- Production Chromium：16 條旅程全部通過（包含下列鍵盤與縮放測試）。
- 鍵盤 Skip link、Enter 導覽、reduced-motion 與 200% CSS 內容縮放另有瀏覽器測試；此縮放測試不等同所有瀏覽器原生縮放及所有輔助工具認證。
- 財務總覽及評價審核的既有 E2E 使用 route fixtures。客服附件錯誤與 409、退貨狀態／禁用動作由頁面單元測試覆蓋；空白清單的截圖不宣稱涵蓋每種真實售後狀態。

付款、發票及物流是專案既有模擬流程；AI 與 Email 未呼叫外部服務。這份證據針對本 PR 視覺與整合範圍，團隊尚未開發的功能仍依實作矩陣排程。

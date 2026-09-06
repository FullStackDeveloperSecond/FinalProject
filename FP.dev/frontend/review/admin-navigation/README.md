# 後台分類導覽與圖示優化

首頁及側欄使用同一份 navigation.ts 分類資料，並繼續透過 canAccessAdminPage 過濾權限。分類包括：紫色客服與售後、藍色商品管理、青綠庫存管理、橘色營運與物流、玫紅財務與報表。每個分類有名稱、圖示與框線，辨識不僅依賴顏色。

側欄在首頁預設收合；點分類標題或下箭頭可展開，進入功能頁時自動展開所屬分類。按鈕具備 aria-expanded／aria-controls，隱藏項目不進入鍵盤 Tab 順序。手機沿用管理選單按鈕，展開區域可獨立捲動。

图示為本次直接繪製的 SVG 線條，參考 [Magnific Icons](https://www.magnific.com/icons) 的一致線條圖示方向；沒有下載或嵌入第三方付費素材，沒有新增圖示套件。圖示為文字的輔助，對讀屏隱藏。

## 免登入畫面

- [桌面第一屏](desktop-viewport.png)
- [完整工作台與展開選單](desktop-expanded.png)
- [側欄收合](desktop-collapsed.png)
- [手機展開選單](mobile-navigation.png)
- [手機工作台](dashboard-360.png)
- [僅客服角色的畫面](customer-service-role.png)

圖片来自 production build；使用隔離 Playwright 瀏覽器的示範 session，只為驗證視覺與角色過濾，沒有修改本機帳號、資料庫、登入 guard 或後端授權。

啟動 admin-web preview 於 5176 後，在 FP.dev 執行 `node scripts/review-admin-navigation.mjs` 可重現。瀏覽器驗收包括 Enter／空白鍵收合、360／768／1280px 無水平溢位、手機選單、客服角色分類過濾。單元測試另驗證路由切換自動展開目前分類。

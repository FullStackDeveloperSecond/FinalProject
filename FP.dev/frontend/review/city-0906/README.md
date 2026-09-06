# 電腦城市與 Donngu：第一版設計預覽

使用 Kefan 提供的城市插畫，套用於首頁入口與前台內頁街景；以青綠色、電路網格、立體底座卡片和深色頁尾延續城市主題。樣式僅由 customer-web 載入。

Donngu 常駐前台，依路由介紹商品、組裝、會員、結帳、訂單與客服功能。點頭像可以開關；關閉後記住選擇，換頁不強制重開。支援 Escape 關閉並將焦點交回頭像，對話框為非模態，不搶鍵盤焦點。客服連結通往既有客服中心，不新增外部 AI 呼叫。

## 畫面

- [首頁桌面與 Donngu](home-desktop.png)
- [首頁手機與 Donngu](home-mobile-guide.png)
- [商品桌面與 Donngu](products-desktop.png)
- [AI 搜尋](ai-1440.png)
- [登入](login-360.png)
- [客服中心](support-1440.png)

production 預覽截圖使用本機測試資料，不包含開發動效面板。首頁、商品、AI、登入、客服共 15 個頁面／寬度組合（360、768、1440）無水平溢位，另驗證 Donngu 開關、儲存記憶、跨頁文字及 Escape 焦點回復。前台 632 tests、核心 coverage 88.67%、typecheck、lint、build 通過。

## 重現

在 `FP.dev` 啟動 API 與 customer-web production preview（5173），再執行 `node scripts/review-city.mjs`。圖片會寫入本目錄；preview 使用專案既有本機連線設定。

城市素材：使用者提供的 `Gemini_Generated_Image_4lccvg4lccvg4lcc.jpg`、`Gemini_Generated_Image_cjhn0gcjhn0gcjhn.jpg`，複製至 public/brand 的 city-interior.jpg／city-map.jpg。Donngu 使用專案既有透明底 donggu-hero-wave.png。未額外生成素材。

這是本機設計提案，尚未推送覆蓋 PR #82。之前 PR 的 CI 成功結果不代表這個尚未推送版本的遠端 CI。

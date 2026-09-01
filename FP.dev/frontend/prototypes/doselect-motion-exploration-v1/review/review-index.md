# GSAP 動態方案 A／B／C — 離線比較素材索引

錄製日期：2026-09-01　·　分支 `feature/gsap-motion-exploration-v1`　·　基底 `e0dbf8ba`

## 錄製條件（三套方案完全一致）

| 項目 | 值 |
|---|---|
| 瀏覽器 | 本機已安裝 Chrome（Playwright 1.62.1 `channel: chrome`，headless） |
| 前台 | http://localhost:5173（Vite dev） |
| API | http://localhost:5126（Development，背景工作關閉，未 migrate／未 seed） |
| 桌面視窗 | 1280×900 |
| 行動視窗 | 375×812 |
| 資料 | 隔離測試帳號名下的兩筆隔離測試案件（長對話 14 則訊息、短對話 2 則），錄製後已連同帳號一併刪除 |
| 方案切換 | 網址 `?motion=gentle｜donggu｜crisp`（dev-only，production 不存在） |
| 影片 | WebM／VP8，原速 |
| Contact sheet | 情境 1／2／3／5 為 GSAP `timeScale(0.25)` 慢速取樣，其餘原速；影片一律原速 |

> **格式說明**：Playwright 內附的 ffmpeg 是精簡建置，只含 libvpx（VP8）編碼器，沒有 libx264 與 gif，因此本輪無法產出 MP4／GIF。WebM 可直接在 Chrome／Edge／Firefox 播放。若需要 MP4／GIF，需另外安裝完整版 ffmpeg。

## 素材清單（24 段影片 + 24 張 contact sheet）

### A — Gentle Guidance

進場 `0.34s / power2.out / y 10px / scale 1`　·　stagger `0.045s 間隔，上限 12`　·　面板 `0.42s / power2.out / x 14px（收起 0.24s / power1.inOut）`　·　回饋 `0.28s / power1.inOut`

| # | 頁面／情境 | 視窗 | 操作內容 | reduced-motion | 影片 | Contact sheet | 量測 |
|---|---|---|---|---|---|---|---|
| 1 | 前台首頁（桌面） | 1280x900 | 載入首頁後重新導覽一次，錄 hero 淡入與三步驟／分類卡 stagger | 停用 | [`gentle-1-home-1280.webm`](videos/gentle-1-home-1280.webm) 661 KB | [`gentle-1-home-1280.png`](contact-sheets/gentle-1-home-1280.png) 185 KB | 溢位 0px |
| 2 | 前台首頁（375px） | 375x812 | 同上，於 375×812 檢視，並量測水平溢位 | 停用 | [`gentle-2-home-375.webm`](videos/gentle-2-home-375.webm) 287 KB | [`gentle-2-home-375.png`](contact-sheets/gentle-2-home-375.png) 593 KB | 溢位 0px |
| 3 | 客服案件開啟 2/5 : 3/5 詳細 | 1280x900 | 客服案件列表 → 點第一列「在此檢視」，錄詳細欄由右側展開 | 停用 | [`gentle-3-case-panel-open.webm`](videos/gentle-3-case-panel-open.webm) 381 KB | [`gentle-3-case-panel-open.png`](contact-sheets/gentle-3-case-panel-open.png) 227 KB | 溢位 0px；列表佔 0.4（451:677）, dialog=false |
| 4 | 長對話進場與捲動 | 1280x900 | 開啟長對話案件後，分 8 次逐步捲動詳細欄 | 停用 | [`gentle-4-long-thread.webm`](videos/gentle-4-long-thread.webm) 425 KB | [`gentle-4-long-thread.png`](contact-sheets/gentle-4-long-thread.png) 272 KB | 14 則訊息 |
| 5 | 詳細面板關閉 | 1280x900 | 開啟詳細後按右上角關閉鈕，錄收起過程與收起後狀態 | 停用 | [`gentle-5-case-panel-close.webm`](videos/gentle-5-case-panel-close.webm) 450 KB | [`gentle-5-case-panel-close.png`](contact-sheets/gentle-5-case-panel-close.png) 219 KB | 節點移除=true, 殘留 tween 0 |
| 6 | reduced-motion 模式 | 1280x900 | context 設 prefers-reduced-motion: reduce，錄首頁與面板開啟 | **啟用** | [`gentle-6-reduced-motion.webm`](videos/gentle-6-reduced-motion.webm) 420 KB | [`gentle-6-reduced-motion.png`](contact-sheets/gentle-6-reduced-motion.png) 173 KB | tween 首頁 0／面板 0, opacity 1 |
| 7 | 鍵盤開啟／關閉案件 | 1280x900 | 鍵盤 focus「在此檢視」按 Enter 開啟，再 focus 關閉鈕按 Enter 關閉 | 停用 | [`gentle-7-keyboard-open-close.webm`](videos/gentle-7-keyboard-open-close.webm) 433 KB | [`gentle-7-keyboard-open-close.png`](contact-sheets/gentle-7-keyboard-open-close.png) 201 KB | Enter 開啟=true, Enter 關閉=true |
| 8 | 快速連續開關面板（tween 殘留檢查） | 1280x900 | 連續開關面板 10 次後靜置，量測 tween 峰值與殘留 | 停用 | [`gentle-8-rapid-toggle.webm`](videos/gentle-8-rapid-toggle.webm) 1371 KB | [`gentle-8-rapid-toggle.png`](contact-sheets/gentle-8-rapid-toggle.png) 138 KB | 10 次開關：峰值 2, 殘留 0 |

### B — Donggu Friendly

進場 `0.42s / back.out(1.25) / y 12px / scale 0.96`　·　stagger `0.055s 間隔，上限 12`　·　面板 `0.48s / back.out(1.1) / x 16px（收起 0.26s / power2.inOut）`　·　回饋 `0.5s / elastic.out(1, 0.55)`

| # | 頁面／情境 | 視窗 | 操作內容 | reduced-motion | 影片 | Contact sheet | 量測 |
|---|---|---|---|---|---|---|---|
| 1 | 前台首頁（桌面） | 1280x900 | 載入首頁後重新導覽一次，錄 hero 淡入與三步驟／分類卡 stagger | 停用 | [`donggu-1-home-1280.webm`](videos/donggu-1-home-1280.webm) 656 KB | [`donggu-1-home-1280.png`](contact-sheets/donggu-1-home-1280.png) 177 KB | 溢位 0px |
| 2 | 前台首頁（375px） | 375x812 | 同上，於 375×812 檢視，並量測水平溢位 | 停用 | [`donggu-2-home-375.webm`](videos/donggu-2-home-375.webm) 330 KB | [`donggu-2-home-375.png`](contact-sheets/donggu-2-home-375.png) 585 KB | 溢位 0px |
| 3 | 客服案件開啟 2/5 : 3/5 詳細 | 1280x900 | 客服案件列表 → 點第一列「在此檢視」，錄詳細欄由右側展開 | 停用 | [`donggu-3-case-panel-open.webm`](videos/donggu-3-case-panel-open.webm) 416 KB | [`donggu-3-case-panel-open.png`](contact-sheets/donggu-3-case-panel-open.png) 229 KB | 溢位 0px；列表佔 0.4（451:677）, dialog=false |
| 4 | 長對話進場與捲動 | 1280x900 | 開啟長對話案件後，分 8 次逐步捲動詳細欄 | 停用 | [`donggu-4-long-thread.webm`](videos/donggu-4-long-thread.webm) 432 KB | [`donggu-4-long-thread.png`](contact-sheets/donggu-4-long-thread.png) 273 KB | 14 則訊息 |
| 5 | 詳細面板關閉 | 1280x900 | 開啟詳細後按右上角關閉鈕，錄收起過程與收起後狀態 | 停用 | [`donggu-5-case-panel-close.webm`](videos/donggu-5-case-panel-close.webm) 440 KB | [`donggu-5-case-panel-close.png`](contact-sheets/donggu-5-case-panel-close.png) 212 KB | 節點移除=true, 殘留 tween 0 |
| 6 | reduced-motion 模式 | 1280x900 | context 設 prefers-reduced-motion: reduce，錄首頁與面板開啟 | **啟用** | [`donggu-6-reduced-motion.webm`](videos/donggu-6-reduced-motion.webm) 408 KB | [`donggu-6-reduced-motion.png`](contact-sheets/donggu-6-reduced-motion.png) 174 KB | tween 首頁 0／面板 0, opacity 1 |
| 7 | 鍵盤開啟／關閉案件 | 1280x900 | 鍵盤 focus「在此檢視」按 Enter 開啟，再 focus 關閉鈕按 Enter 關閉 | 停用 | [`donggu-7-keyboard-open-close.webm`](videos/donggu-7-keyboard-open-close.webm) 426 KB | [`donggu-7-keyboard-open-close.png`](contact-sheets/donggu-7-keyboard-open-close.png) 203 KB | Enter 開啟=true, Enter 關閉=true |
| 8 | 快速連續開關面板（tween 殘留檢查） | 1280x900 | 連續開關面板 10 次後靜置，量測 tween 峰值與殘留 | 停用 | [`donggu-8-rapid-toggle.webm`](videos/donggu-8-rapid-toggle.webm) 1140 KB | [`donggu-8-rapid-toggle.png`](contact-sheets/donggu-8-rapid-toggle.png) 149 KB | 10 次開關：峰值 2, 殘留 0 |

### C — Crisp Tech

進場 `0.2s / power3.out / y 6px / scale 1`　·　stagger `0.022s 間隔，上限 16`　·　面板 `0.38s / power3.out / x 10px（收起 0.2s / power3.in）`　·　回饋 `0.16s / power3.out`

| # | 頁面／情境 | 視窗 | 操作內容 | reduced-motion | 影片 | Contact sheet | 量測 |
|---|---|---|---|---|---|---|---|
| 1 | 前台首頁（桌面） | 1280x900 | 載入首頁後重新導覽一次，錄 hero 淡入與三步驟／分類卡 stagger | 停用 | [`crisp-1-home-1280.webm`](videos/crisp-1-home-1280.webm) 458 KB | [`crisp-1-home-1280.png`](contact-sheets/crisp-1-home-1280.png) 185 KB | 溢位 0px |
| 2 | 前台首頁（375px） | 375x812 | 同上，於 375×812 檢視，並量測水平溢位 | 停用 | [`crisp-2-home-375.webm`](videos/crisp-2-home-375.webm) 213 KB | [`crisp-2-home-375.png`](contact-sheets/crisp-2-home-375.png) 630 KB | 溢位 0px |
| 3 | 客服案件開啟 2/5 : 3/5 詳細 | 1280x900 | 客服案件列表 → 點第一列「在此檢視」，錄詳細欄由右側展開 | 停用 | [`crisp-3-case-panel-open.webm`](videos/crisp-3-case-panel-open.webm) 382 KB | [`crisp-3-case-panel-open.png`](contact-sheets/crisp-3-case-panel-open.png) 223 KB | 溢位 0px；列表佔 0.4（451:677）, dialog=false |
| 4 | 長對話進場與捲動 | 1280x900 | 開啟長對話案件後，分 8 次逐步捲動詳細欄 | 停用 | [`crisp-4-long-thread.webm`](videos/crisp-4-long-thread.webm) 458 KB | [`crisp-4-long-thread.png`](contact-sheets/crisp-4-long-thread.png) 273 KB | 14 則訊息 |
| 5 | 詳細面板關閉 | 1280x900 | 開啟詳細後按右上角關閉鈕，錄收起過程與收起後狀態 | 停用 | [`crisp-5-case-panel-close.webm`](videos/crisp-5-case-panel-close.webm) 467 KB | [`crisp-5-case-panel-close.png`](contact-sheets/crisp-5-case-panel-close.png) 201 KB | 節點移除=true, 殘留 tween 0 |
| 6 | reduced-motion 模式 | 1280x900 | context 設 prefers-reduced-motion: reduce，錄首頁與面板開啟 | **啟用** | [`crisp-6-reduced-motion.webm`](videos/crisp-6-reduced-motion.webm) 405 KB | [`crisp-6-reduced-motion.png`](contact-sheets/crisp-6-reduced-motion.png) 173 KB | tween 首頁 0／面板 0, opacity 1 |
| 7 | 鍵盤開啟／關閉案件 | 1280x900 | 鍵盤 focus「在此檢視」按 Enter 開啟，再 focus 關閉鈕按 Enter 關閉 | 停用 | [`crisp-7-keyboard-open-close.webm`](videos/crisp-7-keyboard-open-close.webm) 452 KB | [`crisp-7-keyboard-open-close.png`](contact-sheets/crisp-7-keyboard-open-close.png) 202 KB | Enter 開啟=true, Enter 關閉=true |
| 8 | 快速連續開關面板（tween 殘留檢查） | 1280x900 | 連續開關面板 10 次後靜置，量測 tween 峰值與殘留 | 停用 | [`crisp-8-rapid-toggle.webm`](videos/crisp-8-rapid-toggle.webm) 1305 KB | [`crisp-8-rapid-toggle.png`](contact-sheets/crisp-8-rapid-toggle.png) 137 KB | 10 次開關：峰值 1, 殘留 0 |

## 素材內容安全性

- **未錄到登入頁**：登入只在另一個「不錄影」的 context 執行一次，之後所有情境都以 storageState 直接進入已登入狀態。素材中不存在密碼欄位。
- **無 cookie／token／connection string**：影片與截圖只拍瀏覽器可視區域，不含 DevTools、網路面板或環境變數。
- **無其他真實會員資料**：客服案件列表由後端依登入者過濾，隔離測試帳號名下只有那兩筆隔離測試案件，畫面上不會出現其他顧客的案件。
- 畫面上唯一的個人識別是隔離測試帳號的顯示名稱「動畫測試帳號」，屬本次測試資料，錄製後即刪除。
- storageState（含 session cookie）只寫在 scratchpad，錄製結束即刪除，未進入 repo。

---

## 視覺改版版本索引

四個版本並存，互不覆蓋。**目前的正式方向是 v4。**

| 版本 | 方向 | 狀態 | 目錄 |
|---|---|---|---|
| v1 | `#E6C8D4` 為全站主體色 | 已停用 | `brand-v1/` |
| v2 | 深藍殼層主導，粉色只在漸層尾端 | 已停用 | `brand-v2/` |
| v3 | 淡藍殼層＋粉色漸層尾端，首頁九圖示、Hero 素材 | 已停用（圖示與素材成果保留） | `brand-v3/` |
| **v4** | **對齊 `DoSelect_全組第一版UI預覽_2026-08-27` 六張參考圖**：白／近白最大面積、淡藍分區、高飽和亮藍操作焦點、深海軍藍只做文字、`#E6C8D4` 回到輔助角色 | **現行** | `brand-v4-reference-aligned/` |

- 設計基準文件與參考圖 contact sheet：`reference-ui-alignment/`
  （`README.md` 元件映射與取捨、`palette.md` 取樣值與對比度、`contact-sheet.png`）
- v4 驗收截圖：1280：16 張　·　768：16 張　·　375：16 張（共 48 張，全部取自 **production build**（`vite preview`），
  因此畫面中不存在 dev-only 的動態方案切換器）
- v4 擷取量測：`brand-v4-reference-aligned/capture-results.json`
  （每張的水平溢位、header／sidebar／畫布實際 computed 背景、Logo naturalWidth、console error 數）
- 登入後頁面一律以 Playwright `page.route('**/api/v1/**')` 建立純前端視覺狀態，
  fixture 全為示範資料；**未啟動後端、未建立測試帳號、未修改資料庫**。

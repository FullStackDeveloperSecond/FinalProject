# 參考圖對齊說明（brand-v4 reference-aligned）

本輪唯一的正式視覺基準：

```
D:\期末小組電商網站\DoSelect_全組第一版UI預覽_2026-08-27\
  01_前台首頁與新手導購.png
  02_會員中心與訂單.png
  03_活動購物車與結帳.png
  04_商品與庫存管理後台.png
  05_客服檢舉退貨工作台.png
  06_營運報表分析後台.png
```

原始參考圖**唯讀，未修改**。本目錄只放縮圖與說明。

- `contact-sheet.png` / `contact-sheet.html` — 六張參考圖的縮圖與 production 對應
- `thumbs/` — 520px 寬縮圖（僅供對照，取樣一律以原始檔為準）
- `reference-index.json` — 檔名／尺寸／檔案大小／對應 production 頁面與 route
- `palette.md` — 逐像素取樣值與 token 對照
- 驗收截圖在同層的 `../brand-v4-reference-aligned/`

v1（`#E6C8D4` 主體）、v2（深藍殼層）、v3（淡藍殼層＋粉色漸層尾端）三個方向**均已停用**，
截圖保留在 `../brand-v1`、`../brand-v2`、`../brand-v3` 供對照，沒有被覆蓋。

---

## 一、六張參考圖 → production 頁面對應

| 參考圖 | 對應 production 頁面 | route | 驗收截圖 |
|---|---|---|---|
| 01 前台首頁與新手導購 | 首頁、Hero、三步驟導購、六個分類入口、CTA + Donggu | `/` | `customer-01-home.png` |
| 02 會員中心與訂單 | 我的評價、售後入口、會員相關空狀態 | `/account/reviews`、`/support` | `customer-02-account-reviews.png`、`customer-02-support-home.png` |
| 03 活動購物車與結帳 | 購物車、優惠、訂單摘要、結帳 CTA | `/cart` | `customer-03-cart.png` |
| 04 商品與庫存管理後台 | 商品列表、庫存、Sidebar、完整 header | `/admin/products`、`/admin/` | `admin-04-products-inventory.png`、`admin-04-shell-home.png` |
| 05 客服檢舉退貨工作台 | 案件列表與詳細、2:3 分割工作台、退貨佇列 | `/support/tickets`、`/admin/cases`、`/admin/returns` | `customer-05-*`、`admin-05-*` |
| 06 營運報表分析後台 | 營運報表、AI 用量、指標卡、圖表 | `/admin/reports/*`、`/admin/ai/usage` | `admin-06-operational-report.png`、`admin-06-ai-usage.png` |

登入頁：`customer-07-login.png`、`admin-06-login.png`（沿用 02／04 的白底＋淡藍語言）。

---

## 二、共用元件映射

| 元件 | 參考圖依據 | production 實作 |
|---|---|---|
| **Header** | 01/02/03 白底、細底線、深墨藍導覽字、當前項亮藍底線 | `.site-header { background: var(--color-surface); border-bottom: 1px solid var(--color-border-line) }`；當前項 `color: var(--color-primary)` + `box-shadow: inset 0 -3px 0` |
| **Sidebar** | 04/05/06 白底、細右框、當前項淡藍膠囊＋亮藍左指示條 | `.admin-sidebar { background: var(--color-surface) }`；`a[aria-current] { background: var(--color-primary-soft); border-left-color: var(--color-primary) }` |
| **Hero** | 01 淡藍卡片、深海軍藍主標、右側吉祥物 | `--gradient-hero` + `--gradient-hero-glow`（右上角 `#E6C8D4` 光暈）、`--color-ink` 主標、`donggu-hero-wave.png` 裝飾 |
| **Card** | 01–06 一律白底＋細邊＋輕陰影 | `.card / .panel { background: var(--color-surface); border: 1px solid var(--color-border-soft); box-shadow: var(--shadow-sm) }` |
| **Button** | 03 主 CTA 實心亮藍；04 次要白底亮藍描邊 | primary = `--color-primary`；`.ds-app-button--secondary` 改為白底＋`--color-primary` 描邊（原為深海軍藍描邊） |
| **Form** | 03/04 白底輸入框、低對比藍灰描邊、亮藍 focus | `--color-border`（白底 3.22:1）、`--color-focus-ring` = 亮藍 |
| **Table** | 04/05 淡藍表頭、白色資料列、低對比中性藍灰分隔 | `thead th { background: var(--color-section) }`、`td { border-color: var(--color-border-soft) }`、選取列 `--color-primary-soft` |
| **Tabs** | 06 亮藍底線／實心亮藍當前項 | 報表分頁沿用 primary 實心；Customer 導覽用亮藍底線 |
| **Badge** | 02/04/05 淡色底＋同色系深字 | `--color-success-bg/-border`、`--color-warning-*`、`--color-danger-*`、`--color-info-*`，語意未被粉紅化 |
| **Progress** | 04 亮藍填色＋極淡灰軌 | `--chart-1` 填色 / `--chart-track` 軌道 |
| **Chart** | 06 藍／淺藍／青綠／黃橘四段 | `--chart-1..6`（藍・淺藍・青綠・黃橘・粉・紫），圖例必附文字標籤與數值 |
| **Empty / Error / Success** | 02/05 淡色底＋語意色圖示與文字 | `--color-*-bg` + `--color-*`；狀態一律「顏色＋文字」雙通道，不只用顏色 |

---

## 三、忠實套用的部分

1. **白／近白是最大面積** —— header、sidebar、卡片、表格列全部改為 `#FFFFFF`；頁面畫布 `#F7FAFE`。
   v2／v3 的深藍與淡藍大型殼層漸層全部移除，`.site-header` 不再套任何 gradient。
2. **淡藍只負責分區** —— 表頭、提示區、選取列、訊息泡泡用 `#F1F6FD` / `#ECF4FE`。
3. **高飽和亮藍負責操作與焦點** —— `#0B66E8` 用於主按鈕、連結、當前項、進度、圖表、重點數字。
4. **深海軍藍只做文字** —— `#001C46` 是內文與標題；只保留 `--gradient-deep` 給少數強調區塊，
   不鋪 header／footer／login。
5. **卡片＝白底＋細邊＋輕陰影**。
6. **圖形是導覽手段** —— 首頁三步驟加了編號圓標與流程箭頭；九個語意圖示放大成辨識入口
   （步驟 52px 容器／30px 圖示，分類 44px 容器／26px 圖示）。
7. **圖表系列可區分** —— 四個主系列直接取自參考圖 06 的甜甜圈；語意色維持原意。
8. **`#E6C8D4` 退回輔助角色** —— 只出現在 hero 右上角光暈、footer 尾端、登入外圈、
   售後柔性表面 `--color-surface-pink`，不再支配任何殼層。

---

## 四、因現有功能／RWD／可及性而調整的部分

| 調整 | 原因 |
|---|---|
| Header 只留一組品牌鎖定塊（≥641px 隱藏 `.brand-link__text`） | 橫式正式 Logo 圖檔本身已含吉祥物與「DoSelect」，旁邊再放文字字標會出現兩個品牌名；參考圖 01 只有一組。窄畫面只顯示方形徽章時才用文字補上品牌名。 |
| 白色 header 上取消 Logo 底板 | 參考圖的 header 是白色，Logo 直接坐在上面即可（「Select」深藍在白底 10.40:1）。v3 的白色底板在白 header 上只是多一層框。 |
| `--color-on-brand` 語意翻轉回白色 | v3 殼層是淡色所以是深墨藍；v4 這個 token 只用在亮藍實心塊上，必須是白色（5.15:1）。 |
| 客服案件列表改用 **container query** 切換堆疊卡片 | 參考圖 05 的案件列表是窄欄卡片。原本的堆疊版型只綁 `@media (max-width: 640px)`，所以 1280px 開啟詳細面板時列表欄只剩約 450px，表格被擠成一字一行、按鈕被裁掉（v3 截圖也一樣）。改成 `@container case-list (max-width: 640px)` 後依「欄寬」判斷，2:3 面板比例、DOM 順序與鍵盤操作完全不變。 |
| 兩張最寬的後台表格包進 `.table-scroll` | 375px 時 `products-table` 571px、`admin-returns__table` 604px，會把整頁推寬。`.table-scroll` 是專案原有慣例（AiUsagePage、OperationalReportPage 已在用）但一直沒有對應的 CSS 規則，本輪補上。 |
| `.table-scroll thead th { position: static }` | `.table-scroll` 一旦成為捲動容器，`.site-main thead th` 的 `position: sticky; top: var(--header-h)` 會改以容器為基準，表頭會浮在資料列中間。橫向捲動容器裡的垂直 sticky 沒有意義。 |
| `.app-shell`、`.reviews-page`、`.review-form` 加 `grid-template-columns: minmax(0, 1fr)` | grid 軌道預設 `min-width: auto`，長的 select 選項或不可斷字內容會把整個殼層推寬。與 Admin `.app-shell--bare` 用的是同一招。 |
| 報表頁 560px 以下收成單欄、圖表列改雙欄 | `repeat(2, minmax(10rem,1fr))` 與 `7rem + 8rem + 8rem` 固定軌道在 375px 一定超寬。 |
| 圖表第 5、6 系列用 `#D3789F` / `#7B5CF0` 而不是 `#E6C8D4` | `#E6C8D4` 在白底只有 1.28:1，當資料系列會看不見。第 5 系列是品牌粉的可讀化版本（白底 3.02:1）。 |
| 紫色 `#ECEEFD` / `#4B45C4` 進入 primitive | 參考圖 01 的第三張入口卡與 02 的「配送中」徽章都用紫色；不加進來就無法忠實還原。它是裝飾色，不是新的語意狀態。 |

---

## 五、不可回退的既有成果（本輪全部保留）

Donggu Hero 圖與素材處理、正式 Logo 與 shared `BrandMark.vue`、Customer Donggu 動效、
Admin Crisp 動效、客服與售後 Gentle 動效、reduced-motion、GSAP cleanup、
production 無 dev switcher、Admin 375px 溢位修正、Logo base-path、route mocking 視覺驗證、
GSAP 授權文件、review-index 去識別化、首頁九個圖示的資料結構與型別安全、
route／API／DTO／權限與商業行為。

以上每一項在 `customer-web/src/brand-system.spec.ts`（58 項）與
`*/src/motion-exploration.spec.ts` 中都有對應的護欄測試。

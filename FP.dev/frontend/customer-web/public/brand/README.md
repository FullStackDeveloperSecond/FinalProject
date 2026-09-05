# Customer 前台品牌素材

## 這個資料夾裡的檔案就是執行期的權威版本

`BrandMark.vue` 與首頁 Hero 只會載入本資料夾的檔案。要換圖，換這裡；
日常開發**不需要**先取得原始母檔。

| 檔名 | 用途 | 尺寸 | 大小 |
| --- | --- | --- | --- |
| `doselect-mark-40.webp` | header 標記 1x（優先） | 40×40 | ~1.3 KB |
| `doselect-mark-80.webp` | header 標記 2x（優先） | 80×80 | ~2.7 KB |
| `doselect-mark-120.webp` | header 標記 3x（優先） | 120×120 | ~4.6 KB |
| `doselect-mark-40.png` | 不支援 WebP 時的後備 1x | 40×40 | ~3.7 KB |
| `doselect-mark-80.png` | 不支援 WebP 時的後備 2x | 80×80 | ~12.5 KB |
| `doselect-mark-120.png` | 不支援 WebP 時的後備 3x | 120×120 | ~27.1 KB |
| `donggu-hero-wave.png` | **前台限定**：首頁 Hero 裝飾（`aria-hidden`、空 alt、375px 隱藏） | 320×480 | ~152 KB |

`doselect-mark-*` 這六個檔在 `admin-web/public/brand/` 也要有一模一樣的一份 ——
兩支 App 的 `import.meta.env.BASE_URL` 不同（`/` 與 `/admin/`），沒辦法共用同一份實體檔案。
`donggu-hero-wave.png` **只有前台會用**，不要複製到後台。

## 原始母檔在版本庫外

標記的母檔是「DoSelect 懂選 正式商標2」（1254×1254 正方形 PNG、自帶白底、
**檔名沒有副檔名**），1.29 MB，刻意沒有進版本庫 —— 大型素材會讓 clone、fetch
與 CI checkout 成本永久增加，PR #82 已把先前誤入版控的 44.9 MB review 素材清掉。

- **共用位置**：`[待補：組內共用雲端資料夾連結]`
- **保管人**：`[待補：品牌素材負責人]`

以上兩格請由知道的人補上；在那之前，需要母檔請在群組詢問。
再強調一次：**改 UI、跑測試、出 build 都不需要母檔**，上表六個衍生檔已在版本庫裡。

舊的 `doselect-logo-horizontal.png`（橫式）與 `doselect-logo-badge.png`（方形）
已於 PR #82 刪除，程式中沒有任何引用。

## BrandMark 怎麼用這些檔案

`shared/src/components/BrandMark.vue` 只建立**一個** `<img>`，包在 `<picture>` 裡：

```html
<picture>
  <source srcset="…-40.webp 1x, …-80.webp 2x, …-120.webp 3x" type="image/webp">
  <img src="…-40.png" srcset="…-40.png 1x, …-80.png 2x, …-120.png 3x"
       alt="" width="40" height="40">
</picture>
```

- 瀏覽器依 `type` 與密度描述符**只挑一個資源下載**，不會兩張都抓。
- 不使用「放兩個 `<img>` 再用 CSS 隱藏」：`display: none` 只是不畫，圖片照樣會下載。
- 顯示尺寸由 `--logo-mark-size`（design-tokens.css）決定，目前是 40px；
  衍生檔就是照這個尺寸的 1x/2x/3x 產生的，**不要只改其中一邊**。
- **`alt` 依情境而定**：header 傳 `<BrandMark decorative />`，圖片用空 `alt`，
  由旁邊的可見文字「DoSelect 懂選」擔任連結唯一的 accessible name；
  單獨使用時不傳 `decorative`，`alt` 會是「DoSelect 懂選」。
- 載入失敗時切成預留框，**不顯示破圖**；裝飾模式下該預留框也對輔助技術隱藏。

## 要換標記時的流程

1. 從上面的共用位置取得新的母檔。
2. 重新產生六個衍生檔。專案沒有安裝任何影像處理套件，目前是用既有的
   Playwright／Chrome canvas 編碼器產出：把母檔讀成 `data:` URI
   （`file://` 子資源會污染 canvas，`toDataURL` 會被擋），`drawImage` 到
   40／80／120 的正方形 canvas，再各自 `toDataURL('image/webp', 0.9)` 與
   `toDataURL('image/png')`。
3. 本資料夾與 `admin-web/public/brand/` **各放一份**。
4. 更新兩份 README 的尺寸與大小欄位。
5. 跑 `customer-web` 的 `src/brand-system.spec.ts` 與 `src/components/BrandMark.spec.ts`：
   會檢查單一 `<img>`、允許的檔名、檔案大小上限、`alt` 行為，以及沒有舊 Logo 引用。

檔名或尺寸有變動時 **必須同步改 `BrandMark.vue` 與兩份 README**；測試會把它們對起來。

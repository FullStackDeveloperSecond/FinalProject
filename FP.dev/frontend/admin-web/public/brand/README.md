# 品牌素材放置區

## 唯一來源

正式標記的唯一來源是 **`D:/期末小組電商網站/DoSelect正式素材/DoSelect 懂選 正式商標2`**
（1254×1254 正方形 PNG、自帶白底、**檔名沒有副檔名**）。
本資料夾的 `doselect-mark-*` 全部由這一張等比縮放產生，不裁切、不變形。

除此之外不使用其他標記：舊的 `doselect-logo-horizontal.png`（橫式）與
`doselect-logo-badge.png`（方形）已刪除，程式中也沒有任何引用。

## 檔案清單

| 檔名 | 用途 | 尺寸 | 大小 |
| --- | --- | --- | --- |
| `doselect-mark-40.webp` | header 標記 1x（優先） | 40×40 | ~1.3 KB |
| `doselect-mark-80.webp` | header 標記 2x（優先） | 80×80 | ~2.7 KB |
| `doselect-mark-120.webp` | header 標記 3x（優先） | 120×120 | ~4.6 KB |
| `doselect-mark-40.png` | 不支援 WebP 時的後備 1x | 40×40 | ~3.7 KB |
| `doselect-mark-80.png` | 不支援 WebP 時的後備 2x | 80×80 | ~12.5 KB |
| `doselect-mark-120.png` | 不支援 WebP 時的後備 3x | 120×120 | ~27.1 KB |
| `donggu-hero-wave.png` | 首頁 Hero 裝飾（純裝飾，`aria-hidden`，375px 隱藏） | 320×480 | ~152 KB |

同一組 `doselect-mark-*` 必須同時存在於
`customer-web/public/brand/` 與 `admin-web/public/brand/` ——
兩支 App 的 `import.meta.env.BASE_URL` 不同（`/` 與 `/admin/`），
不能共用同一份實體檔案。

## BrandMark 怎麼用這些檔案

`shared/src/components/BrandMark.vue` 只建立**一個** `<img>`，包在 `<picture>` 裡：

```html
<picture>
  <source srcset="…-40.webp 1x, …-80.webp 2x, …-120.webp 3x" type="image/webp">
  <img src="…-40.png" srcset="…-40.png 1x, …-80.png 2x, …-120.png 3x"
       alt="DoSelect 懂選" width="40" height="40">
</picture>
```

- 瀏覽器依 `type` 與密度描述符**只挑一個資源下載**，不會兩張都抓。
- 不再使用「放兩個 `<img>` 再用 CSS 隱藏其中一個」的作法：`display: none`
  只是不畫，圖片照樣會下載。
- 顯示尺寸由 `--logo-mark-size`（design-tokens.css）決定，目前是 40px。
- 載入失敗時 `@error` 會切成帶 `aria-label="DoSelect 懂選（正式 Logo 尚未匯入）"`
  的預留框，**不顯示破圖**。
- 標記本身是方形徽章，40px 下裡面的 "DoSelect" 字樣讀不出來，
  所以兩支 App 的 header 都另外用文字承載品牌名
  （Customer 的 `.brand-link__text`、Admin 的 `.brand-link__name`）。

## 要換標記時的流程

1. 換掉來源檔（或指向新的正式素材）。
2. 重新產生六個衍生檔。專案沒有安裝任何影像處理套件，
   目前是用既有的 Playwright／Chrome canvas 編碼器產出：
   把來源讀成 `data:` URI（`file://` 子資源會污染 canvas，`toDataURL` 會被擋），
   `drawImage` 到 40／80／120 的正方形 canvas，再各自
   `toDataURL('image/webp', 0.9)` 與 `toDataURL('image/png')`。
3. 兩支 App 的 `public/brand/` 各放一份。
4. 更新本檔的尺寸與大小欄位。
5. 跑 `customer-web` 的 `brand-system.spec.ts`：
   它會檢查單一 `<img>`、允許的檔名、檔案大小上限，以及「沒有任何舊 Logo 引用」。

檔名或尺寸有變動時 **必須同步改 `BrandMark.vue` 與本檔**；
測試會把兩邊對起來，不一致就會失敗。

<script setup lang="ts">
import { ref } from 'vue'

/**
 * DoSelect 正式品牌標記。
 *
 * 唯一來源是正式商標 2（`DoSelect 懂選 正式商標2`，1254×1254 正方形 PNG，自帶白底）。
 * 桌面與行動版共用同一支標記，只有解析度不同 —— 因此這裡**只建立一個 `<img>`**。
 *
 * 為什麼不是兩個 `<img>` 再用 CSS 隱藏：
 *   `display: none` 只是不畫，瀏覽器仍然會下載那張圖。舊版同時放橫式與方形兩張
 *   1.2 MB 的 Logo，等於每次載入都白拿 2.4 MB。改用單一 `<picture>` 之後，
 *   瀏覽器依 `type` 與 `srcset` 密度描述符**只挑一個資源**下載。
 *
 * 衍生檔（由正式商標 2 等比縮放產生，見 public/brand/README.md）：
 *   doselect-mark-40.{webp,png}    1x   header 顯示尺寸 40px
 *   doselect-mark-80.{webp,png}    2x
 *   doselect-mark-120.{webp,png}   3x
 * WebP 優先，PNG 是給不支援 WebP 的瀏覽器的後備；兩者都保持 1:1，不裁切、不變形。
 *
 * `src` 以 `import.meta.env.BASE_URL` 組成執行期 public 路徑（Customer 是 `/`，
 * Admin 是 `/admin/`），避免被 Vite 當成建置期資產匯入。
 * 素材缺失時退回帶標示的預留框，不顯示破圖。
 */

// 兩個 App 的 public base 不同，同一個元件在兩邊都能正確取到素材。
const assetBase = import.meta.env.BASE_URL
const brandPath = `${assetBase}brand/`

/** PNG 後備的 1x 來源；同時提供 `width`/`height` 的內建尺寸。 */
const markSrc = `${brandPath}doselect-mark-40.png`
const webpSrcset = `${brandPath}doselect-mark-40.webp 1x, ${brandPath}doselect-mark-80.webp 2x, ${brandPath}doselect-mark-120.webp 3x`
const pngSrcset = `${brandPath}doselect-mark-40.png 1x, ${brandPath}doselect-mark-80.png 2x, ${brandPath}doselect-mark-120.png 3x`

const markAvailable = ref(true)
</script>

<template>
  <span class="brand-mark">
    <picture
      v-if="markAvailable"
      class="brand-mark__picture"
    >
      <source
        :srcset="webpSrcset"
        type="image/webp"
      >
      <img
        class="brand-mark__img"
        :src="markSrc"
        :srcset="pngSrcset"
        alt="DoSelect 懂選"
        width="40"
        height="40"
        decoding="async"
        @error="markAvailable = false"
      >
    </picture>
    <span
      v-else
      class="brand-mark__slot"
      role="img"
      aria-label="DoSelect 懂選（正式 Logo 尚未匯入）"
    >D</span>
  </span>
</template>

<style scoped>
.brand-mark {
  display: inline-flex;
  align-items: center;
  flex: none;
}

.brand-mark__picture {
  display: inline-flex;
  width: var(--logo-mark-size);
  height: var(--logo-mark-size);
}

.brand-mark__img {
  display: block;
  /* 1254×1254 原稿，固定 1:1，只縮不拉 */
  width: 100%;
  height: 100%;
  aspect-ratio: 1 / 1;
  object-fit: contain;
  border-radius: var(--radius-pill);
}

.brand-mark__slot {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: var(--logo-mark-size);
  height: var(--logo-mark-size);
  font-size: var(--fs-caption);
  font-weight: 700;
  color: var(--color-primary);
  background: var(--color-surface-strong);
  border-radius: var(--radius-pill);
}
</style>

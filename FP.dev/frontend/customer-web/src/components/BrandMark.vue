<script setup lang="ts">
import { ref } from 'vue'

/**
 * 品牌標記。優先使用 `public/brand/donggu-mark.png` 的正式立繪；
 * 檔案尚未放入專案時退回帶標示的預留框，不顯示破圖，也不使用替代角色。
 *
 * `src` 以動態繫結傳入，讓 Vite 視為執行期的 public 路徑而非建置期資產匯入，
 * 素材尚未放進來時建置仍可通過。
 */
const markSrc = '/brand/donggu-mark.png'
const assetAvailable = ref(true)
</script>

<template>
  <span class="brand-mark">
    <img
      v-if="assetAvailable"
      class="brand-mark__img"
      :src="markSrc"
      alt=""
      width="36"
      height="36"
      @error="assetAvailable = false"
    >
    <span
      v-else
      class="brand-mark__slot"
      role="img"
      aria-label="Donggu 立繪預留區"
      title="Donggu 正式立繪尚未匯入，暫以預留標記顯示"
    >D</span>
  </span>
</template>

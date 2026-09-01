<script setup lang="ts">
import { ref } from 'vue'

/**
 * DoSelect 正式品牌標記。
 *
 * 兩種正式素材，依可用寬度切換，兩者都不拉伸、不裁切：
 *
 * - `doselect-logo-horizontal.png`（2048×768，透明背景）
 *   桌面 header／footer 使用。這支 Logo 的「Select」字色就是品牌藍 #043A91，
 *   **只能放在淺色底上**：白底 10.40:1，深藍底只有 1.59:1 會整個被吞掉。
 *   v4 起 header 依參考圖改為白色，Logo 直接坐在 header 上即可；元件本身仍保留
 *   淺色底板作為預設，讓它被放進其他深色區塊時不會失效。
 *
 * - `doselect-logo-badge.png`（1254×1254，自帶白底）
 *   窄畫面與緊湊導覽使用。因為自帶白底，容器改用白色表面，接縫才不會露出來。
 *
 * `src` 以動態繫結傳入，讓 Vite 視為執行期 public 路徑而非建置期資產匯入；
 * 素材缺失時退回帶標示的預留框，不顯示破圖。
 */
withDefaults(defineProps<{
  /** 緊湊模式：只顯示方形徽章，不顯示橫式 Logo。 */
  compact?: boolean
}>(), { compact: false })

// 兩個 App 的 public base 不同（Customer 是 /，Admin 是 /admin/），
// 用 import.meta.env.BASE_URL 組路徑，同一個元件在兩邊都能正確取到素材。
const assetBase = import.meta.env.BASE_URL
const wordmarkSrc = `${assetBase}brand/doselect-logo-horizontal.png`
const badgeSrc = `${assetBase}brand/doselect-logo-badge.png`

const wordmarkAvailable = ref(true)
const badgeAvailable = ref(true)
</script>

<template>
  <span
    class="brand-mark"
    :data-compact="compact"
  >
    <!-- 窄畫面／緊湊：方形徽章（自帶白底 → 白色容器） -->
    <span class="brand-mark__badge-shell">
      <img
        v-if="badgeAvailable"
        class="brand-mark__badge"
        :src="badgeSrc"
        alt="DoSelect 懂選"
        width="1254"
        height="1254"
        decoding="async"
        @error="badgeAvailable = false"
      >
      <span
        v-else
        class="brand-mark__slot"
        role="img"
        aria-label="DoSelect 懂選（正式 Logo 尚未匯入）"
      >D</span>
    </span>

    <!-- 桌面：橫式透明 Logo（為深色底設計 → 深墨藍容器） -->
    <span
      v-if="!compact"
      class="brand-mark__wordmark-shell"
    >
      <img
        v-if="wordmarkAvailable"
        class="brand-mark__wordmark"
        :src="wordmarkSrc"
        alt="DoSelect 懂選"
        width="2048"
        height="768"
        decoding="async"
        @error="wordmarkAvailable = false"
      >
      <span
        v-else
        class="brand-mark__fallback-text"
      >DoSelect 懂選</span>
    </span>
  </span>
</template>

<style scoped>
.brand-mark {
  display: inline-flex;
  align-items: center;
  gap: var(--logo-gap);
  min-width: 0;
}

/* ---- 方形徽章：自帶白底，用白色容器讓接縫消失 ---- */
.brand-mark__badge-shell {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: none;
  width: var(--logo-mark-size);
  height: var(--logo-mark-size);
  padding: 2px;
  background: var(--color-surface);
  border-radius: var(--radius-pill);
  box-shadow: var(--shadow-sm);
  overflow: hidden;
}

.brand-mark__badge {
  display: block;
  /* 1:1 原始比例，只縮不拉 */
  width: 100%;
  height: 100%;
  aspect-ratio: 1 / 1;
  object-fit: contain;
  border-radius: var(--radius-pill);
}

/* ---- 橫式 Logo ---- */
/* 淺色底板：與 .brand-mark__badge-shell 同一套規則，讓兩支 Logo 的留白一致。
   白色 header 上由 app 端把底板設成 transparent（見兩支 style.css 的 v4 區段）。 */
.brand-mark__wordmark-shell {
  display: inline-flex;
  align-items: center;
  padding: var(--space-1) var(--space-3);
  background: var(--color-surface);
  border-radius: var(--radius-md);
}

.brand-mark__wordmark {
  display: block;
  /* 2048×768 = 2.667:1，固定高度、寬度自動，不變形 */
  height: var(--logo-wordmark-h);
  width: auto;
  max-width: 100%;
  aspect-ratio: 2048 / 768;
  object-fit: contain;
}

.brand-mark__fallback-text {
  font-size: var(--fs-body);
  font-weight: 700;
  color: var(--color-on-navy);
  white-space: nowrap;
}

.brand-mark__slot {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 100%;
  font-size: var(--fs-caption);
  font-weight: 700;
  color: var(--color-primary);
  background: var(--color-surface-strong);
  border-radius: var(--radius-pill);
}

/* 兩支 Logo 互斥：桌面只顯示橫式（本身已含吉祥物），窄畫面只顯示方形徽章，
   避免同一列出現兩個品牌標記。 */
@media (min-width: 641px) {
  .brand-mark[data-compact="false"] .brand-mark__badge-shell {
    display: none;
  }
}

@media (max-width: 640px) {
  .brand-mark__wordmark-shell {
    display: none;
  }
}
</style>

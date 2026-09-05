<script setup lang="ts">
withDefaults(defineProps<{
  context?: 'storefront' | 'admin'
}>(), {
  context: 'storefront',
})
</script>

<template>
  <span class="doselect-brand">
    <svg
      class="doselect-brand__mark"
      viewBox="0 0 40 40"
      role="img"
      aria-label="懂選標誌"
    >
      <rect
        width="40"
        height="40"
        rx="12"
        fill="currentColor"
      />
      <path
        d="M11 12.5h8.25c6.2 0 10.25 3.05 10.25 7.5s-4.05 7.5-10.25 7.5H11v-15Zm6 4.5v6h2.2c2.65 0 4.3-1.05 4.3-3s-1.65-3-4.3-3H17Z"
        fill="#fff"
      />
      <path
        d="m20.8 24.1 2.35 2.35 5.55-6.05"
        fill="none"
        stroke="#8ed8ff"
        stroke-width="2.4"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
    </svg>
    <span class="doselect-brand__text">
      <span class="doselect-brand__name"><span lang="en">DoSelect</span> <span lang="zh-Hant">懂選</span></span>
      <span
        v-if="context === 'admin'"
        class="doselect-brand__context"
      >管理後台</span>
    </span>
  </span>
</template>

<style scoped>
.doselect-brand {
  display: inline-flex;
  align-items: center;
  gap: 0.625rem;
  min-width: 0;
}

.doselect-brand__mark {
  width: 2rem;
  height: 2rem;
  flex: none;
  color: var(--color-primary);
}

.doselect-brand__text {
  display: inline-flex;
  align-items: baseline;
  gap: 0.45rem;
  min-width: 0;
}

.doselect-brand__name {
  color: var(--color-text);
  font-weight: 800;
  letter-spacing: -0.02em;
  white-space: nowrap;
}

.doselect-brand__context {
  color: var(--color-text-muted);
  font-size: 0.78rem;
  font-weight: 700;
  white-space: nowrap;
}

@media (max-width: 480px) {
  .doselect-brand__context {
    display: none;
  }
}
</style>

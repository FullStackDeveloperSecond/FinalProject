<script setup lang="ts">
withDefaults(defineProps<{
  title?: string
  description?: string
}>(), {
  title: '目前沒有資料',
  description: '調整條件後再試一次，或稍後回來查看。',
})
</script>

<template>
  <section
    class="shared-state"
    role="status"
    aria-live="polite"
  >
    <h2>{{ title }}</h2>
    <p>{{ description }}</p>
    <div
      v-if="$slots.default"
      class="shared-state__actions"
    >
      <slot />
    </div>
  </section>
</template>

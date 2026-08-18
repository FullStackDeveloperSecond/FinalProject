<script setup lang="ts">
withDefaults(defineProps<{
  label?: string
}>(), {
  label: '資料載入中',
})
</script>

<template>
  <section
    class="shared-state"
    role="status"
    aria-live="polite"
    aria-busy="true"
  >
    <span
      class="shared-state__spinner"
      aria-hidden="true"
    />
    <p>{{ label }}</p>
  </section>
</template>

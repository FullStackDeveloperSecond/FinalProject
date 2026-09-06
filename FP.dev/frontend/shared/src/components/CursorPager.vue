<script setup lang="ts">
import AppButton from './AppButton.vue'

withDefaults(defineProps<{
  /** Whether an earlier page exists. */
  hasPrev: boolean
  /** Whether a later page exists. */
  hasNext: boolean
  /** Disables both controls while a page transition is in flight. */
  busy?: boolean
  /** Button labels, from the host app's locale. */
  prevLabel: string
  nextLabel: string
  /** Accessible name for the pager navigation landmark, from the host app's locale. */
  ariaLabel: string
}>(), {
  busy: false,
})

const emit = defineEmits<{
  prev: []
  next: []
}>()
</script>

<template>
  <nav
    class="ds-cursor-pager"
    :aria-label="ariaLabel"
  >
    <AppButton
      variant="secondary"
      type="button"
      :disabled="busy || !hasPrev"
      @click="emit('prev')"
    >
      {{ prevLabel }}
    </AppButton>
    <span
      v-if="$slots.status"
      class="ds-cursor-pager__status"
    >
      <slot name="status" />
    </span>
    <AppButton
      variant="secondary"
      type="button"
      :disabled="busy || !hasNext"
      @click="emit('next')"
    >
      {{ nextLabel }}
    </AppButton>
  </nav>
</template>

<style scoped>
.ds-cursor-pager {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}

.ds-cursor-pager__status {
  font-size: var(--fs-caption);
  color: var(--color-text-muted);
}
</style>

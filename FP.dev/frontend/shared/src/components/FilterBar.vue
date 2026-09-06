<script setup lang="ts">
import AppButton from './AppButton.vue'

withDefaults(defineProps<{
  /** Whether any filter is currently applied — controls the "clear all" affordance. */
  hasActiveFilters?: boolean
  /** Label for the "clear all filters" button, from the host app's locale. */
  clearLabel: string
  /** Optional accessible name; when given the bar becomes a labelled group. */
  ariaLabel?: string
}>(), {
  hasActiveFilters: false,
  ariaLabel: undefined,
})

const emit = defineEmits<{
  clear: []
}>()
</script>

<template>
  <div
    class="ds-filter-bar"
    :role="ariaLabel ? 'group' : undefined"
    :aria-label="ariaLabel || undefined"
  >
    <div class="ds-filter-bar__controls">
      <slot />
    </div>
    <div
      v-if="$slots.actions || hasActiveFilters"
      class="ds-filter-bar__actions"
    >
      <slot name="actions" />
      <AppButton
        v-if="hasActiveFilters"
        variant="ghost"
        type="button"
        @click="emit('clear')"
      >
        {{ clearLabel }}
      </AppButton>
    </div>
  </div>
</template>

<style scoped>
.ds-filter-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--space-3);
}

.ds-filter-bar__controls {
  display: flex;
  flex: 1 1 auto;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--space-3);
  min-width: 0;
}

.ds-filter-bar__actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  margin-inline-start: auto;
}
</style>

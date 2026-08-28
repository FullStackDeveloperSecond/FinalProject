<script setup lang="ts">
import { computed } from 'vue'
import Button from 'primevue/button'

type AppButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'

const props = withDefaults(defineProps<{
  /** Visual intent. Colours come entirely from the shared PrimeVue preset. */
  variant?: AppButtonVariant
  /** Native button type. */
  type?: 'button' | 'submit' | 'reset'
  /** Text label; the default slot takes precedence when both are given. */
  label?: string
  /** Icon class string, e.g. "pi pi-check". Use the #icon slot for custom markup. */
  icon?: string
  iconPos?: 'left' | 'right'
  /** Shows the PrimeVue loading state and blocks activation. */
  loading?: boolean
  disabled?: boolean
  size?: 'small' | 'large'
}>(), {
  variant: 'primary',
  type: 'button',
  label: undefined,
  icon: undefined,
  iconPos: 'left',
  loading: false,
  disabled: false,
  size: undefined,
})

const emit = defineEmits<{
  click: [event: MouseEvent]
}>()

const isBlocked = computed(() => props.loading || props.disabled)

// `secondary` deliberately takes NO PrimeVue modifier: the preset's `outlined` variant
// paints emerald, and secondary must read as Navy. It is painted below through this
// component's own `.ds-app-button--secondary` class with semantic tokens instead.
const variantProps = computed<{ severity?: 'danger'; text?: boolean }>(() => {
  switch (props.variant) {
    case 'ghost':
      return { text: true }
    case 'danger':
      return { severity: 'danger' }
    default:
      return {}
  }
})

function onClick(event: MouseEvent) {
  if (isBlocked.value) {
    return
  }
  emit('click', event)
}
</script>

<template>
  <Button
    v-bind="variantProps"
    class="ds-app-button"
    :class="`ds-app-button--${variant}`"
    :type="type"
    :loading="loading"
    :disabled="isBlocked"
    :size="size"
    @click="onClick"
  >
    <span
      class="ds-app-button__content"
      :class="`ds-app-button__content--icon-${iconPos}`"
    >
      <span
        v-if="$slots.icon || icon"
        class="ds-app-button__icon"
      >
        <slot name="icon"><i
          v-if="icon"
          :class="icon"
          aria-hidden="true"
        /></slot>
      </span>
      <span
        v-if="$slots.default || label"
        class="ds-app-button__label"
      ><slot>{{ label }}</slot></span>
    </span>
  </Button>
</template>

<style scoped>
/*
  Secondary = the Navy counterpart to the emerald primary.
  Styled through this component's own public class and semantic tokens only —
  no `.p-*` PrimeVue internals in any selector, no literal colours, no !important.
  The scope attribute lands on PrimeVue's root <button>, which is enough
  specificity to win over `.p-button` without reaching into its class names.
*/
.ds-app-button--secondary {
  background: var(--color-surface);
  border: 1px solid var(--color-navy);
  color: var(--color-navy);
}

.ds-app-button--secondary:not(:disabled):hover {
  background: var(--color-navy-hover);
  border-color: var(--color-navy-hover);
  color: var(--color-on-navy);
}

.ds-app-button__content {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
}

.ds-app-button__content--icon-right {
  flex-direction: row-reverse;
}

.ds-app-button__icon {
  display: inline-flex;
  align-items: center;
}
</style>

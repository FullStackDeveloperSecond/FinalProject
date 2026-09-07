<script setup lang="ts">
import { computed, useId } from 'vue'

const props = withDefaults(defineProps<{
  label: string
  required?: boolean
  description?: string
  error?: string
  /** Optional explicit id for the control; auto-generated when omitted. */
  id?: string
}>(), {
  required: false,
  description: undefined,
  error: undefined,
  id: undefined,
})

const autoId = useId()
const fieldId = computed(() => props.id ?? autoId)
const descriptionId = computed(() => `${fieldId.value}-description`)
const errorId = computed(() => `${fieldId.value}-error`)

const describedBy = computed(() => {
  const ids: string[] = []
  if (props.description) {
    ids.push(descriptionId.value)
  }
  if (props.error) {
    ids.push(errorId.value)
  }
  return ids.length > 0 ? ids.join(' ') : undefined
})

/**
 * Attributes the consumer binds onto the slotted control with `v-bind`.
 * `required` state is carried here rather than by any visible wording — the
 * asterisk is decorative (aria-hidden) and this component ships no copy.
 * Optional attributes stay `undefined` so Vue omits them entirely, never
 * rendering a misleading `"false"` string.
 */
const controlAttrs = computed(() => ({
  'id': fieldId.value,
  'aria-describedby': describedBy.value,
  'aria-invalid': props.error ? true : undefined,
  'required': props.required ? true : undefined,
  'aria-required': props.required ? 'true' : undefined,
}))
</script>

<template>
  <div
    class="ds-form-field"
    :class="{ 'ds-form-field--invalid': Boolean(error) }"
  >
    <label
      :for="fieldId"
      class="ds-form-field__label"
    >
      <span>{{ label }}</span>
      <span
        v-if="required"
        class="ds-form-field__required-mark"
        aria-hidden="true"
      >*</span>
    </label>

    <p
      v-if="description"
      :id="descriptionId"
      class="ds-form-field__description"
    >
      {{ description }}
    </p>

    <div class="ds-form-field__control">
      <slot v-bind="controlAttrs" />
    </div>

    <p
      v-if="error"
      :id="errorId"
      class="ds-form-field__error"
      role="alert"
    >
      {{ error }}
    </p>
  </div>
</template>

<style scoped>
.ds-form-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.ds-form-field__label {
  display: inline-flex;
  align-items: baseline;
  gap: var(--space-1);
  font-weight: 600;
  font-size: var(--fs-body);
  color: var(--color-text);
}

.ds-form-field__required-mark {
  color: var(--color-danger);
}

.ds-form-field__description {
  margin: 0;
  font-size: var(--fs-caption);
  color: var(--color-text-muted);
}

.ds-form-field__control {
  display: flex;
  flex-direction: column;
}

.ds-form-field__error {
  margin: 0;
  font-size: var(--fs-caption);
  color: var(--color-danger);
}
</style>

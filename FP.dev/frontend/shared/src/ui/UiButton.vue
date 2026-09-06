<script setup lang="ts">
import Button from 'primevue/button'

withDefaults(defineProps<{
  disabled?: boolean
  label?: string
  type?: 'button' | 'submit' | 'reset'
}>(), {
  disabled: false,
  label: undefined,
  type: 'button',
})
</script>

<template>
  <Button
    :disabled="disabled"
    :label="label"
    :type="type"
  >
    <slot />
  </Button>
</template>

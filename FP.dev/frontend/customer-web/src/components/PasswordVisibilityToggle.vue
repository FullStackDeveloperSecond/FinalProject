<script setup lang="ts">
defineProps<{
  modelValue: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

function show(): void {
  emit('update:modelValue', true)
}

function hide(): void {
  emit('update:modelValue', false)
}
</script>

<template>
  <button
    type="button"
    class="password-field__toggle"
    aria-label="按住以顯示密碼"
    @mousedown="show"
    @mouseup="hide"
    @mouseleave="hide"
    @touchstart.prevent="show"
    @touchend.prevent="hide"
    @touchcancel.prevent="hide"
    @keydown.enter.prevent="show"
    @keydown.space.prevent="show"
    @keyup.enter.prevent="hide"
    @keyup.space.prevent="hide"
    @blur="hide"
  >
    <svg
      v-if="modelValue"
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8Z" />
      <circle
        cx="12"
        cy="12"
        r="3"
      />
    </svg>
    <svg
      v-else
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      <path d="M17.94 17.94A10.94 10.94 0 0 1 12 20c-7 0-11-8-11-8a20.3 20.3 0 0 1 5.06-6.06M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a20.3 20.3 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24" />
      <line
        x1="1"
        y1="1"
        x2="23"
        y2="23"
      />
    </svg>
  </button>
</template>

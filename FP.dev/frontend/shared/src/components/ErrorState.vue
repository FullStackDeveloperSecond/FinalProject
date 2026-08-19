<script setup lang="ts">
withDefaults(defineProps<{
  title?: string
  description?: string
  correlationId?: string
  traceId?: string
  retryLabel?: string
}>(), {
  title: '無法載入資料',
  description: '請稍後再試一次。',
  correlationId: undefined,
  traceId: undefined,
  retryLabel: '重試',
})

const emit = defineEmits<{
  retry: []
}>()
</script>

<template>
  <section
    class="shared-state shared-state--error"
    role="alert"
  >
    <h2>{{ title }}</h2>
    <p>{{ description }}</p>
    <dl
      v-if="correlationId || traceId"
      class="shared-state__tracking"
    >
      <template v-if="correlationId">
        <dt>Request ID</dt>
        <dd>{{ correlationId }}</dd>
      </template>
      <template v-if="traceId">
        <dt>Trace ID</dt>
        <dd>{{ traceId }}</dd>
      </template>
    </dl>
    <button
      v-if="retryLabel"
      type="button"
      @click="emit('retry')"
    >
      {{ retryLabel }}
    </button>
  </section>
</template>

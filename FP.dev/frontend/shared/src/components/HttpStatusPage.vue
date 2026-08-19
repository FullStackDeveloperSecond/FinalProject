<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  status: 401 | 403 | 404 | 500
  homeHref?: string
}>(), {
  homeHref: '/',
})

const content = computed(() => {
  const statusContent = {
    401: {
      title: '需要登入',
      description: '請先完成登入，再繼續使用這項功能。',
    },
    403: {
      title: '沒有權限',
      description: '目前的帳號無法使用這個頁面。',
    },
    404: {
      title: '找不到頁面',
      description: '這個頁面不存在、已失效，或目前無法使用。',
    },
    500: {
      title: '系統暫時無法處理',
      description: '請稍後再試；若問題持續發生，請提供畫面上的 Request ID。',
    },
  }

  return statusContent[props.status]
})
</script>

<template>
  <section
    class="http-status-page"
    :aria-labelledby="`http-status-${status}`"
  >
    <p class="http-status-page__code">
      {{ status }}
    </p>
    <h1 :id="`http-status-${status}`">
      {{ content.title }}
    </h1>
    <p>{{ content.description }}</p>
    <a :href="homeHref">返回首頁</a>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import LoginForm from '../features/auth/LoginForm.vue'
const route = useRoute()
const needsSupportLogin = computed(() => typeof route.query.redirect === 'string' && /^\/support(?:\/|$)/.test(route.query.redirect))
</script>

<template>
  <section aria-labelledby="page-title">
    <h1 id="page-title">
      會員登入
    </h1>
    <p
      v-if="needsSupportLogin"
      role="status"
    >
      請先登入才能使用客服中心，登入後會帶你回到原本的客服頁面。
    </p>
    <LoginForm />
  </section>
</template>

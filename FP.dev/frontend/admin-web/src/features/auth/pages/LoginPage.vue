<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'

const auth = useAdminAuthStore()
const router = useRouter()

const email = ref('')
const password = ref('')

async function onSubmit(): Promise<void> {
  await auth.login(email.value, password.value)
  if (!auth.challenge) {
    return
  }
  await router.push(auth.challenge.kind === 'enroll' ? '/login/enroll' : '/login/verify')
}
</script>

<template>
  <div class="auth-page">
    <form
      class="auth-card"
      @submit.prevent="onSubmit"
    >
      <h1>管理員登入</h1>
      <p class="auth-card__subtitle">
        DoSelect 懂選．後台管理系統
      </p>

      <label class="field">
        <span class="field__label">電子郵件</span>
        <input
          v-model="email"
          type="email"
          name="email"
          autocomplete="username"
          required
          :disabled="auth.loading"
        >
      </label>

      <label class="field">
        <span class="field__label">密碼</span>
        <input
          v-model="password"
          type="password"
          name="password"
          autocomplete="current-password"
          required
          :disabled="auth.loading"
        >
      </label>

      <p
        v-if="auth.errorMessage"
        class="field-error"
        role="alert"
      >
        {{ auth.errorMessage }}
      </p>

      <button
        type="submit"
        class="auth-card__submit"
        :disabled="auth.loading"
      >
        {{ auth.loading ? '登入中…' : '登入' }}
      </button>
    </form>
  </div>
</template>

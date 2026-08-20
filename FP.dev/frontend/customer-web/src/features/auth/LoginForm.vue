<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { useSessionStore } from '../../stores/session'

const email = ref('')
const password = ref('')
const rememberMe = ref(false)
const submitting = ref(false)
const topLevelError = ref<string | null>(null)

const router = useRouter()
const sessionStore = useSessionStore()

async function handleSubmit(): Promise<void> {
  topLevelError.value = null
  submitting.value = true

  try {
    await sessionStore.login({
      email: email.value.trim(),
      password: password.value,
      rememberMe: rememberMe.value,
    })
    await router.push('/')
  } catch (error) {
    topLevelError.value = resolveErrorMessage(error)
  } finally {
    submitting.value = false
  }
}

function resolveErrorMessage(error: unknown): string {
  if (!isApiError(error)) {
    return '登入時發生未預期的錯誤，請稍後再試一次。'
  }

  switch (error.code) {
    case 'invalid_credentials':
      return 'Email 或密碼錯誤，請再試一次。'
    case 'account_locked':
      return '登入失敗次數過多，帳號已暫時鎖定，請稍後再試。'
    case 'account_email_unverified':
      return '此帳號尚未完成 Email 驗證，請查看您的信箱完成驗證後再登入。'
    case 'account_suspended':
      return '此帳號已被停權，如有疑問請聯繫客服。'
    default:
      return error.message
  }
}
</script>

<template>
  <form
    class="auth-form"
    novalidate
    @submit.prevent="handleSubmit"
  >
    <p
      v-if="topLevelError"
      class="form-banner form-banner--error"
      role="alert"
    >
      {{ topLevelError }}
    </p>

    <div class="form-field">
      <label for="login-email">電子郵件</label>
      <input
        id="login-email"
        v-model="email"
        type="email"
        autocomplete="email"
        required
      >
    </div>

    <div class="form-field">
      <label for="login-password">密碼</label>
      <input
        id="login-password"
        v-model="password"
        type="password"
        autocomplete="current-password"
        required
      >
    </div>

    <div class="form-checkbox">
      <input
        id="login-remember-me"
        v-model="rememberMe"
        type="checkbox"
      >
      <label for="login-remember-me">記住我</label>
    </div>

    <button
      type="submit"
      :disabled="submitting"
    >
      {{ submitting ? '登入中…' : '登入' }}
    </button>

    <p class="auth-form__switch">
      還沒有帳戶？<RouterLink to="/register">
        立即註冊
      </RouterLink>
    </p>
  </form>
</template>

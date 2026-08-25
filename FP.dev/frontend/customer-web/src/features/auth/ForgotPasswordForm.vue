<script setup lang="ts">
import { ref } from 'vue'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState } from '@doselect/web-shared/components'
import { requestPasswordReset } from './api'

const email = ref('')
const submitting = ref(false)
const submitted = ref(false)
const topLevelError = ref<string | null>(null)

async function handleSubmit(): Promise<void> {
  topLevelError.value = null
  submitting.value = true
  try {
    await requestPasswordReset({ email: email.value.trim() })
    submitted.value = true
  } catch (error) {
    // A failure here must not reveal whether the email belongs to an account — only report that
    // the *request* didn't go through, never anything account-specific (Alex review, 2026-08-24).
    topLevelError.value = resolveErrorMessage(error)
  } finally {
    submitting.value = false
  }
}

function resolveErrorMessage(error: unknown): string {
  if (isApiError(error) && error.code === 'rate_limit_exceeded') {
    return '請求過於頻繁，請稍後再試一次。'
  }

  return '寄送重設連結時發生錯誤，請稍後再試一次。'
}
</script>

<template>
  <EmptyState
    v-if="submitted"
    title="請查看您的信箱"
    description="若此 Email 已註冊，我們已寄出密碼重設信，請於 1 小時內點擊信中連結設定新密碼。"
  >
    <RouterLink to="/login">
      回登入頁
    </RouterLink>
  </EmptyState>

  <form
    v-else
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
      <label for="forgot-password-email">電子郵件</label>
      <input
        id="forgot-password-email"
        v-model="email"
        type="email"
        autocomplete="email"
        required
      >
      <p class="form-field__hint">
        我們會寄送密碼重設連結到這個信箱。
      </p>
    </div>

    <button
      type="submit"
      :disabled="submitting"
    >
      {{ submitting ? '寄送中…' : '寄送重設連結' }}
    </button>

    <p class="auth-form__switch">
      想起密碼了？<RouterLink to="/login">
        回登入頁
      </RouterLink>
    </p>
  </form>
</template>

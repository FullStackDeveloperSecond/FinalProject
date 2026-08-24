<script setup lang="ts">
import { ref } from 'vue'
import { EmptyState } from '@doselect/web-shared/components'
import { requestPasswordReset } from './api'

const email = ref('')
const submitting = ref(false)
const submitted = ref(false)

async function handleSubmit(): Promise<void> {
  submitting.value = true
  try {
    await requestPasswordReset({ email: email.value.trim() })
    submitted.value = true
  } finally {
    submitting.value = false
  }
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

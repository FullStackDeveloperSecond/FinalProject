<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState } from '@doselect/web-shared/components'
import PasswordVisibilityToggle from '../../components/PasswordVisibilityToggle.vue'
import { confirmPasswordReset } from './api'

type Status = 'form' | 'success' | 'missing-params'

const route = useRoute()
const newPassword = ref('')
const confirmPassword = ref('')
const showPassword = ref(false)
const showConfirmPassword = ref(false)
const submitting = ref(false)
const topLevelError = ref<string | null>(null)
const fieldErrors = ref<Record<string, string[]>>({})

function readQueryParam(name: string): string {
  const value = route.query[name]
  return typeof value === 'string' ? value : ''
}

const userPublicId = readQueryParam('publicId')
const token = readQueryParam('token')

const status = ref<Status>(userPublicId && token ? 'form' : 'missing-params')

const passwordMismatch = computed(() =>
  confirmPassword.value.length > 0 && confirmPassword.value !== newPassword.value,
)

function errorsFor(field: string): string[] {
  return fieldErrors.value[field] ?? []
}

async function handleSubmit(): Promise<void> {
  fieldErrors.value = {}
  topLevelError.value = null

  if (passwordMismatch.value) {
    fieldErrors.value = { newPassword: ['密碼與確認密碼不一致。'] }
    return
  }

  submitting.value = true
  try {
    await confirmPasswordReset({
      userPublicId,
      token,
      newPassword: newPassword.value,
    })
    status.value = 'success'
  } catch (error) {
    if (isApiError(error)) {
      if (error.code === 'password_reset_token_invalid') {
        topLevelError.value = '重設連結無效、已使用或已過期，請重新申請忘記密碼。'
      } else if (error.fieldErrors) {
        fieldErrors.value = error.fieldErrors
      } else {
        topLevelError.value = error.message
      }
    } else {
      topLevelError.value = '重設密碼時發生未預期的錯誤，請稍後再試一次。'
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <EmptyState
    v-if="status === 'missing-params'"
    title="重設連結不完整"
    description="請重新點擊密碼重設信中的完整連結，或重新申請一次。"
  >
    <RouterLink to="/forgot-password">
      重新申請
    </RouterLink>
  </EmptyState>

  <EmptyState
    v-else-if="status === 'success'"
    title="密碼已重設"
    description="您的密碼已更新，其他裝置的登入狀態也已一併登出，請使用新密碼重新登入。"
  >
    <RouterLink to="/login">
      前往登入
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
      <label for="reset-password-new">新密碼</label>
      <div class="password-field">
        <input
          id="reset-password-new"
          v-model="newPassword"
          :type="showPassword ? 'text' : 'password'"
          autocomplete="new-password"
          minlength="12"
          maxlength="128"
          required
          :aria-invalid="errorsFor('newPassword').length > 0"
        >
        <PasswordVisibilityToggle v-model="showPassword" />
      </div>
      <p class="form-field__hint">
        至少 12 個字元。
      </p>
      <p
        v-for="message in errorsFor('newPassword')"
        :key="message"
        class="form-field__error"
      >
        {{ message }}
      </p>
    </div>

    <div class="form-field">
      <label for="reset-password-confirm">確認新密碼</label>
      <div class="password-field">
        <input
          id="reset-password-confirm"
          v-model="confirmPassword"
          :type="showConfirmPassword ? 'text' : 'password'"
          autocomplete="new-password"
          required
          :aria-invalid="passwordMismatch"
        >
        <PasswordVisibilityToggle v-model="showConfirmPassword" />
      </div>
      <p
        v-if="passwordMismatch"
        class="form-field__error"
      >
        密碼與確認密碼不一致。
      </p>
    </div>

    <button
      type="submit"
      :disabled="submitting || passwordMismatch"
    >
      {{ submitting ? '設定中…' : '設定新密碼' }}
    </button>
  </form>
</template>

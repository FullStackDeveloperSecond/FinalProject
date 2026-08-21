<script setup lang="ts">
import { computed, ref } from 'vue'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState } from '@doselect/web-shared/components'
import { CURRENT_TERMS_VERSION, registerMember, type RegisterAcceptedResponseBody } from './api'
import PasswordVisibilityToggle from '../../components/PasswordVisibilityToggle.vue'

const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const displayName = ref('')
const acceptTerms = ref(false)
const showPassword = ref(false)
const showConfirmPassword = ref(false)

const submitting = ref(false)
const fieldErrors = ref<Record<string, string[]>>({})
const topLevelError = ref<string | null>(null)
const registered = ref<RegisterAcceptedResponseBody | null>(null)

const passwordMismatch = computed(() =>
  confirmPassword.value.length > 0 && confirmPassword.value !== password.value,
)

function errorsFor(field: string): string[] {
  return fieldErrors.value[field] ?? []
}

async function handleSubmit(): Promise<void> {
  fieldErrors.value = {}
  topLevelError.value = null

  const clientErrors: Record<string, string[]> = {}
  if (passwordMismatch.value) {
    clientErrors.confirmPassword = ['密碼與確認密碼不一致。']
  }
  if (!acceptTerms.value) {
    clientErrors.acceptTermsVersion = ['請先閱讀並同意服務條款與隱私權政策。']
  }
  if (Object.keys(clientErrors).length > 0) {
    fieldErrors.value = clientErrors
    return
  }

  submitting.value = true
  try {
    registered.value = await registerMember({
      email: email.value.trim(),
      password: password.value,
      displayName: displayName.value.trim(),
      acceptTermsVersion: CURRENT_TERMS_VERSION,
    })
  } catch (error) {
    if (isApiError(error)) {
      if (error.code === 'account_email_in_use') {
        fieldErrors.value = { email: ['此 Email 已被註冊，請改用其他 Email 或直接登入。'] }
      } else if (error.fieldErrors) {
        fieldErrors.value = error.fieldErrors
      } else {
        topLevelError.value = error.message
      }
    } else {
      topLevelError.value = '註冊時發生未預期的錯誤，請稍後再試一次。'
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <EmptyState
    v-if="registered"
    title="請完成 Email 驗證"
    :description="`我們已寄出驗證信至 ${registered.emailMasked}，請於 24 小時內點擊信中連結完成驗證。`"
  >
    <RouterLink to="/">
      回首頁
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
      <label for="register-email">電子郵件</label>
      <input
        id="register-email"
        v-model="email"
        type="email"
        autocomplete="email"
        required
        :aria-invalid="errorsFor('email').length > 0"
      >
      <p
        v-for="message in errorsFor('email')"
        :key="message"
        class="form-field__error"
      >
        {{ message }}
      </p>
    </div>

    <div class="form-field">
      <label for="register-password">密碼</label>
      <div class="password-field">
        <input
          id="register-password"
          v-model="password"
          :type="showPassword ? 'text' : 'password'"
          autocomplete="new-password"
          minlength="12"
          maxlength="128"
          required
          :aria-invalid="errorsFor('password').length > 0"
        >
        <PasswordVisibilityToggle v-model="showPassword" />
      </div>
      <p class="form-field__hint">
        至少 12 個字元。
      </p>
      <p
        v-for="message in errorsFor('password')"
        :key="message"
        class="form-field__error"
      >
        {{ message }}
      </p>
    </div>

    <div class="form-field">
      <label for="register-confirm-password">確認密碼</label>
      <div class="password-field">
        <input
          id="register-confirm-password"
          v-model="confirmPassword"
          :type="showConfirmPassword ? 'text' : 'password'"
          autocomplete="new-password"
          required
          :aria-invalid="passwordMismatch || errorsFor('confirmPassword').length > 0"
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

    <div class="form-field">
      <label for="register-display-name">姓名</label>
      <input
        id="register-display-name"
        v-model="displayName"
        type="text"
        autocomplete="name"
        maxlength="100"
        required
        :aria-invalid="errorsFor('displayName').length > 0"
      >
      <p
        v-for="message in errorsFor('displayName')"
        :key="message"
        class="form-field__error"
      >
        {{ message }}
      </p>
    </div>

    <div class="form-checkbox">
      <input
        id="register-accept-terms"
        v-model="acceptTerms"
        type="checkbox"
      >
      <label for="register-accept-terms">我同意服務條款與隱私權政策</label>
    </div>
    <p
      v-for="message in errorsFor('acceptTermsVersion')"
      :key="message"
      class="form-field__error"
    >
      {{ message }}
    </p>

    <button
      type="submit"
      :disabled="submitting || passwordMismatch"
    >
      {{ submitting ? '註冊中…' : '立即註冊' }}
    </button>

    <p class="auth-form__switch">
      已有帳戶？<RouterLink to="/login">
        立即登入
      </RouterLink>
    </p>
  </form>
</template>

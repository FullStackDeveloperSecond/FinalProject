<script setup lang="ts">
import { computed, ref } from 'vue'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState } from '@doselect/web-shared/components'
import { CURRENT_TERMS_VERSION, registerMember, type RegisterAcceptedResponseBody } from './api'
import PasswordVisibilityToggle from '../../components/PasswordVisibilityToggle.vue'
import { clearRegistrationDraft, registrationDraft } from './registrationDraft'

const showPassword = ref(false)
const showConfirmPassword = ref(false)

const submitting = ref(false)
const fieldErrors = ref<Record<string, string[]>>({})
const topLevelError = ref<string | null>(null)
const registered = ref<RegisterAcceptedResponseBody | null>(null)
const touched = ref<Record<string, boolean>>({})
const validationMessages: Record<string, string> = {
  email: '請輸入有效的電子郵件地址，長度須為 3 至 320 個字元。',
  password: '密碼長度須為 12 至 128 個字元。',
  displayName: '請輸入姓名，長度須為 1 至 100 個字元。',
  confirmPassword: '請再次輸入相同密碼。',
  acceptTermsVersion: '請先閱讀並同意服務條款與隱私權政策。',
}
const localErrors = computed<Record<string, string[]>>(() => {
  const errors: Record<string, string[]> = {}
  if (!registrationDraft.displayName.trim() || registrationDraft.displayName.trim().length > 100) errors.displayName = [validationMessages.displayName!]
  if (registrationDraft.email.trim().length > 320 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(registrationDraft.email.trim())) errors.email = [validationMessages.email!]
  if (registrationDraft.password.length < 12 || registrationDraft.password.length > 128) errors.password = [validationMessages.password!]
  if (!registrationDraft.confirmPassword || registrationDraft.confirmPassword !== registrationDraft.password) errors.confirmPassword = [validationMessages.confirmPassword!]
  return errors
})

const passwordMismatch = computed(() =>
  registrationDraft.confirmPassword.length > 0 && registrationDraft.confirmPassword !== registrationDraft.password,
)

function errorsFor(field: string): string[] {
  return (touched.value[field] ? localErrors.value[field] : undefined) ?? fieldErrors.value[field] ?? []
}

async function handleSubmit(): Promise<void> {
  fieldErrors.value = {}
  topLevelError.value = null

  touched.value = { displayName: true, email: true, password: true, confirmPassword: true }
  const clientErrors: Record<string, string[]> = { ...localErrors.value }
  if (passwordMismatch.value) {
    clientErrors.confirmPassword = ['密碼與確認密碼不一致。']
  }
  if (!registrationDraft.acceptTerms) {
    clientErrors.acceptTermsVersion = ['請先閱讀並同意服務條款與隱私權政策。']
  }
  if (Object.keys(clientErrors).length > 0) {
    fieldErrors.value = clientErrors
    return
  }

  submitting.value = true
  try {
    registered.value = await registerMember({
      email: registrationDraft.email.trim(),
      password: registrationDraft.password,
      displayName: registrationDraft.displayName.trim(),
      acceptTermsVersion: CURRENT_TERMS_VERSION,
    })
    clearRegistrationDraft()
  } catch (error) {
    if (isApiError(error)) {
      if (error.code === 'account_email_in_use') {
        fieldErrors.value = { email: ['此 Email 已被註冊，請改用其他 Email 或直接登入。'] }
      } else if (error.fieldErrors) {
        fieldErrors.value = Object.fromEntries(Object.entries(error.fieldErrors).map(([field, messages]) => {
          const key = field.charAt(0).toLowerCase() + field.slice(1)
          return [key, messages.map(message => /[\u3400-\u9fff]/.test(message) ? message : validationMessages[key] ?? '欄位內容不符合規定，請檢查後再試。')]
        }))
      } else {
        topLevelError.value = /[\u3400-\u9fff]/.test(error.message) ? error.message : '註冊失敗，請確認資料後再試一次。'
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
    :description="`若 ${registered.emailMasked} 尚未註冊過，我們已寄出驗證信，請於 24 小時內點擊信中連結完成驗證；若此信箱已經註冊過，請直接查看您原本收到的驗證信或改用登入頁。`"
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
      <label for="register-display-name">姓名 *</label>
      <input
        id="register-display-name"
        v-model="registrationDraft.displayName"
        type="text"
        autocomplete="name"
        maxlength="100"
        required
        :aria-invalid="errorsFor('displayName').length > 0"
        @blur="touched.displayName = true"
        @input="fieldErrors.displayName = []"
      >
      <p
        v-for="message in errorsFor('displayName')"
        :key="message"
        class="form-field__error"
      >
        {{ message }}
      </p>
    </div>

    <div class="form-field">
      <label for="register-email">電子郵件 *</label>
      <input
        id="register-email"
        v-model="registrationDraft.email"
        type="email"
        autocomplete="email"
        required
        maxlength="320"
        :aria-invalid="errorsFor('email').length > 0"
        @blur="touched.email = true"
        @input="fieldErrors.email = []"
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
      <label for="register-password">密碼 *</label>
      <div class="password-field">
        <input
          id="register-password"
          v-model="registrationDraft.password"
          :type="showPassword ? 'text' : 'password'"
          autocomplete="new-password"
          minlength="12"
          maxlength="128"
          required
          :aria-invalid="errorsFor('password').length > 0"
          @blur="touched.password = true"
          @input="fieldErrors.password = []"
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
      <label for="register-confirm-password">確認密碼 *</label>
      <div class="password-field">
        <input
          id="register-confirm-password"
          v-model="registrationDraft.confirmPassword"
          :type="showConfirmPassword ? 'text' : 'password'"
          autocomplete="new-password"
          required
          :aria-invalid="passwordMismatch || errorsFor('confirmPassword').length > 0"
          @blur="touched.confirmPassword = true"
        >
        <PasswordVisibilityToggle v-model="showConfirmPassword" />
      </div>
      <p
        v-if="passwordMismatch || errorsFor('confirmPassword').length"
        class="form-field__error"
      >
        密碼與確認密碼不一致。
      </p>
    </div>

    <div class="form-checkbox">
      <input
        id="register-accept-terms"
        v-model="registrationDraft.acceptTerms"
        type="checkbox"
      >
      <label for="register-accept-terms">
        我同意<RouterLink
          to="/terms"
        >服務條款</RouterLink>與<RouterLink
          to="/privacy"
        >隱私權政策</RouterLink>
      </label>
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

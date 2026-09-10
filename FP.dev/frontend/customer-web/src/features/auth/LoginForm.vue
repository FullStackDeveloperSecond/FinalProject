<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { useSessionStore } from '../../stores/session'
import PasswordVisibilityToggle from '../../components/PasswordVisibilityToggle.vue'
import { resolveSafeRedirect } from '../../router/safeRedirect'
import { requestEmailVerification } from './api'

const email = ref('')
const password = ref('')
const rememberMe = ref(false)
const showPassword = ref(false)
const submitting = ref(false)
const topLevelError = ref<string | null>(null)
const emailUnverified = ref(false)
const resendState = ref<'idle' | 'sending' | 'sent' | 'error'>('idle')
const resendError = ref<string | null>(null)

const router = useRouter()
const route = useRoute()
const sessionStore = useSessionStore()

async function handleSubmit(): Promise<void> {
  topLevelError.value = null
  emailUnverified.value = false
  resendState.value = 'idle'
  resendError.value = null
  submitting.value = true

  try {
    await sessionStore.login({
      email: email.value.trim(),
      password: password.value,
      rememberMe: rememberMe.value,
    })
    // 組長 PR #35 review, item 2: a shopper sent here from NewBuildPage (guest draft → login)
    // must land back where they came from, not always the home page — see safeRedirect.ts for
    // why this one consumption point is where the value gets validated.
    await router.push(resolveSafeRedirect(route.query.redirect))
  } catch (error) {
    topLevelError.value = resolveErrorMessage(error)
    emailUnverified.value = isApiError(error) && error.code === 'account_email_unverified'
  } finally {
    submitting.value = false
  }
}

async function handleResendVerification(): Promise<void> {
  if (resendState.value === 'sending') {
    return
  }

  resendState.value = 'sending'
  resendError.value = null
  try {
    await requestEmailVerification({ email: email.value.trim() })
    resendState.value = 'sent'
  } catch (error) {
    // Falling back to 'idle' here used to swallow the failure entirely — the button just
    // re-enabled with no indication the resend never happened. Surface it instead, without
    // revealing anything account-specific (Alex review, 2026-08-24).
    resendState.value = 'error'
    resendError.value = isApiError(error) && error.code === 'rate_limit_exceeded'
      ? '請求過於頻繁，請稍後再試一次。'
      : '重新寄送驗證信時發生錯誤，請稍後再試一次。'
  }
}

function resolveErrorMessage(error: unknown): string {
  if (!isApiError(error)) {
    return '登入時發生未預期的錯誤，請稍後再試一次。'
  }

  switch (error.code) {
    case 'invalid_credentials':
      // Also covers an internally-locked account: the API deliberately returns the same
      // invalid_credentials response for both so a locked account and a wrong password (or a
      // nonexistent account) cannot be told apart from the outside.
      return 'Email 或密碼錯誤，請再試一次。'
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
    <div
      v-if="topLevelError"
      class="form-banner form-banner--error"
      role="alert"
    >
      <p class="form-banner__message">
        {{ topLevelError }}
      </p>
      <div
        v-if="emailUnverified"
        class="form-banner__actions"
      >
        <button
          type="button"
          class="resend-verification"
          :disabled="resendState === 'sending'"
          aria-label="重新寄送驗證信"
          title="重新寄送驗證信"
          @click="handleResendVerification"
        >
          <svg
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 24 24"
            width="18"
            height="18"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <rect
              x="2"
              y="4"
              width="20"
              height="16"
              rx="2"
            />
            <path d="m2 7 10 6 10-6" />
          </svg>
          <span>{{ resendState === 'sending' ? '寄送中…' : resendState === 'sent' ? '已重新寄出' : '重新寄送驗證信' }}</span>
        </button>
        <p
          v-if="resendState === 'error'"
          class="form-field__error"
        >
          {{ resendError }}
        </p>
      </div>
    </div>

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
      <div class="password-field">
        <input
          id="login-password"
          v-model="password"
          :type="showPassword ? 'text' : 'password'"
          autocomplete="current-password"
          required
        >
        <PasswordVisibilityToggle v-model="showPassword" />
      </div>
      <RouterLink
        to="/forgot-password"
        class="form-field__forgot-link"
      >
        忘記密碼？
      </RouterLink>
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

<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'

const auth = useAdminAuthStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')

async function onSubmit(): Promise<void> {
  await auth.login(email.value, password.value)
  if (!auth.challenge) {
    return
  }
  // ⚠ alex review 第三輪 P2#4：router guard 或 401 handler 導來這裡時，可能帶著
  // ?redirect=... 記住使用者原本要去的深層連結——密碼這關過了還要接著走 MFA，這裡把它原樣
  // 轉給下一個頁面（不在這裡驗證安全性；真正的消費點在 TotpVerifyPage／TotpEnrollPage 完成
  // 後導覽的那一刻，由 resolveSafeRedirect 統一擋掉惡意值），不能讓它在這個中繼站弄丟。
  await router.push({
    path: auth.challenge.kind === 'enroll' ? '/login/enroll' : '/login/verify',
    query: route.query.redirect ? { redirect: route.query.redirect } : {},
  })
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

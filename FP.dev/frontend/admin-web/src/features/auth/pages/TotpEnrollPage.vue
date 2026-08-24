<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { LoadingState } from '@doselect/web-shared/components'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'

const auth = useAdminAuthStore()
const router = useRouter()

const secretKey = ref('')
const qrCodeDataUri = ref('')
const code = ref('')
const recoveryCodes = ref<string[] | null>(null)
const acknowledged = ref(false)
const initializing = ref(true)

onMounted(async () => {
  const begin = await auth.beginEnrollment()
  if (begin) {
    secretKey.value = begin.secretKey
    qrCodeDataUri.value = begin.qrCodeDataUri
  }
  initializing.value = false
})

async function onConfirm(): Promise<void> {
  const codes = await auth.confirmEnrollment(code.value)
  if (codes) {
    recoveryCodes.value = codes
  }
}

async function onAcknowledge(): Promise<void> {
  acknowledged.value = true
  await router.push('/')
}
</script>

<template>
  <div class="auth-page">
    <LoadingState v-if="initializing" />

    <div
      v-else-if="recoveryCodes"
      class="auth-card"
    >
      <h1>請保存您的備援碼</h1>
      <p class="auth-card__subtitle">
        每組備援碼只能使用一次，且離開此頁面後無法再次查看，請立即抄下並妥善保管。
      </p>
      <ul class="recovery-code-list">
        <li
          v-for="recoveryCode in recoveryCodes"
          :key="recoveryCode"
        >
          {{ recoveryCode }}
        </li>
      </ul>
      <label class="field field--checkbox">
        <input
          v-model="acknowledged"
          type="checkbox"
        >
        <span>我已抄下並妥善保存這些備援碼</span>
      </label>
      <button
        type="button"
        class="auth-card__submit"
        :disabled="!acknowledged"
        @click="onAcknowledge"
      >
        完成，進入後台
      </button>
    </div>

    <form
      v-else
      class="auth-card"
      @submit.prevent="onConfirm"
    >
      <h1>綁定兩步驟驗證</h1>
      <p class="auth-card__subtitle">
        首次登入需先綁定 Authenticator App（如 Google Authenticator）才能繼續。
      </p>

      <img
        v-if="qrCodeDataUri"
        :src="qrCodeDataUri"
        alt="TOTP 綁定 QR Code"
        class="totp-qr-code"
      >

      <p
        v-if="secretKey"
        class="totp-secret"
      >
        或手動輸入金鑰：<code>{{ secretKey }}</code>
      </p>

      <label class="field">
        <span class="field__label">請輸入 App 顯示的 6 位數驗證碼以確認綁定</span>
        <input
          v-model="code"
          type="text"
          inputmode="numeric"
          maxlength="6"
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
        {{ auth.loading ? '確認中…' : '確認綁定' }}
      </button>
    </form>
  </div>
</template>

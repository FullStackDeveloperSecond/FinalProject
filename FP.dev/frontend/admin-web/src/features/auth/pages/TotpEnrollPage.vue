<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { LoadingState } from '@doselect/web-shared/components'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'
import { resolveSafeRedirect } from '../../../router/safeRedirect'

const auth = useAdminAuthStore()
const router = useRouter()
const route = useRoute()

const secretKey = ref('')
const qrCodeDataUri = ref('')
const code = ref('')
const recoveryCodes = ref<string[] | null>(null)
const acknowledged = ref(false)
const initializing = ref(true)

onMounted(async () => {
  // router guard 的 requiresChallenge 已經會擋下這個情境，這裡是第二層防呆——沒有
  // challenge 就呼叫 beginEnrollment 只會靜默失敗，留下一個看起來像壞掉的空白表單。
  if (!auth.challenge) {
    await router.replace({ name: 'login' })
    return
  }

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
    return
  }
  // 超過嘗試上限時 store 會清掉 challenge（後端也已讓它失效），這裡導回登入頁，
  // 而不是留在一個再也送不出去的表單上（alex review P2）。
  if (!auth.challenge) {
    await router.replace({ name: 'login' })
  }
}

async function onAcknowledge(): Promise<void> {
  acknowledged.value = true
  // ⚠ alex review 第三輪 P2#4：同 TotpVerifyPage——導回原本要去的深層連結，經
  // resolveSafeRedirect 驗證只接受同源站內路徑，而不是永遠固定回首頁。
  await router.push(resolveSafeRedirect(route.query.redirect))
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

<script setup lang="ts">
import { ref } from 'vue'
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
// 一進頁面就自動呼叫重設金鑰太容易被誤觸發（例如瀏覽器上一頁/下一頁、
// 重新整理），所以改成要先按「開始重新綁定」才會真的呼叫後端重設。
const started = ref(false)

async function onStart(): Promise<void> {
  started.value = true
  const begin = await auth.beginRebind()
  if (begin) {
    secretKey.value = begin.secretKey
    qrCodeDataUri.value = begin.qrCodeDataUri
  }
}

async function onConfirm(): Promise<void> {
  const codes = await auth.confirmRebind(code.value)
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
  <section class="page">
    <h1>重新綁定 TOTP</h1>
    <p>
      換了新手機或遺失原本的 Authenticator App 時，可以在這裡重新綁定。完成後，
      這台裝置的登入會維持有效，但其他所有裝置上既有的登入 Session 會全部失效，
      需要重新登入並用新的驗證碼。
    </p>

    <div
      v-if="!started"
      class="auth-card"
    >
      <p class="field-error">
        ⚠ 按下開始後，目前的驗證碼設定會立刻失效，其他所有裝置上既有的登入 Session
        也會全部失效。請確定手邊已經有新的 Authenticator App 準備好再繼續。
      </p>
      <button
        type="button"
        class="auth-card__submit"
        @click="onStart"
      >
        開始重新綁定
      </button>
    </div>

    <LoadingState v-else-if="!secretKey && !recoveryCodes" />

    <div
      v-else-if="recoveryCodes"
      class="auth-card"
    >
      <h2>請保存您的新備援碼</h2>
      <p class="auth-card__subtitle">
        原本的備援碼已經失效，每組新備援碼只能使用一次，且離開此頁面後無法再次查看，請立即抄下並妥善保管。
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
        完成
      </button>
    </div>

    <form
      v-else
      class="auth-card"
      @submit.prevent="onConfirm"
    >
      <img
        v-if="qrCodeDataUri"
        :src="qrCodeDataUri"
        alt="TOTP 重新綁定 QR Code"
        class="totp-qr-code"
      >

      <p
        v-if="secretKey"
        class="totp-secret"
      >
        或手動輸入金鑰：<code>{{ secretKey }}</code>
      </p>

      <label class="field">
        <span class="field__label">請輸入新裝置 App 顯示的 6 位數驗證碼以確認</span>
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
        {{ auth.loading ? '確認中…' : '確認重新綁定' }}
      </button>
    </form>
  </section>
</template>

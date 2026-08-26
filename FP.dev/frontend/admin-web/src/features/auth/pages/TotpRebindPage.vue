<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'

const auth = useAdminAuthStore()
const router = useRouter()

const secretKey = ref('')
const qrCodeDataUri = ref('')
const code = ref('')
const recoveryCodes = ref<string[] | null>(null)
const acknowledged = ref(false)
// 一進頁面就自動呼叫重設金鑰太容易被誤觸發（例如瀏覽器上一頁/下一頁、
// 重新整理），所以改成要先完成 step-up 驗證才會真的呼叫後端重設。
const started = ref(false)

// ⚠ alex review 裁定 A1：只有既有 Session 不足以簽發 rebind challenge，必須先證明目前仍握有
// 一組有效的 TOTP 驗證碼或 Recovery Code，兩者擇一。
const stepUpMethod = ref<'totp' | 'recoveryCode'>('totp')
const stepUpCode = ref('')

function resetToStepUp(): void {
  started.value = false
  secretKey.value = ''
  qrCodeDataUri.value = ''
  code.value = ''
  stepUpCode.value = ''
}

// alex review P2：rebindChallengePublicId 被 store 清掉（過期／限流／SecurityStamp 不符）代表
// 這張 challenge 已死，畫面卻仍停在「輸入驗證碼」的 confirm 表單——重送必然再次失敗。這裡讓頁面
// 跟著退回「開始重新綁定」的 step-up 畫面，而不是留著一個看起來還能用、實際上已經失效的表單。
watch(
  () => auth.rebindChallengePublicId,
  (challengePublicId) => {
    if (challengePublicId === null && started.value && !recoveryCodes.value) {
      resetToStepUp()
    }
  },
)

async function onStepUpSubmit(): Promise<void> {
  // 只有拿到 secret 才切到 started=true——原本先設 true 再打 API，失敗時（401／500／
  // 網路異常）secretKey 依然是空字串，畫面永遠卡在 LoadingState，沒有錯誤訊息也沒有
  // 重試按鈕（alex review P2）。失敗時維持在「開始」卡片，auth.errorMessage 會顯示
  // 原因，按鈕本身就是重試入口。
  const begin = await auth.beginRebind(
    stepUpMethod.value === 'totp'
      ? { totpCode: stepUpCode.value }
      : { recoveryCode: stepUpCode.value },
  )
  if (begin) {
    secretKey.value = begin.secretKey
    qrCodeDataUri.value = begin.qrCodeDataUri
    started.value = true
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

    <form
      v-if="!started"
      class="auth-card"
      @submit.prevent="onStepUpSubmit"
    >
      <p class="field-error">
        ⚠ 確認身分後，目前的驗證碼設定會立刻失效，其他所有裝置上既有的登入 Session
        也會全部失效。請確定手邊已經有新的 Authenticator App 準備好再繼續。
      </p>

      <p>為了確認是您本人操作，請輸入目前仍有效的驗證碼，或使用一組備援碼。</p>

      <fieldset class="field field--radio">
        <label>
          <input
            v-model="stepUpMethod"
            type="radio"
            value="totp"
          >
          <span>目前的 6 位數驗證碼</span>
        </label>
        <label>
          <input
            v-model="stepUpMethod"
            type="radio"
            value="recoveryCode"
          >
          <span>備援碼</span>
        </label>
      </fieldset>

      <label class="field">
        <span class="field__label">
          {{ stepUpMethod === 'totp' ? '目前的 6 位數驗證碼' : '備援碼' }}
        </span>
        <input
          v-model="stepUpCode"
          type="text"
          :inputmode="stepUpMethod === 'totp' ? 'numeric' : 'text'"
          :maxlength="stepUpMethod === 'totp' ? 6 : 64"
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
        {{ auth.loading ? '處理中…' : '驗證並開始重新綁定' }}
      </button>
    </form>

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

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'
import { resolveSafeRedirect } from '../../../router/safeRedirect'

const auth = useAdminAuthStore()
const router = useRouter()
const route = useRoute()

const code = ref('')
const useRecoveryCode = ref(false)

// router guard 的 requiresChallenge 已經會擋下這個情境，這裡是第二層防呆
// （例如未來有其他方式導覽進來），避免卡在沒有 challenge 可用的死頁面。
onMounted(() => {
  if (!auth.challenge) {
    router.replace({ name: 'login' })
  }
})

async function onSubmit(): Promise<void> {
  const succeeded = useRecoveryCode.value
    ? await auth.useRecoveryCode(code.value)
    : await auth.verifyTotp(code.value)
  if (succeeded) {
    // ⚠ alex review 第三輪 P2#4：完成 MFA 後導回原本要去的深層連結，而不是永遠固定回首頁；
    // route.query.redirect 是使用者可控的查詢參數（可能來自惡意連結），在這個唯一的消費點
    // 用 resolveSafeRedirect 驗證只接受同源站內路徑。
    await router.push(resolveSafeRedirect(route.query.redirect))
    return
  }
  // 超過嘗試上限時 store 會清掉 challenge（後端也已讓它失效），這裡導回登入頁，
  // 而不是留在一個再也送不出去的表單上（alex review P2）。
  if (!auth.challenge) {
    await router.replace({ name: 'login' })
  }
}

function toggleRecoveryCode(): void {
  useRecoveryCode.value = !useRecoveryCode.value
  code.value = ''
  auth.errorMessage = null
}
</script>

<template>
  <div class="auth-page">
    <form
      class="auth-card"
      @submit.prevent="onSubmit"
    >
      <h1>兩步驟驗證</h1>
      <p class="auth-card__subtitle">
        {{ useRecoveryCode ? '請輸入其中一組備援碼' : '請輸入 Authenticator App 顯示的 6 位數驗證碼' }}
      </p>

      <label class="field">
        <span class="field__label">{{ useRecoveryCode ? '備援碼' : '驗證碼' }}</span>
        <input
          v-model="code"
          type="text"
          inputmode="numeric"
          autocomplete="one-time-code"
          :maxlength="useRecoveryCode ? 64 : 6"
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
        {{ auth.loading ? '驗證中…' : '驗證' }}
      </button>

      <button
        type="button"
        class="auth-card__link"
        @click="toggleRecoveryCode"
      >
        {{ useRecoveryCode ? '改用驗證碼' : '改用備援碼' }}
      </button>
    </form>
  </div>
</template>

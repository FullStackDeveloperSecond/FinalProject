<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'

const auth = useAdminAuthStore()
const router = useRouter()

const code = ref('')
const useRecoveryCode = ref(false)

async function onSubmit(): Promise<void> {
  const succeeded = useRecoveryCode.value
    ? await auth.useRecoveryCode(code.value)
    : await auth.verifyTotp(code.value)
  if (succeeded) {
    await router.push('/')
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

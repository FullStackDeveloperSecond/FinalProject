<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { requestGuestOrderAccess } from './api'

const router = useRouter()

const orderNumber = ref('')
const email = ref('')
const submitting = ref(false)
const topLevelError = ref<string | null>(null)

async function handleSubmit(): Promise<void> {
  topLevelError.value = null
  submitting.value = true
  try {
    const accepted = await requestGuestOrderAccess({
      orderNumber: orderNumber.value.trim(),
      email: email.value.trim(),
    })
    await router.push({
      name: 'guest-order-verify',
      query: {
        requestId: accepted.requestPublicId,
        expiresAt: accepted.expiresAtUtc,
      },
    })
  } catch (error) {
    // 回應恆定不透露訂單／Email 是否存在，失敗訊息也不能——只能說「這次請求沒有送出」
    // (參照 ForgotPasswordForm 的同一條規則)。
    topLevelError.value = resolveErrorMessage(error)
  } finally {
    submitting.value = false
  }
}

function resolveErrorMessage(error: unknown): string {
  if (isApiError(error) && error.code === 'rate_limit_exceeded') {
    return '請求過於頻繁，請稍後再試一次。'
  }

  return '查詢訂單時發生錯誤，請稍後再試一次。'
}
</script>

<template>
  <form
    class="guest-order-access-form"
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
      <label for="guest-order-number">訂單編號</label>
      <input
        id="guest-order-number"
        v-model="orderNumber"
        type="text"
        autocomplete="off"
        required
      >
    </div>

    <div class="form-field">
      <label for="guest-order-email">下單時使用的 Email</label>
      <input
        id="guest-order-email"
        v-model="email"
        type="email"
        autocomplete="email"
        required
      >
      <p class="form-field__hint">
        我們會將六位數驗證碼寄到這個信箱，驗證碼 10 分鐘內有效。
      </p>
    </div>

    <button
      type="submit"
      :disabled="submitting"
    >
      {{ submitting ? '查詢中…' : '查詢訂單' }}
    </button>
  </form>
</template>

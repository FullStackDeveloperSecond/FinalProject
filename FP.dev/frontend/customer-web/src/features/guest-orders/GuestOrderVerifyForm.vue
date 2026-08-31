<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState } from '@doselect/web-shared/components'
import { resendGuestOrderAccess, verifyGuestOrderAccess } from './api'

const route = useRoute()
const router = useRouter()

const requestId = computed(() => {
  const value = route.query.requestId
  return typeof value === 'string' ? value : ''
})
const expiresAt = computed(() => {
  const value = route.query.expiresAt
  return typeof value === 'string' ? value : undefined
})

const code = ref('')
const submitting = ref(false)
const verifyError = ref<string | null>(null)

const resending = ref(false)
const resendMessage = ref<string | null>(null)
const resendDisabledUntil = ref<number>(0)
const now = ref(Date.now())
const resendCountdown = computed(() => Math.max(0, Math.ceil((resendDisabledUntil.value - now.value) / 1000)))

function tickResendCountdown(): void {
  if (resendDisabledUntil.value <= Date.now()) {
    return
  }
  now.value = Date.now()
  globalThis.setTimeout(tickResendCountdown, 1000)
}

function formatExpiry(value?: string): string {
  if (!value) {
    return ''
  }
  return new Date(value).toLocaleTimeString('zh-TW')
}

async function handleSubmit(): Promise<void> {
  if (!requestId.value) {
    return
  }

  verifyError.value = null
  submitting.value = true
  try {
    const verified = await verifyGuestOrderAccess({
      requestPublicId: requestId.value,
      code: code.value.trim(),
    })
    await router.push({ name: 'order-detail', params: { orderId: verified.orderPublicId } })
  } catch (error) {
    // 不論碼錯誤、過期或請求本身不存在，一律回相同訊息——不揭露是哪一種原因
    // (GuestOrderAccessUseCase.VerifyAsync 全部回同一個 verification_invalid)。
    verifyError.value = isApiError(error) && error.code === 'rate_limit_exceeded'
      ? '請求過於頻繁，請稍後再試一次。'
      : '驗證碼錯誤或已過期，請確認後重新輸入，或重新查詢訂單取得新的驗證碼。'
  } finally {
    submitting.value = false
  }
}

async function handleResend(): Promise<void> {
  if (!requestId.value || resendCountdown.value > 0) {
    return
  }

  resendMessage.value = null
  resending.value = true
  try {
    const accepted = await resendGuestOrderAccess(requestId.value)
    resendMessage.value = '已重新寄送驗證碼，請查看您的信箱。'
    resendDisabledUntil.value = new Date(accepted.resendAvailableAtUtc).getTime()
    tickResendCountdown()
  } catch (error) {
    resendMessage.value = isApiError(error) && error.code === 'rate_limit_exceeded'
      ? '請求過於頻繁，請稍後再試一次。'
      : '重新寄送時發生錯誤，請稍後再試一次。'
  } finally {
    resending.value = false
  }
}
</script>

<template>
  <EmptyState
    v-if="!requestId"
    title="查無查詢請求"
    description="請重新輸入訂單編號與 Email 查詢訂單。"
  >
    <RouterLink :to="{ name: 'guest-order-access' }">
      重新查詢訂單
    </RouterLink>
  </EmptyState>

  <form
    v-else
    class="guest-order-verify-form"
    novalidate
    @submit.prevent="handleSubmit"
  >
    <p v-if="expiresAt">
      驗證碼將於 {{ formatExpiry(expiresAt) }} 前失效。
    </p>

    <p
      v-if="verifyError"
      class="form-banner form-banner--error"
      role="alert"
    >
      {{ verifyError }}
    </p>

    <div class="form-field">
      <label for="guest-order-code">六位數驗證碼</label>
      <input
        id="guest-order-code"
        v-model="code"
        type="text"
        inputmode="numeric"
        autocomplete="one-time-code"
        minlength="6"
        maxlength="6"
        required
      >
    </div>

    <button
      type="submit"
      :disabled="submitting || code.trim().length !== 6"
    >
      {{ submitting ? '驗證中…' : '驗證並查看訂單' }}
    </button>

    <p
      v-if="resendMessage"
      role="status"
    >
      {{ resendMessage }}
    </p>

    <button
      type="button"
      :disabled="resending || resendCountdown > 0"
      @click="handleResend"
    >
      {{ resendCountdown > 0 ? `重新寄送（${resendCountdown} 秒後可用）` : '沒收到驗證碼？重新寄送' }}
    </button>
  </form>
</template>

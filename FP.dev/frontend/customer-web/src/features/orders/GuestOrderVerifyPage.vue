<script setup lang="ts">
import { computed, ref } from 'vue'
import { isApiError } from '@doselect/web-shared/api'
import { useRoute, useRouter } from 'vue-router'
import { resendGuestOrderAccess, verifyGuestOrderAccess } from './guestAccessApi'

const route = useRoute()
const router = useRouter()
const requestPublicId = computed(() => {
  const value = route.query.requestPublicId
  return typeof value === 'string' ? value : ''
})
const code = ref('')
const isSubmitting = ref(false)
const isResending = ref(false)
const message = ref<string>()
const errorMessage = ref<string>()

async function verify(): Promise<void> {
  if (!requestPublicId.value) {
    errorMessage.value = '驗證要求不存在，請重新申請。'
    return
  }

  isSubmitting.value = true
  errorMessage.value = undefined
  try {
    const verified = await verifyGuestOrderAccess({
      requestPublicId: requestPublicId.value,
      code: code.value.trim(),
    })
    await router.push({
      name: 'order-detail',
      params: { orderId: verified.orderPublicId },
    })
  }
  catch (error) {
    errorMessage.value = isApiError(error)
      && ['guest_order_verification_invalid', 'guest_order_access_expired'].includes(error.code)
      ? '驗證碼無效或已過期，請確認後重試或重新申請。'
      : '目前無法完成驗證，請稍後再試。'
  }
  finally {
    isSubmitting.value = false
  }
}

async function resend(): Promise<void> {
  if (!requestPublicId.value) {
    errorMessage.value = '驗證要求不存在，請重新申請。'
    return
  }

  isResending.value = true
  errorMessage.value = undefined
  message.value = undefined
  try {
    await resendGuestOrderAccess(requestPublicId.value)
    message.value = '若資料仍有效，新的驗證碼已重新寄送。'
  }
  catch (error) {
    errorMessage.value = isApiError(error) && error.status === 429
      ? '重新寄送次數過多，請稍後再試。'
      : '目前無法重新寄送，請稍後再試。'
  }
  finally {
    isResending.value = false
  }
}
</script>

<template>
  <section aria-labelledby="guest-verify-title">
    <h1 id="guest-verify-title">
      驗證訪客訂單
    </h1>
    <p>輸入 Email 中的六位數驗證碼。驗證成功後，只能存取這一筆訂單 30 分鐘。</p>

    <form @submit.prevent="verify">
      <label for="guest-order-code">六位數驗證碼</label>
      <input
        id="guest-order-code"
        v-model="code"
        inputmode="numeric"
        autocomplete="one-time-code"
        pattern="[0-9]{6}"
        minlength="6"
        maxlength="6"
        required
      >
      <button
        type="submit"
        :disabled="isSubmitting || !requestPublicId"
      >
        {{ isSubmitting ? '驗證中…' : '驗證並查看訂單' }}
      </button>
    </form>

    <button
      type="button"
      data-test="resend-code"
      :disabled="isResending || !requestPublicId"
      @click="resend"
    >
      {{ isResending ? '重新寄送中…' : '重新寄送驗證碼' }}
    </button>
    <a href="/guest-orders/access">重新申請</a>

    <p
      v-if="message"
      role="status"
    >
      {{ message }}
    </p>
    <p
      v-if="errorMessage"
      role="alert"
    >
      {{ errorMessage }}
    </p>
  </section>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { isApiError } from '@doselect/web-shared/api'
import { useRouter } from 'vue-router'
import { requestGuestOrderAccess } from './guestAccessApi'

const router = useRouter()
const form = reactive({ orderNumber: '', email: '' })
const isSubmitting = ref(false)
const message = ref<string>()
const errorMessage = ref<string>()

async function submit(): Promise<void> {
  isSubmitting.value = true
  errorMessage.value = undefined
  message.value = undefined
  try {
    const accepted = await requestGuestOrderAccess({
      orderNumber: form.orderNumber.trim(),
      email: form.email.trim(),
    })
    message.value = '若資料相符，驗證碼會寄到訂單 Email。'
    await router.push({
      name: 'guest-order-verify',
      query: { requestPublicId: accepted.requestPublicId },
    })
  }
  catch (error) {
    errorMessage.value = isApiError(error) && error.status === 429
      ? '要求次數過多，請稍後再試。'
      : '目前無法送出驗證要求，請稍後再試。'
  }
  finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <section aria-labelledby="guest-access-title">
    <h1 id="guest-access-title">
      訪客訂單查詢
    </h1>
    <p>輸入訂單編號與下單 Email；不論資料是否相符，畫面都不會揭露訂單是否存在。</p>
    <form @submit.prevent="submit">
      <label for="guest-order-number">訂單編號</label>
      <input
        id="guest-order-number"
        v-model="form.orderNumber"
        required
        maxlength="32"
        autocomplete="off"
      >

      <label for="guest-order-email">訂單 Email</label>
      <input
        id="guest-order-email"
        v-model="form.email"
        type="email"
        required
        maxlength="320"
        autocomplete="email"
      >

      <button
        type="submit"
        :disabled="isSubmitting"
      >
        {{ isSubmitting ? '送出中…' : '寄送驗證碼' }}
      </button>
    </form>
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

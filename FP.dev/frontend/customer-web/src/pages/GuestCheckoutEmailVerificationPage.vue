<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { getOrCreateGuestCartKey } from '../features/cart/guestCartKey'
import { verifyGuestCheckoutEmail } from '../features/checkout/api'

const isVerifying = ref(true)
const isVerified = ref(false)
const errorMessage = ref<string | null>(null)

const fragment = computed(() => new URLSearchParams(window.location.hash.slice(1)))

onMounted(async () => {
  const requestPublicId = fragment.value.get('requestPublicId')
  const code = fragment.value.get('code')
  if (!requestPublicId || !code) {
    errorMessage.value = '驗證連結不完整，請回到結帳頁重新寄送驗證信。'
    isVerifying.value = false
    return
  }

  try {
    await verifyGuestCheckoutEmail(requestPublicId, code, getOrCreateGuestCartKey())
    isVerified.value = true
    window.history.replaceState(null, '', window.location.pathname)
  } catch (caught) {
    errorMessage.value = isApiError(caught) && caught.code === 'guest_checkout_email_verification_invalid'
      ? '驗證連結無效或已過期，請回到結帳頁重新寄送。'
      : '目前無法完成驗證，請稍後再試。'
  } finally {
    isVerifying.value = false
  }
})
</script>

<template>
  <main
    class="verification-page"
    aria-labelledby="verification-title"
  >
    <h1 id="verification-title">
      結帳信箱驗證
    </h1>
    <p v-if="isVerifying">
      正在驗證信箱…
    </p>
    <template v-else-if="isVerified">
      <p role="status">
        信箱驗證完成。您現在可以回到結帳頁建立訂單。
      </p>
      <RouterLink
        class="page-action"
        to="/checkout"
      >
        返回結帳
      </RouterLink>
    </template>
    <template v-else>
      <p role="alert">
        {{ errorMessage }}
      </p>
      <RouterLink
        class="page-action"
        to="/checkout"
      >
        返回結帳
      </RouterLink>
    </template>
  </main>
</template>

<style scoped>
.verification-page { max-width: 38rem; margin: 0 auto; padding: 2rem 1rem; }
</style>

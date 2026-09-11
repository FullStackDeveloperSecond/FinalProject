<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { acceptAdminInvitation } from '../../adminAccounts/api'

type Status = 'form' | 'success' | 'missing'
const route = useRoute()
const router = useRouter()
const newPassword = ref('')
const confirmation = ref('')
const busy = ref(false)
const errorMessage = ref('')

function readFragment(name: string): string {
  const fragment = route.hash.startsWith('#') ? route.hash.slice(1) : route.hash
  return new URLSearchParams(fragment.replaceAll('+', '%2B')).get(name) ?? ''
}

const publicId = readFragment('publicId')
const token = readFragment('token')
const status = ref<Status>(publicId && token ? 'form' : 'missing')
const mismatch = computed(() => confirmation.value.length > 0 && confirmation.value !== newPassword.value)

onMounted(() => {
  if (publicId || token) void router.replace({ path: route.path })
})

async function submit() {
  if (busy.value || mismatch.value) return
  busy.value = true
  errorMessage.value = ''
  try {
    await acceptAdminInvitation({ publicId, token, newPassword: newPassword.value })
    status.value = 'success'
  } catch (error) {
    if (isApiError(error) && error.code === 'admin_invitation_invalid') {
      errorMessage.value = '邀請連結無效、已使用或已過期，請聯絡最高管理員重新寄送。'
    } else if (isApiError(error) && error.fieldErrors?.newPassword?.length) {
      errorMessage.value = error.fieldErrors.newPassword.join(' ')
    } else {
      errorMessage.value = '設定管理員密碼失敗，請稍後再試。'
    }
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="auth-page">
    <section
      v-if="status === 'missing'"
      class="auth-card"
      aria-labelledby="invitation-title"
    >
      <h1 id="invitation-title">
        邀請連結不完整
      </h1>
      <p>請重新開啟邀請信中的完整連結。</p>
      <RouterLink
        class="auth-card__link"
        to="/login"
      >
        返回管理員登入
      </RouterLink>
    </section>
    <section
      v-else-if="status === 'success'"
      class="auth-card"
      aria-labelledby="invitation-success-title"
    >
      <h1 id="invitation-success-title">
        管理員帳號設定完成
      </h1>
      <p>請登入並完成 TOTP 綁定後使用管理後台。</p>
      <RouterLink
        class="auth-card__link"
        to="/login"
      >
        前往管理員登入
      </RouterLink>
    </section>
    <form
      v-else
      class="auth-card"
      @submit.prevent="submit"
    >
      <h1>設定管理員帳號</h1>
      <p class="auth-card__subtitle">
        請設定密碼；首次登入還需要綁定 TOTP。
      </p>
      <label class="field"><span class="field__label">新密碼</span><input
        v-model="newPassword"
        type="password"
        autocomplete="new-password"
        minlength="12"
        maxlength="128"
        required
        :disabled="busy"
      ></label>
      <label class="field"><span class="field__label">確認新密碼</span><input
        v-model="confirmation"
        type="password"
        autocomplete="new-password"
        minlength="12"
        maxlength="128"
        required
        :disabled="busy"
        :aria-invalid="mismatch"
      ></label>
      <p
        v-if="mismatch"
        class="field-error"
      >
        兩次輸入的密碼不一致。
      </p>
      <p
        v-if="errorMessage"
        class="field-error"
        role="alert"
      >
        {{ errorMessage }}
      </p>
      <button
        type="submit"
        class="auth-card__submit"
        :disabled="busy || mismatch"
      >
        {{ busy ? '設定中…' : '設定密碼' }}
      </button>
    </form>
  </div>
</template>

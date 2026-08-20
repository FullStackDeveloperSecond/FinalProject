<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { confirmEmailVerification } from './api'

type Status = 'loading' | 'success' | 'error' | 'missing-params'

const route = useRoute()
const status = ref<Status>('loading')
const errorMessage = ref('')

function readQueryParam(name: string): string {
  const value = route.query[name]
  return typeof value === 'string' ? value : ''
}

async function confirm(): Promise<void> {
  const userPublicId = readQueryParam('publicId')
  const token = readQueryParam('token')

  if (!userPublicId || !token) {
    status.value = 'missing-params'
    return
  }

  status.value = 'loading'
  try {
    await confirmEmailVerification({ userPublicId, token })
    status.value = 'success'
  } catch (error) {
    status.value = 'error'
    errorMessage.value = isApiError(error) && error.code === 'email_token_invalid'
      ? '驗證連結無效、已使用或已過期，請重新註冊或重新申請驗證信。'
      : '驗證時發生未預期的錯誤，請稍後再試一次。'
  }
}

onMounted(confirm)
</script>

<template>
  <LoadingState
    v-if="status === 'loading'"
    label="正在驗證您的 Email…"
  />

  <EmptyState
    v-else-if="status === 'success'"
    title="Email 驗證成功"
    description="您的帳號已啟用，請前往登入頁使用會員帳號。"
  >
    <RouterLink to="/login">
      前往登入
    </RouterLink>
  </EmptyState>

  <EmptyState
    v-else-if="status === 'missing-params'"
    title="驗證連結不完整"
    description="請重新點擊驗證信中的完整連結，或重新註冊以取得新的驗證信。"
  >
    <RouterLink to="/register">
      重新註冊
    </RouterLink>
  </EmptyState>

  <ErrorState
    v-else
    title="Email 驗證失敗"
    :description="errorMessage"
    retry-label="重試"
    @retry="confirm"
  />
</template>

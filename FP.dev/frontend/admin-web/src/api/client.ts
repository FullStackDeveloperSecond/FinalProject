import {
  createAntiforgeryTokenProvider,
  createDoSelectClient,
  isApiError,
  resolveApiBaseUrl,
  type ApiError,
  type paths,
} from '@doselect/web-shared/api'
import router from '../router'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'

export const apiBaseUrl = resolveApiBaseUrl(import.meta.env.VITE_API_BASE_URL)
const antiforgeryTokenProvider = createAntiforgeryTokenProvider({
  baseUrl: apiBaseUrl,
  client: 'admin',
})

export function resetAntiforgeryToken(): void {
  antiforgeryTokenProvider.reset()
}

export function createApiClient<Paths extends object>() {
  return createDoSelectClient<Paths>({
    baseUrl: apiBaseUrl,
    getAntiforgeryToken: antiforgeryTokenProvider.getToken,
    client: 'admin',
  })
}

/**
 * ⚠ alex review 第三輪 P2#4：這個登入頁如今已經存在（本 PR 新增），下面原本「登入頁還不存在
 * 所以先不處理 401」的前提已經失效——Session 過期時，受保護頁面打的 API 401 仍然只會在
 * 觸發它的那個頁面顯示局部錯誤，使用者不會被導回登入，畫面卡在一個已經沒有 Session 的
 * 半殘狀態。改成清掉本地 Session 快取、導向 /login，並帶上目前所在的站內路徑，讓
 * LoginPage／TotpVerifyPage／TotpEnrollPage 在完成登入後可以導回原本要去的頁面
 * （useAdminAuthStore.beginRedirect 附近的 resolveSafeRedirect 負責擋掉惡意 redirect 值）。
 * 403 有明確的目的地，維持導向既有的 /forbidden HttpStatusPage 路由。只掛在 singleton
 * 上——一般的 createApiClient() factory 保持沒有 router 副作用，方便測試。
 */
export function handleGlobalApiError(error: ApiError): void {
  if (!isApiError(error)) {
    return
  }
  if (error.status === 401) {
    // 已經在登入頁／導覽中就不用再導一次，避免跟 router guard 或使用者自己的操作互相搶導覽。
    if (router.currentRoute.value.name === 'login') {
      return
    }
    void useAdminAuthStore().handleSessionExpired(router.currentRoute.value.fullPath)
    return
  }
  if (error.status === 403) {
    void router.push('/forbidden')
  }
}

export const apiClient = createDoSelectClient<paths>({
  baseUrl: apiBaseUrl,
  getAntiforgeryToken: antiforgeryTokenProvider.getToken,
  onApiError: handleGlobalApiError,
  client: 'admin',
})

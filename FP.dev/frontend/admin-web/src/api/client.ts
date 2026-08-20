import {
  createAntiforgeryTokenProvider,
  createDoSelectClient,
  isApiError,
  resolveApiBaseUrl,
  type ApiError,
  type paths,
} from '@doselect/web-shared/api'
import router from '../router'

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
  })
}

/**
 * No admin login/session UI exists yet in this app (nothing else owns it), so this is
 * scoped to what's actually achievable here: routing every 401/403 to the existing
 * (previously orphaned) /unauthorized and /forbidden HttpStatusPage routes instead of
 * leaving a page stuck on a raw ApiError message. Wired only on the singleton — the
 * generic createApiClient() factory stays free of router side effects for tests.
 */
export function handleGlobalApiError(error: ApiError): void {
  if (!isApiError(error)) {
    return
  }
  if (error.status === 401) {
    void router.push('/unauthorized')
  } else if (error.status === 403) {
    void router.push('/forbidden')
  }
}

export const apiClient = createDoSelectClient<paths>({
  baseUrl: apiBaseUrl,
  getAntiforgeryToken: antiforgeryTokenProvider.getToken,
  onApiError: handleGlobalApiError,
})

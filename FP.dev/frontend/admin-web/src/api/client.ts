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
 * No admin login/session UI exists yet in this app (nothing else owns it). A 401 means the
 * session expired or was never established — the correct destination is `/admin/login?
 * returnUrl=...` (PR #24 review round 3), not `/unauthorized`, which is a permission error and
 * gives the admin no way back in. Since that login page doesn't exist yet, routing 401 anywhere
 * would either 404 or misrepresent an expired session as a forbidden one, so it's intentionally
 * left unhandled here (surfaces as an inline ApiError on whatever page triggered it) until the
 * login page lands — wire the safe-redirect-with-returnUrl then, not before.
 * 403 has a real destination today, so it still routes to the existing (previously orphaned)
 * /forbidden HttpStatusPage route. Wired only on the singleton — the generic createApiClient()
 * factory stays free of router side effects for tests.
 */
export function handleGlobalApiError(error: ApiError): void {
  if (!isApiError(error)) {
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
})

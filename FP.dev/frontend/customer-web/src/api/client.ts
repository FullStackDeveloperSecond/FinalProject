import {
  createAntiforgeryTokenProvider,
  createDoSelectClient,
  resolveApiBaseUrl,
  type paths,
} from '@doselect/web-shared/api'

const browserApiFallback = typeof window === 'undefined'
  ? 'http://localhost:5126'
  : window.location.origin
export const apiBaseUrl = resolveApiBaseUrl(import.meta.env.VITE_API_BASE_URL, browserApiFallback)
const antiforgeryTokenProvider = createAntiforgeryTokenProvider({
  baseUrl: apiBaseUrl,
  client: 'member',
})

export function resetAntiforgeryToken(): void {
  antiforgeryTokenProvider.reset()
}

export function createApiClient<Paths extends object>() {
  return createDoSelectClient<Paths>({
    baseUrl: apiBaseUrl,
    getAntiforgeryToken: antiforgeryTokenProvider.getToken,
    client: 'member',
  })
}

/** Singleton client typed against the shared generated OpenAPI schema (`npm run api:generate` in frontend/shared). */
export const apiClient = createApiClient<paths>()

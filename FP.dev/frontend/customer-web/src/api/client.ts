import {
  createAntiforgeryTokenProvider,
  createDoSelectClient,
  resolveApiBaseUrl,
  type paths,
} from '@doselect/web-shared/api'

export const apiBaseUrl = resolveApiBaseUrl(import.meta.env.VITE_API_BASE_URL)
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
  })
}

/** Singleton client typed against the shared generated OpenAPI schema (`npm run api:generate` in frontend/shared). */
export const apiClient = createApiClient<paths>()

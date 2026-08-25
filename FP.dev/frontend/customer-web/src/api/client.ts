import {
  createAntiforgeryTokenProvider,
  createDoSelectClient,
  resolveApiBaseUrl,
} from '@doselect/web-shared/api'
import type { paths } from '@doselect/web-shared/api'

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

/** Typed client for every Controller-backed endpoint described by the generated OpenAPI schema. */
export const apiClient = createApiClient<paths>()

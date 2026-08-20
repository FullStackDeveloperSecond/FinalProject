import {
  createAntiforgeryTokenProvider,
  createDoSelectClient,
  resolveApiBaseUrl,
} from '@doselect/web-shared/api'
import type { paths } from './generated/schema'

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

export const apiClient = createApiClient<paths>()

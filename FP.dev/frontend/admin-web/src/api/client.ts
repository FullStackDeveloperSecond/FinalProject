import { createDoSelectClient, resolveApiBaseUrl } from '@doselect/web-shared/api'
import type { paths } from './generated/schema'

export const apiBaseUrl = resolveApiBaseUrl(import.meta.env.VITE_API_BASE_URL)

export function createApiClient<Paths extends object>() {
  return createDoSelectClient<Paths>({
    baseUrl: apiBaseUrl,
  })
}

export const apiClient = createApiClient<paths>()

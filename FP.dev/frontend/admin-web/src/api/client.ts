import { createDoSelectClient, resolveApiBaseUrl } from '@doselect/web-shared/api'

export const apiBaseUrl = resolveApiBaseUrl(import.meta.env.VITE_API_BASE_URL)

export function createApiClient<Paths extends object>() {
  return createDoSelectClient<Paths>({
    baseUrl: apiBaseUrl,
  })
}

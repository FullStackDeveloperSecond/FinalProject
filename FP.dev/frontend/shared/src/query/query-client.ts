import { QueryClient } from '@tanstack/vue-query'
import { isApiError } from '../api/errors'

export function createDoSelectQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: shouldRetryQuery,
        staleTime: 30_000,
        refetchOnWindowFocus: false,
      },
      mutations: {
        retry: false,
      },
    },
  })
}

export function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  if (failureCount >= 1) {
    return false
  }

  if (!isApiError(error)) {
    return true
  }

  return error.status === 0 || error.status >= 500
}

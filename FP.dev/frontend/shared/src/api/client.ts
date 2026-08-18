import createClient, { type Client, type Middleware } from 'openapi-fetch'
import { createCorrelationId, isValidCorrelationId } from './correlation-id'
import { createApiError, createNetworkError, type ApiError } from './errors'

const unsafeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

export interface DoSelectClientOptions {
  baseUrl: string
  fetch?: typeof globalThis.fetch
  getAntiforgeryToken?: () => Promise<string | undefined>
  onApiError?: (error: ApiError) => void
}

export function createDoSelectClient<Paths extends object>(
  options: DoSelectClientOptions,
): Client<Paths> {
  const client = createClient<Paths>({
    baseUrl: normalizeApiBaseUrl(options.baseUrl),
    credentials: 'include',
    fetch: options.fetch,
  })

  client.use(createHttpMiddleware(options))
  return client
}

export function resolveApiBaseUrl(
  configuredValue: string | undefined,
  fallback = 'http://localhost:5126',
): string {
  return normalizeApiBaseUrl(configuredValue?.trim() || fallback)
}

function createHttpMiddleware(options: DoSelectClientOptions): Middleware {
  return {
    async onRequest({ request }) {
      const headers = new Headers(request.headers)
      const existingCorrelationId = headers.get('X-Correlation-ID')
      if (!existingCorrelationId || !isValidCorrelationId(existingCorrelationId)) {
        headers.set('X-Correlation-ID', createCorrelationId())
      }

      if (unsafeMethods.has(request.method.toUpperCase()) && options.getAntiforgeryToken) {
        const token = await options.getAntiforgeryToken()
        if (token) {
          headers.set('X-XSRF-TOKEN', token)
        }
      }

      return new Request(request, { headers })
    },
    async onResponse({ response }) {
      if (response.ok) {
        return response
      }

      const error = await createApiError(response)
      options.onApiError?.(error)
      throw error
    },
    onError({ error }) {
      const networkError = error instanceof Error && error.name === 'ApiError'
        ? error as ApiError
        : createNetworkError(error)
      options.onApiError?.(networkError)
      return networkError
    },
  }
}

function normalizeApiBaseUrl(value: string): string {
  let parsed: URL
  try {
    parsed = new URL(value)
  } catch {
    throw new Error('VITE_API_BASE_URL must be an absolute HTTP or HTTPS URL.')
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error('VITE_API_BASE_URL must use HTTP or HTTPS.')
  }

  if (parsed.username || parsed.password || parsed.search || parsed.hash) {
    throw new Error('VITE_API_BASE_URL must not contain credentials, query parameters, or fragments.')
  }

  return parsed.toString().replace(/\/$/, '')
}

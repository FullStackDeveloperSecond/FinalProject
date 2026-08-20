import { createCorrelationId } from './correlation-id'
import { createApiError } from './errors'

export interface AntiforgeryTokenProvider {
  getToken: () => Promise<string>
  reset: () => void
}

export interface AntiforgeryTokenProviderOptions {
  baseUrl: string
  client: 'member' | 'admin'
  fetch?: typeof globalThis.fetch
}

interface AntiforgeryTokenResponse {
  requestToken: string
}

export function createAntiforgeryTokenProvider(
  options: AntiforgeryTokenProviderOptions,
): AntiforgeryTokenProvider {
  const fetchImplementation = options.fetch ?? globalThis.fetch
  let cachedToken: string | undefined
  let pendingRequest: Promise<string> | undefined
  let generation = 0

  async function requestToken(requestGeneration: number): Promise<string> {
    const response = await fetchImplementation(
      `${options.baseUrl}/api/v1/security/antiforgery-token`,
      {
        credentials: 'include',
        headers: {
          'X-Correlation-ID': createCorrelationId(),
          'X-DoSelect-Client': options.client,
        },
      },
    )

    if (!response.ok) {
      throw await createApiError(response)
    }

    const body = await response.json() as Partial<AntiforgeryTokenResponse>
    if (typeof body.requestToken !== 'string' || body.requestToken.length === 0) {
      throw new Error('The API returned an invalid antiforgery token response.')
    }

    if (requestGeneration !== generation) {
      throw new Error('The antiforgery token request was reset before it completed.')
    }

    cachedToken = body.requestToken
    return cachedToken
  }

  return {
    getToken() {
      if (cachedToken) {
        return Promise.resolve(cachedToken)
      }

      if (!pendingRequest) {
        const request = requestToken(generation).finally(() => {
          if (pendingRequest === request) {
            pendingRequest = undefined
          }
        })
        pendingRequest = request
      }

      return pendingRequest
    },
    reset() {
      generation += 1
      cachedToken = undefined
      pendingRequest = undefined
    },
  }
}

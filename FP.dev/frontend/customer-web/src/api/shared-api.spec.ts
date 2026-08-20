import { ApiError, createAntiforgeryTokenProvider, createApiError, createCorrelationId, createDoSelectClient, isValidCorrelationId, resolveApiBaseUrl } from '@doselect/web-shared/api'
import { shouldRetryQuery } from '@doselect/web-shared/query'
import { describe, expect, it, vi } from 'vitest'

interface TestPaths {
  '/resource': {
    get: {
      responses: {
        200: {
          content: {
            'application/json': { ok: boolean }
          }
        }
        403: {
          content: {
            'application/problem+json': { code: string }
          }
        }
      }
    }
    post: {
      responses: {
        200: {
          content: {
            'application/json': { ok: boolean }
          }
        }
      }
    }
  }
}

describe('shared API foundation', () => {
  it('normalizes the configured API base URL', () => {
    expect(resolveApiBaseUrl('http://localhost:5126/')).toBe('http://localhost:5126')
    expect(resolveApiBaseUrl(undefined)).toBe('http://localhost:5126')
    expect(() => resolveApiBaseUrl('file:///tmp/api')).toThrow(/HTTP or HTTPS/)
  })

  it('creates an API-compatible correlation ID', () => {
    const correlationId = createCorrelationId()

    expect(correlationId).toHaveLength(32)
    expect(isValidCorrelationId(correlationId)).toBe(true)
    expect(isValidCorrelationId('含有非 ASCII 字元')).toBe(false)
  })

  it('parses Problem Details and tracking identifiers', async () => {
    const response = new Response(JSON.stringify({
      type: 'urn:doselect:problem:validation_failed',
      title: 'Validation failed',
      status: 400,
      code: 'validation_failed',
      traceId: 'trace-123',
      correlationId: 'request-123',
      errors: {
        email: ['Email 格式不正確'],
      },
    }), {
      status: 400,
      headers: {
        'Content-Type': 'application/problem+json; charset=utf-8',
      },
    })

    const error = await createApiError(response)

    expect(error).toBeInstanceOf(ApiError)
    expect(error.code).toBe('validation_failed')
    expect(error.correlationId).toBe('request-123')
    expect(error.traceId).toBe('trace-123')
    expect(error.fieldErrors?.email).toEqual(['Email 格式不正確'])
  })

  it('only retries a query once for network and server failures', () => {
    expect(shouldRetryQuery(0, new Error('network'))).toBe(true)
    expect(shouldRetryQuery(1, new Error('network'))).toBe(false)
    expect(shouldRetryQuery(0, new ApiError('Unavailable', {
      status: 503,
      code: 'service_unavailable',
    }))).toBe(true)
    expect(shouldRetryQuery(0, new ApiError('Forbidden', {
      status: 403,
      code: 'authorization_forbidden',
    }))).toBe(false)
  })

  it('sends credentials, correlation ID, and CSRF for unsafe requests', async () => {
    const requests: Request[] = []
    const fetchStub: typeof fetch = async (input, init) => {
      const request = new Request(input, init)
      requests.push(request)
      return Response.json({ ok: true })
    }
    const client = createDoSelectClient<TestPaths>({
      baseUrl: 'http://localhost:5126',
      fetch: fetchStub,
      getAntiforgeryToken: async () => 'csrf-token',
    })

    await client.GET('/resource')
    await client.POST('/resource')

    expect(requests).toHaveLength(2)
    expect(requests[0]?.credentials).toBe('include')
    expect(isValidCorrelationId(requests[0]?.headers.get('X-Correlation-ID') ?? '')).toBe(true)
    expect(requests[0]?.headers.has('X-XSRF-TOKEN')).toBe(false)
    expect(requests[1]?.headers.get('X-XSRF-TOKEN')).toBe('csrf-token')
  })

  it('raises a typed API error and reports it once', async () => {
    const onApiError = vi.fn()
    const fetchStub: typeof fetch = async () => new Response(JSON.stringify({
      title: 'Forbidden',
      status: 403,
      code: 'authorization_forbidden',
      correlationId: 'request-403',
    }), {
      status: 403,
      headers: {
        'Content-Type': 'application/problem+json',
      },
    })
    const client = createDoSelectClient<TestPaths>({
      baseUrl: 'http://localhost:5126',
      fetch: fetchStub,
      onApiError,
    })

    await expect(client.GET('/resource')).rejects.toMatchObject({
      status: 403,
      code: 'authorization_forbidden',
      correlationId: 'request-403',
    })
    expect(onApiError).toHaveBeenCalledTimes(1)
  })

  it('keeps one antiforgery token in memory until the session changes', async () => {
    const fetchStub = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(Response.json({ requestToken: 'first-token' }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'second-token' }))
    const provider = createAntiforgeryTokenProvider({
      baseUrl: 'http://localhost:5126',
      client: 'member',
      fetch: fetchStub,
    })

    await expect(Promise.all([provider.getToken(), provider.getToken()]))
      .resolves.toEqual(['first-token', 'first-token'])
    expect(fetchStub).toHaveBeenCalledTimes(1)

    provider.reset()

    await expect(provider.getToken()).resolves.toBe('second-token')
    expect(fetchStub).toHaveBeenCalledTimes(2)
    expect(fetchStub.mock.calls[0]?.[1]).toMatchObject({ credentials: 'include' })
    expect(new Headers(fetchStub.mock.calls[0]?.[1]?.headers).get('X-DoSelect-Client'))
      .toBe('member')
  })

  it('does not reuse a token request that completes after a session reset', async () => {
    let resolveStaleRequest!: (response: Response) => void
    const staleResponse = new Promise<Response>((resolve) => {
      resolveStaleRequest = resolve
    })
    const fetchStub = vi.fn<typeof fetch>()
      .mockReturnValueOnce(staleResponse)
      .mockResolvedValueOnce(Response.json({ requestToken: 'fresh-token' }))
    const provider = createAntiforgeryTokenProvider({
      baseUrl: 'http://localhost:5126',
      client: 'member',
      fetch: fetchStub,
    })

    const staleToken = provider.getToken()
    provider.reset()
    const freshToken = provider.getToken()
    resolveStaleRequest(Response.json({ requestToken: 'stale-token' }))

    await expect(staleToken).rejects.toThrow(/reset/)
    await expect(freshToken).resolves.toBe('fresh-token')
    await expect(provider.getToken()).resolves.toBe('fresh-token')
    expect(fetchStub).toHaveBeenCalledTimes(2)
  })
})

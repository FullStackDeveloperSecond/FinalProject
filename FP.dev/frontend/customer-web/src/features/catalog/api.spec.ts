import { afterEach, describe, expect, it, vi } from 'vitest'

/**
 * PR #24 review round 2: the previous test for this only mocked `apiClient.GET` itself, which
 * proved the function *received* the right object but never exercised the real querySerializer
 * — exactly the layer that was actually broken (openapi-fetch's default serializer throws on an
 * array of objects). This drives the real client (real openapi-fetch, real querySerializer) and
 * only mocks the network boundary (`fetch`), so it fails the same way production would if the
 * serializer regresses.
 */
describe('searchProducts', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.resetModules()
  })

  it('serializes a Specs filter into ASP.NET Core-bindable indexed query keys', async () => {
    const mockFetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', mockFetch)

    const { searchProducts } = await import('./api')
    await searchProducts({
      specs: [{ semanticKey: 'CPU_SOCKET', operator: 'in', values: ['AM5', 'LGA1700'] }],
      pageNumber: 1,
      pageSize: 20,
    })

    expect(mockFetch).toHaveBeenCalledTimes(1)
    const requestUrl = (mockFetch.mock.calls[0][0] as Request).url
    const searchParams = new URL(requestUrl).searchParams

    expect(searchParams.get('Specs[0].SemanticKey')).toBe('CPU_SOCKET')
    expect(searchParams.get('Specs[0].Operator')).toBe('in')
    expect(searchParams.get('Specs[0].Values[0]')).toBe('AM5')
    expect(searchParams.get('Specs[0].Values[1]')).toBe('LGA1700')
  })

  it('still serializes plain params normally when there is no Specs filter', async () => {
    const mockFetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', mockFetch)

    const { searchProducts } = await import('./api')
    await searchProducts({ q: 'gpu', minPrice: 1000, maxPrice: 5000, pageNumber: 2, pageSize: 20 })

    const requestUrl = (mockFetch.mock.calls[0][0] as Request).url
    const searchParams = new URL(requestUrl).searchParams

    expect(searchParams.get('Q')).toBe('gpu')
    expect(searchParams.get('MinPrice')).toBe('1000')
    expect(searchParams.get('MaxPrice')).toBe('5000')
    expect(searchParams.get('PageNumber')).toBe('2')
    expect(searchParams.has('Specs[0].SemanticKey')).toBe(false)
  })
})

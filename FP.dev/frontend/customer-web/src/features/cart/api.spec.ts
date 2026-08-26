import { createDoSelectClient } from '@doselect/web-shared/api'
import { describe, expect, it, vi } from 'vitest'
import type { CartApiPaths, CartDto } from './types'

const emptyCart: CartDto = {
  publicId: 'cart-1',
  items: [],
  coupon: null,
  amounts: {
    subtotal: 0,
    itemDiscount: 0,
    couponDiscount: 0,
    shippingEstimate: null,
    assemblyFee: 0,
    totalEstimate: 0,
    currency: 'TWD',
  },
  warnings: [],
  rowVersion: 'AAAA',
}

describe('cart api wire format', () => {
  it('attaches the guest cart key header on GET /cart when provided', async () => {
    const requests: Request[] = []
    const fetchStub: typeof fetch = async (input, init) => {
      requests.push(new Request(input, init))
      return Response.json(emptyCart)
    }
    const client = createDoSelectClient<CartApiPaths>({ baseUrl: 'http://localhost:5126', fetch: fetchStub })

    await client.GET('/api/v1/cart', { headers: { 'X-DoSelect-Guest-Cart-Key': 'guest-abc' } })

    expect(requests[0]?.headers.get('X-DoSelect-Guest-Cart-Key')).toBe('guest-abc')
  })

  it('sends the add-item request body and antiforgery token', async () => {
    const requests: Request[] = []
    const fetchStub: typeof fetch = vi.fn(async (input, init) => {
      const request = new Request(input, init)
      requests.push(request)
      return Response.json(emptyCart)
    })
    const client = createDoSelectClient<CartApiPaths>({
      baseUrl: 'http://localhost:5126',
      fetch: fetchStub,
      getAntiforgeryToken: async () => 'csrf-token',
    })

    await client.POST('/api/v1/cart/items', {
      body: { skuPublicId: 'sku-1', quantity: 2, cartRowVersion: null },
      headers: { 'X-DoSelect-Guest-Cart-Key': 'guest-abc' },
    })

    const request = requests[0]!
    expect(request.headers.get('X-XSRF-TOKEN')).toBe('csrf-token')
    expect(request.headers.get('X-DoSelect-Guest-Cart-Key')).toBe('guest-abc')
    await expect(request.clone().json()).resolves.toEqual({
      skuPublicId: 'sku-1',
      quantity: 2,
      cartRowVersion: null,
    })
  })

  it('sends the merge request without a guest header (member-only endpoint)', async () => {
    const requests: Request[] = []
    const fetchStub: typeof fetch = async (input, init) => {
      requests.push(new Request(input, init))
      return Response.json({ cart: emptyCart, conflicts: [] })
    }
    const client = createDoSelectClient<CartApiPaths>({ baseUrl: 'http://localhost:5126', fetch: fetchStub })

    await client.POST('/api/v1/cart/actions/merge', {
      body: { guestCartKey: 'guest-abc', strategy: 'mergeAndReportConflicts', idempotencyKey: 'idem-1' },
    })

    expect(requests[0]?.headers.has('X-DoSelect-Guest-Cart-Key')).toBe(false)
    await expect(requests[0]!.clone().json()).resolves.toEqual({
      guestCartKey: 'guest-abc',
      strategy: 'mergeAndReportConflicts',
      idempotencyKey: 'idem-1',
    })
  })
})

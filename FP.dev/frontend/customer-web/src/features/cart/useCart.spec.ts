import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSessionStore } from '../../stores/session'
import type { CartDto } from './types'

const mockGetCart = vi.fn<() => Promise<CartDto>>()
const mockAddCartItem = vi.fn<() => Promise<CartDto>>()
const mockRemoveCartItem = vi.fn<() => Promise<CartDto>>()

vi.mock('./api', () => ({
  getCart: (...args: unknown[]) => mockGetCart(...(args as [])),
  addCartItem: (...args: unknown[]) => mockAddCartItem(...(args as [])),
  updateCartItemQuantity: vi.fn(),
  removeCartItem: (...args: unknown[]) => mockRemoveCartItem(...(args as [])),
  revalidateCart: vi.fn(),
  mergeCartOnLogin: vi.fn(),
}))

vi.mock('./guestCartKey', () => ({
  getOrCreateGuestCartKey: () => 'guest-test-key',
  clearGuestCartKey: vi.fn(),
}))

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

function withQueryClient(setup: () => unknown, initialStatus: 'loading' | 'anonymous' = 'anonymous') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const pinia = createPinia()
  setActivePinia(pinia)
  useSessionStore().status = initialStatus
  const TestComponent = defineComponent({ setup: () => { setup(); return () => h('div') } })
  const wrapper = mount(TestComponent, { global: { plugins: [[VueQueryPlugin, { queryClient }], pinia] } })
  return { wrapper, queryClient }
}

beforeEach(() => {
  mockGetCart.mockReset()
  mockAddCartItem.mockReset()
  mockRemoveCartItem.mockReset()
})

describe('useCart', () => {
  it('fetches the cart using the guest key', async () => {
    mockGetCart.mockResolvedValue(emptyCart)
    const { useCart } = await import('./useCart')
    let query: ReturnType<typeof useCart> | undefined

    withQueryClient(() => { query = useCart() })
    await vi.waitFor(() => expect(query!.isSuccess.value).toBe(true))

    expect(mockGetCart).toHaveBeenCalledWith('guest-test-key')
    expect(query!.data.value).toEqual(emptyCart)
  })

  /**
   * 組長 PR #29 round-4 review, P1 (verification ask): while session status is still 'loading',
   * the real identity (guest vs. an already-authenticated member whose cookie just hasn't been
   * checked yet) isn't known. Fetching under the guest key regardless would risk exactly the
   * flash this whole identity-scoping design exists to prevent — and if that guest-keyed fetch
   * somehow returned the *member's* cart (identity resolved server-side, key resolved
   * client-side), it would sit cached under the wrong key indefinitely. useCart() must not fetch
   * at all until session status leaves 'loading'.
   */
  it('does not fetch under the guest key while session status is still loading', async () => {
    const { useCart } = await import('./useCart')
    let query: ReturnType<typeof useCart> | undefined
    const { queryClient } = withQueryClient(() => { query = useCart() }, 'loading')
    const sessionStore = useSessionStore()

    // Give any (incorrect) eager fetch a chance to have fired.
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(mockGetCart).not.toHaveBeenCalled()
    expect(query!.isPending.value).toBe(true)

    mockGetCart.mockResolvedValueOnce({ ...emptyCart, publicId: 'cart-member' })
    sessionStore.status = 'authenticated'
    sessionStore.user = { publicId: 'member-1', displayName: '測試會員', emailMasked: 'm***@example.com', emailVerified: true, locale: 'zh-TW' }

    await vi.waitFor(() => expect(query!.isSuccess.value).toBe(true))
    expect(mockGetCart).toHaveBeenCalledTimes(1)
    expect(queryClient.getQueryData(['cart', 'guest', 'guest-test-key'])).toBeUndefined()
    expect(queryClient.getQueryData(['cart', 'member', 'member-1'])).toEqual({ ...emptyCart, publicId: 'cart-member' })
  })

  /**
   * 組長 PR #29 round-4 review, P1: the identity-snapshot fix (see the mutation test below) stops
   * a stale response from being written into the *new* identity's cache, but on its own leaves
   * the *old* identity's cache entry sitting around indefinitely after login/logout — a logged-out
   * member's cart data would otherwise stay reachable in memory. useCart() must drop the previous
   * identity's cached data the moment the identity key itself changes.
   */
  it('removes the previous identity\'s cached cart once the identity key changes', async () => {
    mockGetCart.mockResolvedValueOnce(emptyCart)
    const { useCart } = await import('./useCart')
    let query: ReturnType<typeof useCart> | undefined
    const { queryClient } = withQueryClient(() => { query = useCart() })
    const sessionStore = useSessionStore()
    await vi.waitFor(() => expect(query!.isSuccess.value).toBe(true))
    expect(queryClient.getQueryData(['cart', 'guest', 'guest-test-key'])).toEqual(emptyCart)

    mockGetCart.mockResolvedValueOnce({ ...emptyCart, publicId: 'cart-member' })
    sessionStore.status = 'authenticated'
    sessionStore.user = { publicId: 'member-1', displayName: '測試會員', emailMasked: 'm***@example.com', emailVerified: true, locale: 'zh-TW' }
    await vi.waitFor(() => expect(queryClient.getQueryData(['cart', 'member', 'member-1']))
      .toEqual({ ...emptyCart, publicId: 'cart-member' }))

    expect(queryClient.getQueryData(['cart', 'guest', 'guest-test-key'])).toBeUndefined()
  })

  it('updates the cache with the mutation result on add', async () => {
    const cartWithItem: CartDto = { ...emptyCart, items: [{
      publicId: 'item-1',
      skuPublicId: 'sku-1',
      skuCode: 'SKU-1',
      name: '測試商品',
      quantity: 1,
      unitPrice: 100,
      lineTotal: 100,
      availability: 'available',
      priceChanged: false,
      maxPurchasableQuantity: 99,
      assemblyGroupKey: null,
      rowVersion: 'BBBB',
    }] }
    mockAddCartItem.mockResolvedValue(cartWithItem)
    const { useAddCartItem, useCart } = await import('./useCart')
    let addMutation: ReturnType<typeof useAddCartItem> | undefined
    let cartQuery: ReturnType<typeof useCart> | undefined

    withQueryClient(() => {
      addMutation = useAddCartItem()
      cartQuery = useCart()
    })

    await addMutation!.mutateAsync({ skuPublicId: 'sku-1', quantity: 1, cartRowVersion: null })

    expect(mockAddCartItem).toHaveBeenCalledWith('sku-1', 1, null, 'guest-test-key')
    expect(cartQuery!.data.value).toEqual(cartWithItem)
  })

  /**
   * 組長 PR #29 round-4 review, P1: onSuccess used to read the *reactive current* identity key
   * (`identityKey.value`) at the moment the response arrived, not the identity that was active
   * when the request was actually sent — if identity changed while the request was still in
   * flight, the response landed in the *new* identity's cache instead of the guest's, a real
   * cross-identity write. The fix snapshots the identity in `onMutate` (synchronous, runs before
   * `mutationFn`) and writes back to that snapshot regardless of what identity is current by the
   * time the response resolves.
   */
  it('writes a mutation response back to the identity that was active when the request started, not whatever is current when it resolves', async () => {
    let resolveAdd!: (cart: CartDto) => void
    mockAddCartItem.mockImplementationOnce(() => new Promise((resolve) => { resolveAdd = resolve }))
    const cartAfterAdd: CartDto = { ...emptyCart, publicId: 'cart-guest' }

    const { useAddCartItem } = await import('./useCart')
    let addMutation: ReturnType<typeof useAddCartItem> | undefined
    const { queryClient } = withQueryClient(() => {
      addMutation = useAddCartItem()
    })

    // Sent while the shopper is still a guest — this is the identity the request belongs to.
    const pending = addMutation!.mutateAsync({ skuPublicId: 'sku-1', quantity: 1, cartRowVersion: null })
    await vi.waitFor(() => expect(mockAddCartItem).toHaveBeenCalled())

    // The shopper finishes logging in before the add-to-cart response arrives.
    const sessionStore = useSessionStore()
    sessionStore.status = 'authenticated'
    sessionStore.user = { publicId: 'member-1', displayName: '測試會員', emailMasked: 'm***@example.com', emailVerified: true, locale: 'zh-TW' }

    resolveAdd(cartAfterAdd)
    await pending

    expect(queryClient.getQueryData(['cart', 'guest', 'guest-test-key'])).toEqual(cartAfterAdd)
    expect(queryClient.getQueryData(['cart', 'member', 'member-1'])).toBeUndefined()
  })
})

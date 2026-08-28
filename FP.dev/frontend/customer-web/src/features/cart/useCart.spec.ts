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

function withQueryClient(
  setup: () => unknown,
  initialStatus: 'loading' | 'anonymous' | 'error' = 'anonymous',
  seed?: (queryClient: QueryClient) => void,
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seed?.(queryClient)
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
   * 組長 PR #29 round-5 review, P2: TanStack's default mount-refetch used to fire a second,
   * implicit GET whenever the query already had cached data on mount (e.g. revisiting /cart after
   * the shared QueryClient's 30s staleTime expired) — racing CartPage.vue's own explicit
   * `revalidate` call and able to silently clobber whichever result landed second. Every path that
   * can make the cart stale already funnels through an explicit revalidate (initial mount, and
   * every mutation's onSuccess in this file), so the implicit refetch never did useful work; it
   * only ever raced. `refetchOnMount: false` removes it at the source.
   */
  it('does not refetch on mount when the query already has cached data (refetchOnMount: false)', async () => {
    const { useCart } = await import('./useCart')
    let query: ReturnType<typeof useCart> | undefined
    withQueryClient(
      () => { query = useCart() },
      'anonymous',
      (queryClient) => queryClient.setQueryData(['cart', 'guest', 'guest-test-key'], emptyCart),
    )

    expect(query!.isPending.value).toBe(false)
    expect(query!.data.value).toEqual(emptyCart)

    // Give any (incorrect) background refetch a chance to have fired.
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(mockGetCart).not.toHaveBeenCalled()
  })

  /**
   * 組長 PR #29 round-4 review, P1: the identity-snapshot fix (see the mutation test below) stops
   * a stale response from being written into the *new* identity's cache, but on its own leaves
   * the *old* identity's cache entry sitting around indefinitely after login/logout — a logged-out
   * member's cart data would otherwise stay reachable in memory.
   *
   * 組長 PR #29 round-6 review, P1 (point 3): this cleanup used to live inside useCart() itself,
   * so it only ran while CartPage happened to be mounted. It's now a separate composable
   * (useCartIdentityCacheCleanup), called once from App.vue instead — this test calls both
   * together, mirroring how the real app always has App.vue's cleanup active alongside whatever
   * page happens to call useCart().
   */
  it('removes the previous identity\'s cached cart once the identity key changes', async () => {
    mockGetCart.mockResolvedValueOnce(emptyCart)
    const { useCart, useCartIdentityCacheCleanup } = await import('./useCart')
    let query: ReturnType<typeof useCart> | undefined
    const { queryClient } = withQueryClient(() => {
      query = useCart()
      useCartIdentityCacheCleanup()
    })
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

  /**
   * 組長 PR #29 round-6 review, P1 (point 3): the identity-switch cache cleanup must not depend on
   * CartPage (or anything else that calls useCart()) happening to be mounted — a shopper who logs
   * out while browsing ProductDetailPage, say, never mounts useCart() at all, but the previous
   * identity's cart cache must still be evicted. Proven here by never calling useCart() in this
   * test — only useCartIdentityCacheCleanup(), the composable App.vue now calls unconditionally.
   */
  it('useCartIdentityCacheCleanup evicts the previous identity\'s cache even when useCart() itself was never called', async () => {
    const { useCartIdentityCacheCleanup } = await import('./useCart')
    const { queryClient } = withQueryClient(() => { useCartIdentityCacheCleanup() })
    queryClient.setQueryData(['cart', 'guest', 'guest-test-key'], emptyCart)

    const sessionStore = useSessionStore()
    sessionStore.status = 'authenticated'
    sessionStore.user = { publicId: 'member-1', displayName: '測試會員', emailMasked: 'm***@example.com', emailVerified: true, locale: 'zh-TW' }
    await vi.waitFor(() => expect(queryClient.getQueryData(['cart', 'guest', 'guest-test-key'])).toBeUndefined())
  })

  /**
   * 組長 PR #29 round-6 review, P1: a member Cookie the backend already recognizes, but a
   * frontend session refresh still in flight, used to still let a mutation dispatch — its
   * identity snapshot treated "status is 'loading'" the same as guest, so the request went out
   * attributed to the guest cache key while the backend actually mutated the member's real cart.
   * `onMutate` now throws before `mutationFn` ever runs (see snapshotCartMutationIdentity's own
   * remarks on why `onMutate` specifically, not a component-level check, is the real gate) —
   * proven here by asserting the network call itself (mockAddCartItem) never happens.
   */
  it('rejects immediately, without ever calling the network layer, when session status is still \'loading\'', async () => {
    const { useAddCartItem } = await import('./useCart')
    let addMutation: ReturnType<typeof useAddCartItem> | undefined
    withQueryClient(() => { addMutation = useAddCartItem() }, 'loading')

    await expect(addMutation!.mutateAsync({ skuPublicId: 'sku-1', quantity: 1, cartRowVersion: null }))
      .rejects.toThrow('Cart identity is not resolved yet')
    expect(mockAddCartItem).not.toHaveBeenCalled()
  })

  /**
   * 組長 PR #29 round 7 review, P1: a *failed* session refresh (network/5xx) used to resolve
   * `status` all the way to a confirmed-looking 'anonymous' — indistinguishable from a genuine
   * guest to this same identity gate. If the browser still held a valid member Cookie, the mutation
   * would have gone out attributed to the *guest* cache key while the backend actually mutated the
   * real member cart, landing that member's cart data in the guest-visible cache. `status: 'error'`
   * (session.ts) must be refused by this same gate, not just 'loading'.
   */
  it('rejects immediately, without ever calling the network layer, when session status is \'error\' (a failed refresh)', async () => {
    const { useAddCartItem } = await import('./useCart')
    let addMutation: ReturnType<typeof useAddCartItem> | undefined
    withQueryClient(() => { addMutation = useAddCartItem() }, 'error')

    await expect(addMutation!.mutateAsync({ skuPublicId: 'sku-1', quantity: 1, cartRowVersion: null }))
      .rejects.toThrow('Cart identity is not resolved yet')
    expect(mockAddCartItem).not.toHaveBeenCalled()
  })

  it('does not fetch under the guest key when session status is \'error\' (a failed refresh)', async () => {
    const { useCart } = await import('./useCart')
    let query: ReturnType<typeof useCart> | undefined
    withQueryClient(() => { query = useCart() }, 'error')

    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(mockGetCart).not.toHaveBeenCalled()
    expect(query!.isPending.value).toBe(true)
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

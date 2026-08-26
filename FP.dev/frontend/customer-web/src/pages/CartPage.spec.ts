import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import { useSessionStore } from '../stores/session'
import type { CurrentUserDto } from '../features/auth/api'
import type { CartDto, CartValidationDto } from '../features/cart/types'

const testMember: CurrentUserDto = {
  publicId: 'member-1',
  displayName: '測試會員',
  emailMasked: 'm***@example.com',
  emailVerified: true,
  locale: 'zh-TW',
}

const mockGetCart = vi.fn<() => Promise<CartDto>>()
const mockRevalidateCart = vi.fn<() => Promise<CartValidationDto>>()
const mockUpdateCartItemQuantity = vi.fn<() => Promise<CartDto>>()
const mockRemoveCartItem = vi.fn<() => Promise<CartDto>>()

vi.mock('../features/cart/api', () => ({
  getCart: () => mockGetCart(),
  addCartItem: vi.fn(),
  updateCartItemQuantity: () => mockUpdateCartItemQuantity(),
  removeCartItem: () => mockRemoveCartItem(),
  revalidateCart: () => mockRevalidateCart(),
  mergeCartOnLogin: vi.fn(),
}))

vi.mock('../features/cart/guestCartKey', () => ({
  getOrCreateGuestCartKey: () => 'guest-test-key',
  clearGuestCartKey: vi.fn(),
}))

const oneItemCart: CartDto = {
  publicId: 'cart-1',
  items: [{
    publicId: 'item-1',
    skuPublicId: 'sku-1',
    skuCode: 'SKU-1',
    name: 'RTX 4070',
    quantity: 2,
    unitPrice: 18000,
    lineTotal: 36000,
    availability: 'available',
    priceChanged: false,
    maxPurchasableQuantity: 99,
    assemblyGroupKey: null,
    rowVersion: 'AAAA',
  }],
  coupon: null,
  amounts: {
    subtotal: 36000,
    itemDiscount: 0,
    couponDiscount: 0,
    shippingEstimate: null,
    assemblyFee: 0,
    totalEstimate: 36000,
    currency: 'TWD',
  },
  warnings: [],
  rowVersion: 'AAAA',
}

async function mountCartPage(options: { authenticated?: boolean, queryClient?: QueryClient } = {}) {
  const { default: CartPage } = await import('./CartPage.vue')
  const queryClient = options.queryClient ?? new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] })

  const pinia = createPinia()
  setActivePinia(pinia)
  const sessionStore = useSessionStore()
  if (options.authenticated) {
    sessionStore.status = 'authenticated'
    sessionStore.user = testMember
  } else {
    sessionStore.status = 'anonymous'
  }

  return mount(CartPage, {
    global: {
      plugins: [[VueQueryPlugin, { queryClient }], pinia, router],
    },
  })
}

beforeEach(() => {
  mockGetCart.mockReset()
  mockRevalidateCart.mockReset()
  mockUpdateCartItemQuantity.mockReset()
  mockRemoveCartItem.mockReset()
})

describe('CartPage', () => {
  it('shows the loading state before the cart resolves', async () => {
    mockGetCart.mockReturnValue(new Promise(() => {}))
    mockRevalidateCart.mockReturnValue(new Promise(() => {}))

    const wrapper = await mountCartPage()

    expect(wrapper.text()).toContain('購物車載入中')
  })

  it('shows the empty state when the cart has no items', async () => {
    mockGetCart.mockResolvedValue({ ...oneItemCart, items: [] })
    mockRevalidateCart.mockResolvedValue({
      cart: { ...oneItemCart, items: [] },
      isCheckoutReady: true,
      issues: [],
      validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('購物車是空的'))
  })

  it('renders a line item and disables checkout until revalidation confirms it is ready', async () => {
    mockGetCart.mockResolvedValue(oneItemCart)
    mockRevalidateCart.mockResolvedValue({
      cart: oneItemCart,
      isCheckoutReady: false,
      issues: [{ itemPublicId: 'item-1', code: 'cart_item_requires_attention', severity: 'warning', availableActions: ['remove'] }],
      validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))

    expect(mockRevalidateCart).toHaveBeenCalled()
    const checkoutButton = wrapper.find('.cart-page__checkout')
    await vi.waitFor(() => expect(checkoutButton.attributes('disabled')).toBeDefined())
    // PR #29 review, item 5: a bare issue code isn't actionable — the page must show the mapped
    // human message and the available actions, not "cart_item_requires_attention" as-is.
    expect(wrapper.text()).not.toContain('cart_item_requires_attention')
    expect(wrapper.text()).toContain('庫存不足')
    expect(wrapper.text()).toContain('移除')
    // PR #29 review round 2: cart.amounts.totalEstimate, not the old flat cart.totalEstimate —
    // a test fixture that happened to agree with the (wrong) component code never caught this;
    // asserting the actual rendered total is what proves the real nested shape renders correctly.
    expect(wrapper.text()).toContain('NT$36,000')
  })

  /** PR #29 review round 2: a failed revalidate used to be silently swallowed — no error shown, no way to retry. */
  it('shows a retryable error when revalidate fails, and clears it on a successful retry', async () => {
    mockGetCart.mockResolvedValue(oneItemCart)
    mockRevalidateCart.mockRejectedValueOnce(new Error('network error'))

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('購物車檢查失敗'))

    mockRevalidateCart.mockResolvedValueOnce({
      cart: oneItemCart,
      isCheckoutReady: true,
      issues: [],
      validatedAtUtc: new Date().toISOString(),
    })
    const retryButton = wrapper.findAll('button').find((button) => button.text() === '重試')
    await retryButton!.trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).not.toContain('購物車檢查失敗'))
  })

  /**
   * PR #29 review round 2: overlapping revalidate calls used to be possible (onMounted's call
   * and a mutation's onSuccess call could both be in flight at once), risking a stale response
   * overwriting a newer one's issues/checkout-gate state. A trigger that arrives while one is
   * already running must coalesce into a single follow-up call, not fire a second one.
   */
  it('coalesces a revalidate trigger that arrives while one is already in flight into a single follow-up call', async () => {
    mockGetCart.mockResolvedValue(oneItemCart)
    let resolveFirstRevalidate!: (value: CartValidationDto) => void
    mockRevalidateCart.mockImplementationOnce(() => new Promise((resolve) => { resolveFirstRevalidate = resolve }))

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))
    await vi.waitFor(() => expect(mockRevalidateCart).toHaveBeenCalledTimes(1))

    // Item mutation controls must stay disabled for the duration of the in-flight revalidate.
    const checkoutButton = wrapper.find('.cart-page__checkout')
    expect(checkoutButton.attributes('disabled')).toBeDefined()
    const toolbarButton = wrapper.find('.cart-page__toolbar button')
    expect(toolbarButton.attributes('disabled')).toBeDefined()

    // A second trigger while the first is still pending must not start a second network call
    // (the toolbar button is disabled precisely so a real click can't do this either — drive
    // runRevalidate directly to prove the coalescing guard itself, not just the disabled attribute).
    await (wrapper.vm as unknown as { runRevalidate: () => Promise<void> }).runRevalidate()
    expect(mockRevalidateCart).toHaveBeenCalledTimes(1)

    mockRevalidateCart.mockResolvedValueOnce({
      cart: oneItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })
    resolveFirstRevalidate({ cart: oneItemCart, isCheckoutReady: false, issues: [], validatedAtUtc: new Date().toISOString() })

    // ...but exactly one coalesced follow-up call must still happen once the first resolves.
    await vi.waitFor(() => expect(mockRevalidateCart).toHaveBeenCalledTimes(2))
  })

  /**
   * 組長 PR #29 review: an assembled build must render as one group with its SKUs kept visible
   * underneath, and must never mix with another build or a plain SKU.
   */
  it('groups assembled build items together and keeps a plain SKU on its own', async () => {
    const cart: CartDto = {
      ...oneItemCart,
      items: [
        { ...oneItemCart.items[0], publicId: 'a1', skuCode: 'CPU-1', assemblyGroupKey: 'build-1' },
        { ...oneItemCart.items[0], publicId: 'a2', skuCode: 'GPU-1', assemblyGroupKey: 'build-1' },
        { ...oneItemCart.items[0], publicId: 'b1', skuCode: 'CPU-2', assemblyGroupKey: 'build-2' },
        { ...oneItemCart.items[0], publicId: 'p1', skuCode: 'MOUSE-1', assemblyGroupKey: null },
      ],
    }
    mockGetCart.mockResolvedValue(cart)
    mockRevalidateCart.mockResolvedValue({ cart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString() })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU-1'))

    const groups = wrapper.findAll('.cart-page__assembly-group')
    expect(groups).toHaveLength(2)
    expect(groups[0].text()).toContain('CPU-1')
    expect(groups[0].text()).toContain('GPU-1')
    expect(groups[0].text()).not.toContain('CPU-2')
    expect(groups[1].text()).toContain('CPU-2')
    expect(groups.every((group) => !group.text().includes('MOUSE-1'))).toBe(true)
    expect(wrapper.find('.cart-page__items').text()).toContain('MOUSE-1')
  })

  /**
   * 組長 PR #29 review: item mutations only handled onSuccess — a failure left no feedback beyond
   * the button re-enabling, and a concurrency_conflict specifically means the shopper was acting
   * on stale data, so the cart must be refetched before they retry.
   */
  it('shows a retryable error and refetches the cart when removing an item hits a concurrency conflict', async () => {
    mockGetCart.mockResolvedValueOnce(oneItemCart)
    mockRevalidateCart.mockResolvedValue({
      cart: oneItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))

    mockRemoveCartItem.mockRejectedValueOnce(new ApiError('stale', { status: 409, code: 'concurrency_conflict' }))
    mockGetCart.mockResolvedValueOnce({ ...oneItemCart, rowVersion: 'BBBB' })

    await wrapper.find('.cart-line-item__remove').trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('此品項已被更新'))
    expect(mockGetCart).toHaveBeenCalledTimes(2)
  })

  /**
   * 組長 PR #29 review round 3, P2: the previous fix fired `void refetch()` and considered
   * recovery done — but the mutation's own `isPending` (part of `isBusy`) had already gone false
   * by the time onError ran, so a shopper could click another control using the same stale
   * RowVersion before the refetch even landed. This proves every control stays disabled for the
   * whole recovery window (refetch, then revalidate), and that revalidate genuinely runs again
   * once the reload completes — not just that the reload was kicked off.
   */
  it('keeps every control disabled through the whole concurrency-conflict recovery window, and re-validates once it completes', async () => {
    mockGetCart.mockResolvedValueOnce(oneItemCart)
    mockRevalidateCart.mockResolvedValue({
      cart: oneItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))
    await vi.waitFor(() => expect(mockRevalidateCart).toHaveBeenCalledTimes(1))

    mockRemoveCartItem.mockRejectedValueOnce(new ApiError('stale', { status: 409, code: 'concurrency_conflict' }))
    let resolveRefetch!: (cart: CartDto) => void
    mockGetCart.mockImplementationOnce(() => new Promise((resolve) => { resolveRefetch = resolve }))

    await wrapper.find('.cart-line-item__remove').trigger('click')
    await vi.waitFor(() => expect(mockGetCart).toHaveBeenCalledTimes(2))

    // The reload is still pending — every control must stay disabled, not just the row that
    // failed, and revalidate must not have been re-triggered yet (it should only run once the
    // reload actually completes, against the freshly-reloaded cart).
    expect(wrapper.find('.cart-line-item__remove').attributes('disabled')).toBeDefined()
    expect(wrapper.find('.cart-page__toolbar button').attributes('disabled')).toBeDefined()
    expect(mockRevalidateCart).toHaveBeenCalledTimes(1)

    resolveRefetch({ ...oneItemCart, rowVersion: 'BBBB' })
    await vi.waitFor(() => expect(mockRevalidateCart).toHaveBeenCalledTimes(2))
    await vi.waitFor(() => expect(wrapper.find('.cart-line-item__remove').attributes('disabled')).toBeUndefined())
  })

  /**
   * 組長 PR #29 review round 3, P2: a failed reload during recovery must show its own error
   * instead of silently leaving the "已重新載入" message up for a reload that never actually
   * happened.
   */
  it('shows a distinct error when the recovery reload itself fails', async () => {
    mockGetCart.mockResolvedValueOnce(oneItemCart)
    mockRevalidateCart.mockResolvedValue({
      cart: oneItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))

    mockRemoveCartItem.mockRejectedValueOnce(new ApiError('stale', { status: 409, code: 'concurrency_conflict' }))
    mockGetCart.mockRejectedValueOnce(new ApiError('network', { status: 500, code: 'unexpected_error' }))

    await wrapper.find('.cart-line-item__remove').trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('購物車重新載入失敗'))
    expect(wrapper.text()).not.toContain('此品項已被更新')
  })

  /**
   * 組長 PR #29 review round 3, P1: every shopper's cart used the same fixed query key regardless
   * of identity — a signed-in member's cached cart could still be what renders for a moment after
   * a different identity's CartPage mounts, since TanStack Query renders cached data for a key
   * synchronously before its background refetch resolves. Reusing one QueryClient (the same way
   * the real SPA has exactly one) across a guest mount and a member mount proves the member's page
   * never shows so much as a flash of the guest's item — a fresh, empty state is shown until the
   * member's own fetch actually resolves.
   */
  it('does not render the previous identity\'s cached cart after switching from guest to a signed-in member', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    mockGetCart.mockResolvedValueOnce(oneItemCart)
    mockRevalidateCart.mockResolvedValue({
      cart: oneItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const guestWrapper = await mountCartPage({ queryClient })
    await vi.waitFor(() => expect(guestWrapper.text()).toContain('RTX 4070'))
    guestWrapper.unmount()

    const memberCart: CartDto = {
      ...oneItemCart,
      items: [{ ...oneItemCart.items[0], publicId: 'item-2', skuCode: 'SKU-2', name: 'Member Exclusive Part' }],
    }
    mockGetCart.mockResolvedValueOnce(memberCart)
    mockRevalidateCart.mockResolvedValue({
      cart: memberCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const memberWrapper = await mountCartPage({ authenticated: true, queryClient })
    // Checked immediately after mount, before any awaiting — this is the exact moment a
    // shared/flat query key would synchronously render the guest's still-cached "RTX 4070" item.
    expect(memberWrapper.text()).not.toContain('RTX 4070')
    await vi.waitFor(() => expect(memberWrapper.text()).toContain('Member Exclusive Part'))
    expect(mockGetCart).toHaveBeenCalledTimes(2)
  })

  it('shows a retryable error without refetching for a non-concurrency item action failure', async () => {
    mockGetCart.mockResolvedValueOnce(oneItemCart)
    mockRevalidateCart.mockResolvedValue({
      cart: oneItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))

    mockRemoveCartItem.mockRejectedValueOnce(new ApiError('boom', { status: 500, code: 'unexpected_error' }))

    await wrapper.find('.cart-line-item__remove').trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('操作失敗'))
    expect(mockGetCart).toHaveBeenCalledTimes(1)
  })

  /** 組長 PR #29 review: CartDto.warnings was never rendered — always ships a human message already. */
  it('renders cart-level warnings', async () => {
    const cart: CartDto = {
      ...oneItemCart,
      warnings: [{ code: 'cart_item_limit_exceeded', message: '購物車已超過 100 件上限，請先清空部分品項。' }],
    }
    mockGetCart.mockResolvedValue(cart)
    mockRevalidateCart.mockResolvedValue({ cart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString() })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('購物車已超過 100 件上限，請先清空部分品項。'))
  })
})

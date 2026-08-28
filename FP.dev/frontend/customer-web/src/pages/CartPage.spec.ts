import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
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

const mockRemoveCartAssemblyGroup = vi.fn<(...args: unknown[]) => Promise<CartDto>>()

vi.mock('../features/cart/api', () => ({
  getCart: () => mockGetCart(),
  addCartItem: vi.fn(),
  updateCartItemQuantity: () => mockUpdateCartItemQuantity(),
  removeCartItem: () => mockRemoveCartItem(),
  removeCartAssemblyGroup: (...args: unknown[]) => mockRemoveCartAssemblyGroup(...args),
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

async function mountCartPage(
  options: { authenticated?: boolean, queryClient?: QueryClient, identityError?: boolean } = {},
) {
  const { default: CartPage } = await import('./CartPage.vue')
  const queryClient = options.queryClient ?? new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] })

  const pinia = createPinia()
  setActivePinia(pinia)
  const sessionStore = useSessionStore()
  if (options.identityError) {
    sessionStore.status = 'error'
  } else if (options.authenticated) {
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
  mockRemoveCartAssemblyGroup.mockReset()
})

describe('CartPage', () => {
  it('shows the loading state before the cart resolves', async () => {
    mockGetCart.mockReturnValue(new Promise(() => {}))
    mockRevalidateCart.mockReturnValue(new Promise(() => {}))

    const wrapper = await mountCartPage()

    expect(wrapper.text()).toContain('購物車載入中')
  })

  /**
   * 組長 PR #29 round-4 review, P2: the first revalidate used to fire unconditionally from
   * onMounted, starting at the same time as useCart()'s own initial GET rather than after it. If
   * revalidate resolved first and wrote the freshest cart into the query cache, the initial GET —
   * started earlier but arriving later — could still overwrite it with older data, while
   * issues/isCheckoutReady kept reflecting revalidate's now-orphaned newer result. The fix
   * sequences them: revalidate must not start until the initial GET has actually resolved, which
   * structurally removes the "older response arrives after newer one" race, since there is no
   * longer anything in flight for revalidate to race against.
   */
  it('does not start the first revalidate until the initial GET has resolved', async () => {
    let resolveGet!: (cart: CartDto) => void
    mockGetCart.mockImplementationOnce(() => new Promise((resolve) => { resolveGet = resolve }))

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('購物車載入中'))
    expect(mockRevalidateCart).not.toHaveBeenCalled()

    mockRevalidateCart.mockResolvedValueOnce({
      cart: oneItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })
    resolveGet(oneItemCart)

    await vi.waitFor(() => expect(mockRevalidateCart).toHaveBeenCalledTimes(1))
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))
  })

  /**
   * 組長 PR #29 round-5 review, P2: the round-4 fix above only covered a first-ever, no-cache
   * mount, where `isPending` starts true and there is nothing yet for revalidate to race against.
   * It missed a second case with the same shape of bug: the shared QueryClient's 30s staleTime
   * means revisiting /cart after being away that long shows the *cached* cart immediately
   * (`isPending` already false on mount) while TanStack's own default mount-refetch used to kick
   * off a second, implicit GET in the background at that same moment — racing this page's
   * `revalidate` call exactly like the round-4 finding, and able to silently overwrite whichever
   * of the two responses landed second. Fixed by having useCart() set `refetchOnMount: false`
   * (every path that can make the cart stale already funnels through an explicit revalidate), so
   * there is no second fetch left to race — proven here by seeding an already-cached cart before
   * mount and asserting the raw GET (`getCart`) is never called at all, only `revalidateCart`.
   */
  it('does not issue a redundant background GET racing revalidate when mounting with an already-cached, stale cart', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(['cart', 'guest', 'guest-test-key'], oneItemCart)
    mockRevalidateCart.mockResolvedValue({
      cart: oneItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage({ queryClient })
    await vi.waitFor(() => expect(mockRevalidateCart).toHaveBeenCalledTimes(1))
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))

    expect(mockGetCart).not.toHaveBeenCalled()
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
   * 組長 PR #29 round-6 review, P2: `isCheckoutReady` used to only ever be *set*, never reset —
   * starting a new revalidate, a Cart mutation succeeding, or the follow-up revalidate then
   * failing all left whatever the *previous* successful validation happened to say in place, so
   * checkout could stay enabled against a Cart that no longer matches what was actually validated.
   * Here: revalidate succeeds once (checkout enabled) -> the shopper removes an item, which
   * succeeds and writes a new Cart (new RowVersion) -> that mutation's own follow-up revalidate
   * then fails. Checkout must already be disabled the instant the mutation's new RowVersion lands
   * (before the follow-up revalidate even resolves), and must stay disabled once it fails.
   */
  it('disables checkout the instant a Cart mutation succeeds and lands a new RowVersion, and keeps it disabled when the follow-up revalidate then fails', async () => {
    const twoItemCart: CartDto = { ...oneItemCart, items: [...oneItemCart.items, { ...oneItemCart.items[0], publicId: 'item-2', skuCode: 'SKU-2' }] }
    mockGetCart.mockResolvedValue(twoItemCart)
    mockRevalidateCart.mockResolvedValueOnce({
      cart: twoItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))
    await vi.waitFor(() => expect(wrapper.find('.cart-page__checkout').attributes('disabled')).toBeUndefined())

    const updatedCart: CartDto = { ...twoItemCart, items: [twoItemCart.items[1]], rowVersion: 'BBBB' }
    mockRemoveCartItem.mockResolvedValueOnce(updatedCart)
    let resolveRevalidate!: (value: CartValidationDto) => void
    mockRevalidateCart.mockImplementationOnce(() => new Promise((resolve) => { resolveRevalidate = resolve }))

    await wrapper.find('.cart-line-item__remove').trigger('click')
    // The mutation has now succeeded and written the new RowVersion into the Cart query cache
    // (mutation-level onSuccess runs before the call-site onSuccess that starts the follow-up
    // revalidate), which is itself now in flight but hasn't resolved yet — checkout must already
    // be disabled (validatedForRowVersion still points at the old 'AAAA', not the new 'BBBB').
    await vi.waitFor(() => expect(mockRevalidateCart).toHaveBeenCalledTimes(2))
    expect(wrapper.find('.cart-page__checkout').attributes('disabled')).toBeDefined()

    // Once the follow-up revalidate actually succeeds for the new RowVersion, checkout re-enables.
    resolveRevalidate({ cart: updatedCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString() })
    await vi.waitFor(() => expect(wrapper.find('.cart-page__checkout').attributes('disabled')).toBeUndefined())
  })

  /**
   * Same scenario as above, but the follow-up revalidate after the mutation genuinely fails —
   * checkout must stay disabled, not fall back to the pre-mutation successful result.
   */
  it('keeps checkout disabled (does not fall back to the earlier successful validation) when the follow-up revalidate after a mutation genuinely fails', async () => {
    const twoItemCart: CartDto = { ...oneItemCart, items: [...oneItemCart.items, { ...oneItemCart.items[0], publicId: 'item-2', skuCode: 'SKU-2' }] }
    mockGetCart.mockResolvedValue(twoItemCart)
    mockRevalidateCart.mockResolvedValueOnce({
      cart: twoItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))
    await vi.waitFor(() => expect(wrapper.find('.cart-page__checkout').attributes('disabled')).toBeUndefined())

    const updatedCart: CartDto = { ...twoItemCart, items: [twoItemCart.items[1]], rowVersion: 'BBBB' }
    mockRemoveCartItem.mockResolvedValueOnce(updatedCart)
    mockRevalidateCart.mockRejectedValueOnce(new Error('network error'))

    await wrapper.find('.cart-line-item__remove').trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('購物車檢查失敗'))

    expect(wrapper.find('.cart-page__checkout').attributes('disabled')).toBeDefined()
  })

  /**
   * 組長 PR #29 round-6 review, P2: switching identity (login/logout/account switch) loads a
   * completely different Cart — the checkout gate must not carry over the *previous* identity's
   * successful validation. `validatedForRowVersion` binds to a specific Cart RowVersion, so a
   * freshly-loaded different identity's Cart (a different RowVersion) is correctly treated as
   * "not yet validated" without any separate identity-tracking logic.
   */
  it('resets the checkout gate when the shopper\'s identity switches to a different Cart (member login), not just on the initial load', async () => {
    const guestCart: CartDto = { ...oneItemCart, publicId: 'cart-guest', rowVersion: 'GUEST-1' }
    const memberCart: CartDto = { ...oneItemCart, publicId: 'cart-member', rowVersion: 'MEMBER-1' }
    mockGetCart.mockResolvedValueOnce(guestCart)
    mockRevalidateCart.mockResolvedValueOnce({
      cart: guestCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.find('.cart-page__checkout').attributes('disabled')).toBeUndefined())

    // The shopper logs in — a different identity key, a different (member) Cart, with no
    // validation performed for it yet.
    mockGetCart.mockResolvedValueOnce(memberCart)
    let resolveMemberRevalidate!: (value: CartValidationDto) => void
    mockRevalidateCart.mockImplementationOnce(() => new Promise((resolve) => { resolveMemberRevalidate = resolve }))

    useSessionStore().status = 'authenticated'
    useSessionStore().user = testMember

    // Let the identity switch's reactivity actually settle (new query key, brief loading state
    // for the uncached member Cart, then resolution) before checking anything — checking too
    // early can still observe the *previous* (guest) render, since Vue hasn't reacted yet.
    await flushPromises()
    await vi.waitFor(() => expect(wrapper.find('.cart-page__checkout').exists()).toBe(true))
    // Must not still show the guest identity's successful checkout state for the new Cart.
    expect(wrapper.find('.cart-page__checkout').attributes('disabled')).toBeDefined()

    resolveMemberRevalidate({ cart: memberCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString() })
    await vi.waitFor(() => expect(wrapper.find('.cart-page__checkout').attributes('disabled')).toBeUndefined())
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
   * 組長 PR #29 round-6 review, P1: an assembly-group item is one SKU of one physical build —
   * offering the same per-item quantity/remove controls a plain SKU gets would let a shopper
   * change one member's quantity or remove it alone, leaving the rest of the group referring to a
   * build that no longer matches what was actually configured (and the backend now rejects it
   * outright: cart_assembly_item_immutable). Grouped items must render with no quantity `<select>`
   * and no "移除" button at all; a plain SKU alongside them must be unaffected.
   */
  it('renders assembly-group items read-only (no quantity select, no remove button), while a plain SKU keeps full controls', async () => {
    const cart: CartDto = {
      ...oneItemCart,
      items: [
        { ...oneItemCart.items[0], publicId: 'a1', skuCode: 'CPU-1', assemblyGroupKey: 'build-1' },
        { ...oneItemCart.items[0], publicId: 'a2', skuCode: 'GPU-1', assemblyGroupKey: 'build-1' },
        { ...oneItemCart.items[0], publicId: 'p1', skuCode: 'MOUSE-1', assemblyGroupKey: null },
      ],
    }
    mockGetCart.mockResolvedValue(cart)
    mockRevalidateCart.mockResolvedValue({ cart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString() })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU-1'))

    const group = wrapper.find('.cart-page__assembly-group')
    expect(group.find('select').exists()).toBe(false)
    expect(group.findAll('button').some((button) => button.text() === '移除')).toBe(false)
    expect(group.text()).toContain('不可單獨調整數量或移除')

    // The plain SKU, rendered outside the group, is unaffected.
    const plainItem = wrapper.find('.cart-page__items > li:not(.cart-page__assembly-group)')
    expect(plainItem.find('select').exists()).toBe(true)
    expect(plainItem.findAll('button').some((button) => button.text() === '移除')).toBe(true)
  })

  /**
   * 組長 PR #29 round 7 review, P1（AUTO-DEC-015）: blocking per-item edits was correct but left no
   * executable recovery path at all — a group whose SKU went unavailable held the checkout gate
   * open forever. "整組移除" is the one action that works, and it must be a single atomic backend
   * call, never a client-side loop of per-item DELETEs (a mid-loop failure would split the group).
   */
  it('removes a whole assembly group in one atomic backend call, then revalidates', async () => {
    const groupedCart: CartDto = {
      ...oneItemCart,
      items: [
        { ...oneItemCart.items[0], publicId: 'a1', skuCode: 'CPU-1', assemblyGroupKey: 'build-1' },
        { ...oneItemCart.items[0], publicId: 'a2', skuCode: 'GPU-1', assemblyGroupKey: 'build-1' },
        { ...oneItemCart.items[0], publicId: 'p1', skuCode: 'MOUSE-1', assemblyGroupKey: null },
      ],
    }
    const afterRemoval: CartDto = {
      ...oneItemCart,
      rowVersion: 'BBBB',
      items: [{ ...oneItemCart.items[0], publicId: 'p1', skuCode: 'MOUSE-1', assemblyGroupKey: null }],
    }
    mockGetCart.mockResolvedValue(groupedCart)
    mockRevalidateCart.mockResolvedValueOnce({
      cart: groupedCart,
      isCheckoutReady: false,
      issues: [{ itemPublicId: 'a1', code: 'sku_unavailable', severity: 'error', availableActions: ['remove-group'] }],
      validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU-1'))
    // The backend must advertise the action it will actually honor, and the page must label it.
    await vi.waitFor(() => expect(wrapper.text()).toContain('整組移除'))

    mockRemoveCartAssemblyGroup.mockResolvedValueOnce(afterRemoval)
    mockRevalidateCart.mockResolvedValueOnce({
      cart: afterRemoval, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const groupButton = wrapper.find('.cart-page__assembly-group-actions button')
    await groupButton.trigger('click')

    await vi.waitFor(() => expect(mockRemoveCartAssemblyGroup).toHaveBeenCalledTimes(1))
    expect(mockRemoveCartAssemblyGroup).toHaveBeenCalledWith('build-1', 'AAAA', 'guest-test-key')
    // Exactly one atomic call — never a per-item DELETE loop.
    expect(mockRemoveCartItem).not.toHaveBeenCalled()

    await vi.waitFor(() => expect(wrapper.text()).not.toContain('CPU-1'))
    expect(wrapper.text()).toContain('MOUSE-1')
  })

  it('shows a retryable error on the group when the atomic removal fails, leaving the group rendered', async () => {
    const groupedCart: CartDto = {
      ...oneItemCart,
      items: [
        { ...oneItemCart.items[0], publicId: 'a1', skuCode: 'CPU-1', assemblyGroupKey: 'build-1' },
        { ...oneItemCart.items[0], publicId: 'a2', skuCode: 'GPU-1', assemblyGroupKey: 'build-1' },
      ],
    }
    mockGetCart.mockResolvedValue(groupedCart)
    mockRevalidateCart.mockResolvedValue({
      cart: groupedCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU-1'))

    mockRemoveCartAssemblyGroup.mockRejectedValueOnce(new ApiError('boom', { status: 500, code: 'unexpected_error' }))

    await wrapper.find('.cart-page__assembly-group-actions button').trigger('click')

    await vi.waitFor(() => expect(wrapper.find('.cart-page__assembly-group-error').exists()).toBe(true))
    expect(wrapper.find('.cart-page__assembly-group-error').text()).toContain('操作失敗')
    // The group is still there — a failed removal must not optimistically drop it from the page.
    expect(wrapper.text()).toContain('CPU-1')
    expect(wrapper.text()).toContain('GPU-1')
  })

  it('reloads the cart before letting the shopper retry after a concurrency conflict on group removal', async () => {
    const groupedCart: CartDto = {
      ...oneItemCart,
      items: [
        { ...oneItemCart.items[0], publicId: 'a1', skuCode: 'CPU-1', assemblyGroupKey: 'build-1' },
        { ...oneItemCart.items[0], publicId: 'a2', skuCode: 'GPU-1', assemblyGroupKey: 'build-1' },
      ],
    }
    mockGetCart.mockResolvedValue(groupedCart)
    mockRevalidateCart.mockResolvedValue({
      cart: groupedCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU-1'))
    const getCartCallsBefore = mockGetCart.mock.calls.length

    mockRemoveCartAssemblyGroup.mockRejectedValueOnce(
      new ApiError('stale', { status: 409, code: 'concurrency_conflict' }))

    await wrapper.find('.cart-page__assembly-group-actions button').trigger('click')

    await vi.waitFor(() => expect(mockGetCart.mock.calls.length).toBe(getCartCallsBefore + 1))
    expect(wrapper.find('.cart-page__assembly-group-error').text()).toContain('已重新載入')
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

describe('CartPage — session identity fail-closed (組長 PR #29 round 7 review, P1)', () => {
  /**
   * A failed session refresh (status 'error') must not be silently treated like a confirmed guest
   * — useCart()'s query gate (isIdentityConfirmed) now stays disabled for 'error' the same as
   * 'loading', so this page must show why instead of spinning forever on a query that will never
   * run, and must never fetch a guest-keyed cart that could actually belong to a still-logged-in
   * member whose Cookie the refresh call itself failed to confirm.
   */
  it('shows an identity-error banner with a retry entry point instead of the cart, and never fetches under the guest key', async () => {
    const wrapper = await mountCartPage({ identityError: true })
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('無法確認登入狀態')
    expect(wrapper.findAll('button').some((button) => button.text() === '重試')).toBe(true)
    expect(mockGetCart).not.toHaveBeenCalled()
  })

  it('recovers to the normal cart view once a retried refresh resolves to a confirmed identity', async () => {
    mockGetCart.mockResolvedValue(oneItemCart)
    mockRevalidateCart.mockResolvedValue({
      cart: oneItemCart, isCheckoutReady: true, issues: [], validatedAtUtc: new Date().toISOString(),
    })

    const wrapper = await mountCartPage({ identityError: true })
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('無法確認登入狀態')
    expect(mockGetCart).not.toHaveBeenCalled()

    useSessionStore().status = 'anonymous'
    await vi.waitFor(() => expect(wrapper.text()).toContain('RTX 4070'))
    expect(wrapper.text()).not.toContain('無法確認登入狀態')
  })
})

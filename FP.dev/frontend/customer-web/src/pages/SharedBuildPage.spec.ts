import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSessionStore } from '../stores/session'
import type { SharedBuildDto } from '../features/builds/types'

const mockGetSharedBuild = vi.fn()
const mockCreateBuildList = vi.fn()
const mockAddBuildToCart = vi.fn()

vi.mock('../features/builds/api', () => ({
  listBuildLists: vi.fn(),
  getBuildList: vi.fn(),
  createBuildList: (...args: unknown[]) => mockCreateBuildList(...args),
  updateBuildList: vi.fn(),
  deleteBuildList: vi.fn(),
  createBuildShare: vi.fn(),
  revokeBuildShare: vi.fn(),
  getSharedBuild: (...args: unknown[]) => mockGetSharedBuild(...args),
  addBuildToCart: (...args: unknown[]) => mockAddBuildToCart(...args),
  checkCompatibility: vi.fn(),
}))

function sharedBuild(overrides: Partial<SharedBuildDto> = {}): SharedBuildDto {
  return {
    sharePublicId: 'share-1',
    name: '分享的組裝',
    items: [{
      publicId: 'item-1', skuPublicId: 'sku-1', skuCode: 'CPU-1', name: '測試 CPU', categoryCode: 'CPU',
      quantity: 1, sortOrder: 0, unitPrice: 5000, lineTotal: 5000, availability: 'available',
    }],
    compatibility: { overall: 'compatible', ruleSetVersion: 1, settingsVersion: 1, results: [] },
    totals: { merchandise: 5000, assemblyFee: 300, grandTotal: 5300, currency: 'TWD' },
    canCopy: true,
    canAddToCart: true,
    ...overrides,
  }
}

async function mountPage(authenticated: boolean) {
  const { default: SharedBuildPage } = await import('./SharedBuildPage.vue')
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/builds/shared/:shareToken', name: 'build-shared', component: SharedBuildPage, props: true },
      { path: '/builds/:buildId', name: 'build-detail', component: { template: '<div />' } },
      { path: '/login', name: 'login', component: { template: '<div />' } },
    ],
  })
  const pinia = createPinia()
  setActivePinia(pinia)
  useSessionStore().status = authenticated ? 'authenticated' : 'anonymous'

  await router.push('/builds/shared/abc123')
  await router.isReady()

  const wrapper = mount(SharedBuildPage, {
    props: { shareToken: 'abc123' },
    global: {
      plugins: [[VueQueryPlugin, { queryClient }], pinia, router],
      stubs: { CompatibilityFindingsList: true },
    },
  })
  return { wrapper, router }
}

beforeEach(() => {
  mockGetSharedBuild.mockReset()
  mockCreateBuildList.mockReset()
  mockAddBuildToCart.mockReset()
  window.sessionStorage.clear()
})

describe('SharedBuildPage — copy/add-to-cart actions (組長 PR #35 review, item 3)', () => {
  it('redirects an anonymous viewer to /login (returning to this page) instead of acting', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    const { wrapper, router } = await mountPage(false)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    const copyButton = wrapper.findAll('button').find((button) => button.text() === '複製為我的清單')
    await copyButton!.trigger('click')

    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('login'))
    expect(router.currentRoute.value.query.redirect).toBe('/builds/shared/abc123')
    expect(mockCreateBuildList).not.toHaveBeenCalled()
  })

  it('copies the shared build into a new owned list and navigates to it, for an authenticated viewer', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    mockCreateBuildList.mockResolvedValue({ publicId: 'new-build-1', rowVersion: 'AAAA' })

    const { wrapper, router } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    const copyButton = wrapper.findAll('button').find((button) => button.text() === '複製為我的清單')
    await copyButton!.trigger('click')

    await vi.waitFor(() => expect(mockCreateBuildList).toHaveBeenCalledWith(
      expect.objectContaining({ items: [{ skuPublicId: 'sku-1', quantity: 1 }] }),
    ))
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/builds/new-build-1'))
    expect(mockAddBuildToCart).not.toHaveBeenCalled()
  })

  it('copies then adds to cart for "整套加入購物車", for an authenticated viewer', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    mockCreateBuildList.mockResolvedValue({ publicId: 'new-build-1', rowVersion: 'AAAA' })
    mockAddBuildToCart.mockResolvedValue({})

    const { wrapper, router } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    const cartButton = wrapper.findAll('button').find((button) => button.text() === '整套加入購物車')
    await cartButton!.trigger('click')

    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledWith(
      'new-build-1', expect.objectContaining({ quantity: 1, buildRowVersion: 'AAAA' }), expect.any(String),
    ))
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/builds/new-build-1'))
  })

  it('does not render either action button when the backend reports both as unavailable', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild({ canCopy: false, canAddToCart: false }))
    const { wrapper } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    expect(wrapper.findAll('button').some((button) => button.text() === '複製為我的清單')).toBe(false)
    expect(wrapper.findAll('button').some((button) => button.text() === '整套加入購物車')).toBe(false)
  })
})

describe('SharedBuildPage — "整套加入購物車" retry idempotency (組長 PR #35 round-2 review, P1-4)', () => {
  /**
   * Every click used to create a brand-new copy list before adding it to cart. If the add-to-cart
   * response was lost after actually succeeding (or genuinely failed), retrying created *another*
   * copy and added it again — the point of an Idempotency-Key is defeated if it's attached to a
   * different copy's publicId every time. A retry must reuse the copy already created and resend
   * the add-to-cart with the same key, not start over.
   */
  it('reuses the already-created copy and the same Idempotency-Key when retrying after a failed add-to-cart', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    mockCreateBuildList.mockResolvedValue({ publicId: 'new-build-1', rowVersion: 'AAAA' })
    mockAddBuildToCart.mockRejectedValueOnce(new Error('network error'))
    mockAddBuildToCart.mockResolvedValueOnce({})

    const { wrapper, router } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    function findCartButton() {
      return wrapper.findAll('button').find((button) => button.text() === '整套加入購物車')!
    }

    await findCartButton().trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(1))
    await vi.waitFor(() => expect(findCartButton().attributes('disabled')).toBeUndefined())

    await findCartButton().trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(2))
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/builds/new-build-1'))

    // Only one copy was ever created, across both attempts.
    expect(mockCreateBuildList).toHaveBeenCalledTimes(1)
    // Both add-to-cart calls targeted the same copy with the same Idempotency-Key.
    const [firstPublicId, , firstKey] = mockAddBuildToCart.mock.calls[0]!
    const [secondPublicId, , secondKey] = mockAddBuildToCart.mock.calls[1]!
    expect(secondPublicId).toBe(firstPublicId)
    expect(secondKey).toBe(firstKey)
  })

  it('uses a fresh Idempotency-Key and creates a fresh copy for a new attempt after a successful add-to-cart', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    mockCreateBuildList.mockResolvedValue({ publicId: 'new-build-1', rowVersion: 'AAAA' })
    mockAddBuildToCart.mockResolvedValue({})

    const { wrapper } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    const cartButton = wrapper.findAll('button').find((button) => button.text() === '整套加入購物車')!
    await cartButton.trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(1))

    // A genuinely new attempt (e.g. the shopper navigated back and clicked again) after success.
    await cartButton.trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(2))

    expect(mockCreateBuildList).toHaveBeenCalledTimes(2)
    const firstKey = mockAddBuildToCart.mock.calls[0]![2]
    const secondKey = mockAddBuildToCart.mock.calls[1]![2]
    expect(secondKey).not.toBe(firstKey)
  })
})

describe('SharedBuildPage — resuming the original action after login (組長 PR #35 round-2 review, P2-5)', () => {
  /**
   * The anonymous-viewer redirect used to only remember to send the shopper to /login — nothing
   * remembered *which* button they'd pressed, so returning authenticated never finished either
   * action; they had to press it again. The pending-action marker (sessionStorage, scoped to this
   * shareToken) must survive the round trip and auto-resume exactly the action that triggered it.
   */
  it('automatically finishes "整套加入購物車" once the session becomes authenticated, after that click redirected to /login', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    mockCreateBuildList.mockResolvedValue({ publicId: 'new-build-1', rowVersion: 'AAAA' })
    mockAddBuildToCart.mockResolvedValue({})

    const { wrapper, router } = await mountPage(false)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    const cartButton = wrapper.findAll('button').find((button) => button.text() === '整套加入購物車')!
    await cartButton.trigger('click')
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('login'))
    expect(mockCreateBuildList).not.toHaveBeenCalled()

    useSessionStore().status = 'authenticated'
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(1))
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/builds/new-build-1'))
  })

  it('automatically finishes "複製為我的清單" (not add-to-cart) once authenticated, matching the button that was actually pressed', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    mockCreateBuildList.mockResolvedValue({ publicId: 'new-build-1', rowVersion: 'AAAA' })

    const { wrapper, router } = await mountPage(false)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    const copyButton = wrapper.findAll('button').find((button) => button.text() === '複製為我的清單')!
    await copyButton.trigger('click')
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('login'))

    useSessionStore().status = 'authenticated'
    await vi.waitFor(() => expect(mockCreateBuildList).toHaveBeenCalledTimes(1))
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/builds/new-build-1'))
    expect(mockAddBuildToCart).not.toHaveBeenCalled()
  })

  it('does not auto-resume any action for a viewer who was already authenticated (no button was ever pressed)', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())

    const { wrapper } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(mockCreateBuildList).not.toHaveBeenCalled()
    expect(mockAddBuildToCart).not.toHaveBeenCalled()
  })
})

describe('SharedBuildPage — totals breakdown (送出前文件核對發現)', () => {
  /**
   * 商品、組裝與相容性.md lists 組裝服務費 NT$300／台 as an explicit line of a build group, and
   * BuildDetailPage.vue shows the three-line breakdown. The share page used to show only
   * grandTotal, so someone opening a shared link could not tell where the price came from.
   * Values always come from `totals` — never a hardcoded 300 in the frontend.
   */
  it('shows the merchandise / assembly-fee / grand-total breakdown, not just the grand total', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    const { wrapper } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    const totals = wrapper.find('.shared-build-page__totals').text()
    expect(totals).toContain('商品小計')
    expect(totals).toContain('5,000')
    expect(totals).toContain('組裝費')
    expect(totals).toContain('300')
    expect(totals).toContain('合計')
    expect(totals).toContain('5,300')
  })
})

describe('SharedBuildPage — share-token identity switch (組長 PR #35 round-3 review, P2-5)', () => {
  /**
   * Vue Router reuses this component instance across /builds/shared/:shareToken navigations on the
   * same route record — it does not unmount/remount just because the token changed (same precedent
   * as ProductDetailPage.vue's selectedSkuPublicId fix, PR #24 review). The pending-action storage
   * key, the pending copy-for-cart, the cart Idempotency-Key and the auto-resume latch were all
   * computed once at setup, so following a second shared link in the same tab carried the FIRST
   * link's state into it.
   */
  it('does not fire the previous token\'s pending action against a different shared build', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    mockCreateBuildList.mockResolvedValue({ publicId: 'new-build-1', rowVersion: 'AAAA' })

    // A pending "copy" was left behind for a DIFFERENT shared link in this tab.
    window.sessionStorage.setItem('doselect.sharedBuild.pendingAction.other-token', 'copy')

    const { wrapper } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))
    await new Promise((resolve) => setTimeout(resolve, 0))

    // abc123 has no pending action of its own — the other token's marker must not fire here.
    expect(mockCreateBuildList).not.toHaveBeenCalled()
    expect(window.sessionStorage.getItem('doselect.sharedBuild.pendingAction.other-token')).toBe('copy')
  })

  it('reads the new token\'s own pending action after navigating to a different shared build', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    mockCreateBuildList.mockResolvedValue({ publicId: 'new-build-2', rowVersion: 'BBBB' })

    const { wrapper } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(mockCreateBuildList).not.toHaveBeenCalled()

    // The shopper follows a second shared link in the same tab, which does have a pending action.
    window.sessionStorage.setItem('doselect.sharedBuild.pendingAction.token-2', 'copy')
    await wrapper.setProps({ shareToken: 'token-2' })
    await flushPromises()

    // The auto-resume latch must have been reset by the token change, otherwise this never fires.
    await vi.waitFor(() => expect(mockCreateBuildList).toHaveBeenCalledTimes(1))
    expect(window.sessionStorage.getItem('doselect.sharedBuild.pendingAction.token-2')).toBeNull()
  })

  it('does not reuse the previous token\'s copy or Idempotency-Key for a different shared build', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    mockCreateBuildList.mockResolvedValue({ publicId: 'new-build-1', rowVersion: 'AAAA' })
    mockAddBuildToCart.mockRejectedValueOnce(new Error('network error'))

    const { wrapper } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    const cartButton = () => wrapper.findAll('button').find((button) => button.text() === '整套加入購物車')!
    await cartButton().trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(1))
    const firstKey = mockAddBuildToCart.mock.calls[0]![2]
    await vi.waitFor(() => expect(cartButton().attributes('disabled')).toBeUndefined())

    // Navigate to a different shared build, then add THAT one to the cart.
    mockCreateBuildList.mockResolvedValue({ publicId: 'new-build-2', rowVersion: 'BBBB' })
    mockAddBuildToCart.mockResolvedValueOnce({})
    await wrapper.setProps({ shareToken: 'token-2' })
    await flushPromises()

    await cartButton().trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(2))

    // A fresh copy was made for the new shared build — the first build's pending copy must not be
    // reused — and the Idempotency-Key is new, because this is a different logical operation, not
    // a retry of the first one.
    const [secondPublicId, , secondKey] = mockAddBuildToCart.mock.calls[1]!
    expect(secondPublicId).toBe('new-build-2')
    expect(secondKey).not.toBe(firstKey)
  })

  it('clears a previous token\'s error message when navigating to a different shared build', async () => {
    mockGetSharedBuild.mockResolvedValue(sharedBuild())
    mockCreateBuildList.mockRejectedValueOnce(new Error('boom'))

    const { wrapper } = await mountPage(true)
    await vi.waitFor(() => expect(wrapper.text()).toContain('分享的組裝'))

    await wrapper.findAll('button').find((button) => button.text() === '複製為我的清單')!.trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('操作失敗'))

    await wrapper.setProps({ shareToken: 'token-2' })
    await flushPromises()

    expect(wrapper.text()).not.toContain('操作失敗')
  })
})

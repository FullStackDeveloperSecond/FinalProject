import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { ApiError } from '@doselect/web-shared/api'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSessionStore } from '../stores/session'
import type { BuildListDto, CompatibilityCheckDto } from '../features/builds/types'
import { stageBuildImport } from '../features/builds/buildImport'

const mockCreateBuildList = vi.fn()
const mockCheckCompatibility = vi.fn()
const mockAddBuildToCart = vi.fn()
const mockGetCart = vi.fn()
const mockSearchProducts = vi.fn()
vi.mock('../features/cart/api', () => ({ getCart: (...args: unknown[]) => mockGetCart(...args) }))
vi.mock('../features/catalog/api', () => ({
  searchProducts: (...args: unknown[]) => mockSearchProducts(...args),
  getProductDetail: vi.fn(),
}))

vi.mock('../features/builds/api', () => ({
  listBuildLists: vi.fn(),
  getBuildList: vi.fn(),
  createBuildList: (...args: unknown[]) => mockCreateBuildList(...args),
  updateBuildList: vi.fn(),
  deleteBuildList: vi.fn(),
  createBuildShare: vi.fn(),
  revokeBuildShare: vi.fn(),
  getSharedBuild: vi.fn(),
  addBuildToCart: (...args: unknown[]) => mockAddBuildToCart(...args),
  checkCompatibility: (...args: unknown[]) => mockCheckCompatibility(...args),
}))

const mockLoadGuestBuildDraft = vi.fn()
const mockSaveGuestBuildDraft = vi.fn()
const mockClearGuestBuildDraft = vi.fn()

// markPendingBuildSaveResume/consumePendingBuildSaveResume are left as their real (sessionStorage
// -backed) implementations — they're the mechanism under test for 組長 PR #35 round-2 review,
// P1-1, and are simple enough that exercising the real round trip through sessionStorage is more
// convincing than re-implementing the same logic a second time as a mock.
vi.mock('../features/builds/guestBuildDraft', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../features/builds/guestBuildDraft')>()
  return {
    ...actual,
    loadGuestBuildDraft: () => mockLoadGuestBuildDraft(),
    saveGuestBuildDraft: (...args: unknown[]) => mockSaveGuestBuildDraft(...args),
    clearGuestBuildDraft: () => mockClearGuestBuildDraft(),
  }
})

const compatibleResult: CompatibilityCheckDto = {
  overall: 'compatible', ruleSetVersion: 1, settingsVersion: 1, results: [], evaluatedAtUtc: new Date().toISOString(),
}

const draftItem = { skuPublicId: 'sku-1', quantity: 1, name: 'CPU 測試品', categoryCode: 'CPU' }

async function mountPage() {
  const { default: NewBuildPage } = await import('./NewBuildPage.vue')
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/builds/new', name: 'build-new', component: NewBuildPage },
      { path: '/builds/:buildId', name: 'build-detail', component: { template: '<div />' } },
      { path: '/login', name: 'login', component: { template: '<div />' } },
      { path: '/cart', name: 'cart', component: { template: '<div />' } },
    ],
  })
  const pinia = createPinia()
  setActivePinia(pinia)

  await router.push('/builds/new')
  await router.isReady()

  const wrapper = mount(NewBuildPage, {
    global: { plugins: [[VueQueryPlugin, { queryClient }], pinia, router] },
  })
  return { wrapper, router }
}

beforeEach(() => {
  mockAddBuildToCart.mockReset()
  mockGetCart.mockReset()
  mockCreateBuildList.mockReset()
  mockCheckCompatibility.mockReset()
  mockCheckCompatibility.mockResolvedValue(compatibleResult)
  mockSearchProducts.mockReset()
  mockLoadGuestBuildDraft.mockReset()
  mockLoadGuestBuildDraft.mockReturnValue({ name: '', items: [] })
  mockSaveGuestBuildDraft.mockReset()
  mockClearGuestBuildDraft.mockReset()
  window.sessionStorage.clear()
})

describe('NewBuildPage', () => {
  it('fills every missing required category with one in-stock demo product', async () => {
    mockSearchProducts.mockImplementation(async ({ category }: { category: string }) => ({
      items: [{
        productPublicId: `product-${category}`,
        defaultSkuPublicId: `sku-${category}`,
        productCode: `PRODUCT-${category}`,
        skuCode: `SKU-${category}`,
        name: `${category} 展示商品`,
        category: { code: category, name: category },
        brand: { code: 'DEMO', name: 'Demo' },
        price: { list: 1000, sale: null },
        availability: 'inStock',
        primaryImage: null,
        badges: [],
      }],
      pageNumber: 1,
      pageSize: 1,
      totalCount: 1,
      totalPages: 1,
    }))

    const { wrapper } = await mountPage()
    await wrapper.findAll('button').find(button => button.text() === '一鍵帶入 Demo 配置')!.trigger('click')
    await vi.waitFor(() => expect(mockSearchProducts).toHaveBeenCalledTimes(8))

    expect(mockSearchProducts).toHaveBeenCalledWith(expect.objectContaining({
      category: 'CPU', inStock: true, sort: 'priceAsc', pageSize: 1,
    }))
    expect(wrapper.text()).toContain('已帶入 8 個必要分類')
    expect(wrapper.findAll('.build-items-editor__slot-items li')).toHaveLength(8)
    expect(mockSaveGuestBuildDraft).toHaveBeenLastCalledWith(expect.objectContaining({
      name: 'Demo 一鍵配置',
      items: expect.arrayContaining([expect.objectContaining({ skuPublicId: 'sku-CPU', categoryCode: 'CPU' })]),
    }))
    wrapper.unmount()
  })

  it('keeps the draft unchanged when a demo category has no sellable product', async () => {
    mockLoadGuestBuildDraft.mockReturnValue({ name: '原清單', items: [draftItem] })
    mockSearchProducts.mockImplementation(async ({ category }: { category: string }) => ({
      items: category === 'GPU' ? [] : [{
        defaultSkuPublicId: `sku-${category}`, name: `${category} 商品`, skuCode: `SKU-${category}`,
      }],
    }))

    const { wrapper } = await mountPage()
    await wrapper.findAll('button').find(button => button.text() === '一鍵帶入 Demo 配置')!.trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('未變更原清單'))

    expect(wrapper.text()).toContain('CPU 測試品')
    expect(wrapper.text()).not.toContain('MOTHERBOARD 商品')
    wrapper.unmount()
  })

  it('previews imported owned parts and saves them separately only after confirmation', async () => {
    const owned = { sourceType: 'catalogSku', skuPublicId: 'owned-psu', categoryCode: 'PSU', displayName: '我的電源供應器', specifications: [], quantity: 1, confirmedByUser: true }
    stageBuildImport({ name: '匯入', items: [draftItem], ownedParts: [owned] })
    const { wrapper } = await mountPage()
    expect(wrapper.text()).toContain('確認匯入內容')
    expect(mockCreateBuildList).not.toHaveBeenCalled()
    await wrapper.findAll('button').find(button => button.text() === '以匯入內容取代整份草稿')!.trigger('click')
    await wrapper.find('.new-build-page__actions button').trigger('click')
    await vi.waitFor(() => expect(mockCreateBuildList).toHaveBeenCalled())
    expect(mockCreateBuildList.mock.calls[0]![0].ownedParts).toEqual([owned])
    expect(mockCreateBuildList.mock.calls[0]![0].items).toEqual([{ skuPublicId: draftItem.skuPublicId, quantity: draftItem.quantity }])
    wrapper.unmount()
  })

  it('keeps the exact transfer request and idempotency key when retrying a lost response', async () => {
    mockLoadGuestBuildDraft.mockReturnValue({ name: '移轉', items: [draftItem], cartSource: {
      publicId: 'cart-1', rowVersion: 'cart-v1', items: [{ cartItemPublicId: 'row-1', skuPublicId: draftItem.skuPublicId, quantity: 1 }],
    } })
    mockCreateBuildList.mockResolvedValue({ publicId: 'build-1', rowVersion: 'build-v1' })
    mockGetCart.mockResolvedValue({ publicId: 'cart-1', rowVersion: 'cart-v1' })
    mockAddBuildToCart.mockRejectedValueOnce(new Error('連線中斷')).mockResolvedValueOnce({})
    const { wrapper, router } = await mountPage()
    useSessionStore().status = 'authenticated'
    await wrapper.vm.$nextTick()
    const purchase = () => wrapper.findAll('button').find(button => button.text() === '儲存並加入購物車')!
    await purchase().trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('連線中斷'))
    await purchase().trigger('click')
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/cart'))
    expect(mockCreateBuildList).toHaveBeenCalledTimes(1)
    expect(mockGetCart).toHaveBeenCalledTimes(1)
    expect(mockAddBuildToCart.mock.calls[0]).toEqual(mockAddBuildToCart.mock.calls[1])
    expect(mockAddBuildToCart.mock.calls[0]![1]).toEqual({ quantity: 1, buildRowVersion: 'build-v1', cartRowVersion: 'cart-v1', cartTransfers: [{ cartItemPublicId: 'row-1', quantity: 1 }] })
    wrapper.unmount()
  })

  it('does not redirect or clear the draft when an in-flight purchase finishes after leaving the page', async () => {
    mockLoadGuestBuildDraft.mockReturnValue({ name: '組裝', items: [draftItem] })
    mockCreateBuildList.mockResolvedValue({ publicId: 'build-1', rowVersion: 'build-v1' })
    mockGetCart.mockResolvedValue({ publicId: 'cart-1', rowVersion: 'cart-v1' })
    let finishPurchase!: (value: unknown) => void
    mockAddBuildToCart.mockImplementation(() => new Promise(resolve => { finishPurchase = resolve }))
    const { wrapper, router } = await mountPage()
    useSessionStore().status = 'authenticated'
    await wrapper.vm.$nextTick()
    await wrapper.findAll('button').find(button => button.text() === '儲存並加入購物車')!.trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledOnce())
    wrapper.unmount()
    await router.push('/login')
    finishPurchase({})
    await new Promise(resolve => setTimeout(resolve, 0))
    expect(router.currentRoute.value.path).toBe('/login')
    expect(mockClearGuestBuildDraft).not.toHaveBeenCalled()
  })

  it('does not turn an old cart selection into additional purchases after login merges or changes the cart', async () => {
    mockLoadGuestBuildDraft.mockReturnValue({ name: '移轉', items: [draftItem], cartSource: {
      publicId: 'guest-cart', rowVersion: 'old', items: [{ cartItemPublicId: 'old-row', skuPublicId: draftItem.skuPublicId, quantity: 1 }],
    } })
    mockCreateBuildList.mockResolvedValue({ publicId: 'build-1', rowVersion: 'build-v1' })
    mockGetCart.mockResolvedValue({ publicId: 'member-cart', rowVersion: 'new' })
    const { wrapper } = await mountPage()
    useSessionStore().status = 'authenticated'
    await wrapper.vm.$nextTick()
    await wrapper.findAll('button').find(button => button.text() === '儲存並加入購物車')!.trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('購物車已變更'))
    expect(mockAddBuildToCart).not.toHaveBeenCalled()
    expect(mockClearGuestBuildDraft).not.toHaveBeenCalled()
    wrapper.unmount()
  })
  /**
   * 組長 PR #35 review, item 2: a guest hitting save() used to be sent to /unauthorized — a dead
   * end. It must redirect to /login instead, preserving this exact page as the return target so
   * the draft (never cleared on a failed save) can be resumed after login.
   */
  it('redirects to /login with this page as the return target when save hits a 401', async () => {
    mockLoadGuestBuildDraft.mockReturnValue({ name: '我的組裝', items: [draftItem] })
    mockCreateBuildList.mockRejectedValueOnce(new ApiError('unauthorized', { status: 401, code: 'unauthorized' }))

    const { wrapper, router } = await mountPage()
    useSessionStore().status = 'anonymous'
    await wrapper.vm.$nextTick()

    await wrapper.find('.new-build-page__actions button').trigger('click')
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('login'))

    expect(router.currentRoute.value.query.redirect).toBe('/builds/new')
    expect(mockClearGuestBuildDraft).not.toHaveBeenCalled()
  })

  /**
   * 組長 PR #35 review, item 2: "登入成功後再把 localStorage 草稿建立成新的會員清單" — the save
   * must finish automatically once the session resolves to authenticated after returning from the
   * 401 -> /login redirect this exact draft triggered, not require pressing the button again.
   */
  it('automatically finishes the save once the session becomes authenticated, after that specific save hit 401 and redirected to /login', async () => {
    mockLoadGuestBuildDraft.mockReturnValue({ name: '我的組裝', items: [draftItem] })
    const savedBuild: BuildListDto = {
      publicId: 'build-1',
      name: '我的組裝',
      items: [],
      compatibility: { overall: 'compatible', ruleSetVersion: 1, settingsVersion: 1, results: [] },
      totals: { merchandise: 0, assemblyFee: 300, grandTotal: 300, currency: 'TWD' },
      activeShare: null,
      updatedAtUtc: new Date().toISOString(),
      rowVersion: 'AAAA',
    }
    mockCreateBuildList.mockRejectedValueOnce(new ApiError('unauthorized', { status: 401, code: 'unauthorized' }))
    mockCreateBuildList.mockResolvedValueOnce(savedBuild)

    const { wrapper, router } = await mountPage()
    const sessionStore = useSessionStore()
    sessionStore.status = 'anonymous'
    await wrapper.vm.$nextTick()

    // The guest clicks save while still anonymous — this is what actually marks the pending
    // resume (see markPendingBuildSaveResume in save()'s 401 handler), not the status flip alone.
    await wrapper.find('.new-build-page__actions button').trigger('click')
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('login'))
    expect(mockCreateBuildList).toHaveBeenCalledTimes(1)

    sessionStore.status = 'authenticated'
    await vi.waitFor(() => expect(mockCreateBuildList).toHaveBeenCalledTimes(2))
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/builds/build-1'))
    expect(mockClearGuestBuildDraft).toHaveBeenCalled()
  })

  /**
   * 組長 PR #35 round-2 review, P1-1: "session authenticated + a draft exists" alone used to be
   * enough to auto-import — which also fires for a shopper who is already logged in and simply
   * visits /builds/new directly (or switched accounts) with an unrelated draft still sitting in
   * localStorage. Nothing on this page load ever hit the 401 -> markPendingBuildSaveResume() path,
   * so there is no pending-resume marker to consume, and the draft must NOT be silently imported
   * into whatever account happens to be signed in.
   */
  it('does not auto-import a leftover draft for a shopper who is already logged in (no pending resume marker set)', async () => {
    mockLoadGuestBuildDraft.mockReturnValue({ name: '我的組裝', items: [draftItem] })

    const { wrapper } = await mountPage()
    const sessionStore = useSessionStore()
    sessionStore.status = 'authenticated'
    await wrapper.vm.$nextTick()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(mockCreateBuildList).not.toHaveBeenCalled()
  })

  it('does not attempt an auto-resume save when there is no draft (nothing to save)', async () => {
    mockLoadGuestBuildDraft.mockReturnValue({ name: '', items: [] })

    const { wrapper } = await mountPage()
    const sessionStore = useSessionStore()
    sessionStore.status = 'anonymous'
    await wrapper.vm.$nextTick()

    sessionStore.status = 'authenticated'
    await wrapper.vm.$nextTick()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(mockCreateBuildList).not.toHaveBeenCalled()
  })

  /**
   * 組長 PR #35 round-3 review, P1-2: mirrors EfCompatibilityCheckService.MergeAndValidateItems's
   * own bounds (1–20 items, 1–8 per SKU). BuildItemsEditor.vue's own picker/quantity input can no
   * longer produce an out-of-bounds value directly, but a guest draft loaded from localStorage
   * (written by an older build of this page, before this fix existed) still can — "儲存為我的清單"
   * must stay disabled and explain why rather than let a stale draft reach the backend and fail
   * there instead.
   */
  it('disables save and shows a validation error when a loaded draft item has an out-of-bounds quantity', async () => {
    mockLoadGuestBuildDraft.mockReturnValue({
      name: '我的組裝',
      items: [{ skuPublicId: 'sku-1', quantity: 9, name: '記憶體 測試品', categoryCode: 'MEMORY' }],
    })

    const { wrapper } = await mountPage()
    await wrapper.vm.$nextTick()

    const button = wrapper.find('.new-build-page__actions button')
    expect(button.attributes('disabled')).toBeDefined()
    expect(wrapper.find('.new-build-page__items-errors').text()).toContain('1–8')
  })

  it('disables save and shows a validation error when a loaded draft has more than 20 items', async () => {
    const items = Array.from({ length: 21 }, (_, index) => ({
      skuPublicId: `sku-${index}`, quantity: 1, name: `記憶體 測試品 ${index}`, categoryCode: 'MEMORY',
    }))
    mockLoadGuestBuildDraft.mockReturnValue({ name: '我的組裝', items })

    const { wrapper } = await mountPage()
    await wrapper.vm.$nextTick()

    const button = wrapper.find('.new-build-page__actions button')
    expect(button.attributes('disabled')).toBeDefined()
    expect(wrapper.find('.new-build-page__items-errors').text()).toContain('最多 20 項')
  })
})

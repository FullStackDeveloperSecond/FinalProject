import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
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

import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { ApiError } from '@doselect/web-shared/api'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSessionStore } from '../stores/session'
import type { BuildListDto, CompatibilityCheckDto } from '../features/builds/types'

const mockCreateBuildList = vi.fn()
const mockCheckCompatibility = vi.fn()

vi.mock('../features/builds/api', () => ({
  listBuildLists: vi.fn(),
  getBuildList: vi.fn(),
  createBuildList: (...args: unknown[]) => mockCreateBuildList(...args),
  updateBuildList: vi.fn(),
  deleteBuildList: vi.fn(),
  createBuildShare: vi.fn(),
  revokeBuildShare: vi.fn(),
  getSharedBuild: vi.fn(),
  addBuildToCart: vi.fn(),
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
  mockCreateBuildList.mockReset()
  mockCheckCompatibility.mockReset()
  mockCheckCompatibility.mockResolvedValue(compatibleResult)
  mockLoadGuestBuildDraft.mockReset()
  mockLoadGuestBuildDraft.mockReturnValue({ name: '', items: [] })
  mockSaveGuestBuildDraft.mockReset()
  mockClearGuestBuildDraft.mockReset()
  window.sessionStorage.clear()
})

describe('NewBuildPage', () => {
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
})

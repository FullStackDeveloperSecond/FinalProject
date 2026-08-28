import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import BuildItemsEditor from '../features/builds/components/BuildItemsEditor.vue'
import type { BuildListDto } from '../features/builds/types'

const mockGetBuildList = vi.fn()
const mockUpdateBuildList = vi.fn()
const mockDeleteBuildList = vi.fn()
const mockCreateBuildShare = vi.fn()
const mockRevokeBuildShare = vi.fn()
const mockAddBuildToCart = vi.fn()

vi.mock('../features/builds/api', () => ({
  listBuildLists: vi.fn(),
  getBuildList: (...args: unknown[]) => mockGetBuildList(...args),
  createBuildList: vi.fn(),
  updateBuildList: (...args: unknown[]) => mockUpdateBuildList(...args),
  deleteBuildList: (...args: unknown[]) => mockDeleteBuildList(...args),
  createBuildShare: (...args: unknown[]) => mockCreateBuildShare(...args),
  revokeBuildShare: (...args: unknown[]) => mockRevokeBuildShare(...args),
  getSharedBuild: vi.fn(),
  addBuildToCart: (...args: unknown[]) => mockAddBuildToCart(...args),
  checkCompatibility: vi.fn(),
}))

function baseBuild(overrides: Partial<BuildListDto> = {}): BuildListDto {
  return {
    publicId: 'build-1',
    name: '我的組裝',
    items: [
      {
        publicId: 'item-1', skuPublicId: 'sku-cpu', skuCode: 'CPU-1', name: '測試 CPU', categoryCode: 'CPU',
        quantity: 1, sortOrder: 0, unitPrice: 5000, lineTotal: 5000, availability: 'available',
      },
    ],
    compatibility: { overall: 'compatible', ruleSetVersion: 1, settingsVersion: 1, results: [] },
    totals: { merchandise: 5000, assemblyFee: 300, grandTotal: 5300, currency: 'TWD' },
    activeShare: null,
    updatedAtUtc: new Date().toISOString(),
    rowVersion: 'AAAA',
    ...overrides,
  }
}

async function mountPage() {
  const { default: BuildDetailPage } = await import('./BuildDetailPage.vue')
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/builds/:buildId', name: 'build-detail', component: BuildDetailPage, props: true },
      { path: '/account/builds', name: 'build-lists', component: { template: '<div />' } },
    ],
  })
  await router.push('/builds/build-1')
  await router.isReady()

  return mount(BuildDetailPage, {
    props: { buildId: 'build-1' },
    global: {
      plugins: [[VueQueryPlugin, { queryClient }], router],
      stubs: { BuildItemsEditor: true, CompatibilityFindingsList: true },
    },
  })
}

beforeEach(() => {
  mockGetBuildList.mockReset()
  mockUpdateBuildList.mockReset()
  mockDeleteBuildList.mockReset()
  mockCreateBuildShare.mockReset()
  mockRevokeBuildShare.mockReset()
  mockAddBuildToCart.mockReset()
})

describe('BuildDetailPage — add-to-cart Idempotency-Key (組長 PR #35 review, item 4)', () => {
  it('reuses the same Idempotency-Key when retrying after a failed add-to-cart call', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    mockAddBuildToCart.mockRejectedValueOnce(new Error('network error'))
    mockAddBuildToCart.mockResolvedValueOnce({})

    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    function findCartButton() {
      return wrapper.findAll('button').find((button) => button.text() === '加入購物車')!
    }

    await findCartButton().trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(1))
    // The button disables itself while the mutation is in flight; a click that lands before it
    // re-enables is a no-op in a real browser (and in jsdom), so wait for that first.
    await vi.waitFor(() => expect(findCartButton().attributes('disabled')).toBeUndefined())

    await findCartButton().trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(2))

    const [firstKey] = mockAddBuildToCart.mock.calls[0]!.slice(2)
    const [secondKey] = mockAddBuildToCart.mock.calls[1]!.slice(2)
    expect(firstKey).toBe(secondKey)
  })

  it('uses a fresh Idempotency-Key for a new attempt after a successful add-to-cart call', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    mockAddBuildToCart.mockResolvedValue({})

    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    function findCartButton() {
      return wrapper.findAll('button').find((button) => button.text() === '加入購物車')!
    }

    await findCartButton().trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(1))
    await vi.waitFor(() => expect(findCartButton().attributes('disabled')).toBeUndefined())

    await findCartButton().trigger('click')
    await vi.waitFor(() => expect(mockAddBuildToCart).toHaveBeenCalledTimes(2))

    const firstKey = mockAddBuildToCart.mock.calls[0]!.slice(2)[0]
    const secondKey = mockAddBuildToCart.mock.calls[1]!.slice(2)[0]
    expect(firstKey).not.toBe(secondKey)
  })
})

describe('BuildDetailPage — proactive add-to-cart disable (組長 PR #35 review, item 5)', () => {
  it('enables add-to-cart for a compatible build with all-available items', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    const cartButton = wrapper.findAll('button').find((button) => button.text() === '加入購物車')
    expect(cartButton!.attributes('disabled')).toBeUndefined()
  })

  it('disables add-to-cart and explains why for insufficientData (e.g. a missing required category)', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild({
      compatibility: { overall: 'insufficientData', ruleSetVersion: 1, settingsVersion: 1, results: [] },
    }))
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    const cartButton = wrapper.findAll('button').find((button) => button.text() === '加入購物車')
    expect(cartButton!.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('尚缺少必要元件')
  })

  it('disables add-to-cart when an item is unavailable, even if overall is compatible', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild({
      items: [
        {
          publicId: 'item-1', skuPublicId: 'sku-cpu', skuCode: 'CPU-1', name: '測試 CPU', categoryCode: 'CPU',
          quantity: 1, sortOrder: 0, unitPrice: 5000, lineTotal: 5000, availability: 'unavailable',
        },
      ],
    }))
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    const cartButton = wrapper.findAll('button').find((button) => button.text() === '加入購物車')
    expect(cartButton!.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('已下架或庫存不足')
  })

  it('disables add-to-cart when a finding has a disabled rule', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild({
      compatibility: {
        overall: 'warning', ruleSetVersion: 1, settingsVersion: 1,
        results: [{ ruleCode: 'CPU_SOCKET', severity: 'ruleDisabled', messageKey: 'x', subjectSkuPublicIds: [], facts: {} }],
      },
    }))
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    const cartButton = wrapper.findAll('button').find((button) => button.text() === '加入購物車')
    expect(cartButton!.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('已停用')
  })
})

describe('BuildDetailPage — unsaved-edits guard (組長 PR #35 round-2 review, P1-2)', () => {
  /**
   * The editor only ever mutates local name/items; share, add-to-cart, and the rendered
   * compatibility/price all still read `buildList.value` — the last-fetched *server* state. A
   * shopper who swapped a part but hasn't clicked "儲存變更" yet must not be able to share or
   * add-to-cart, since the backend would act on the old, still-stored combination, not what's
   * currently on screen.
   */
  it('disables share and add-to-cart while the name has an unsaved edit', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    const cartButton = () => wrapper.findAll('button').find((button) => button.text() === '加入購物車')!
    const shareButton = () => wrapper.findAll('button').find((button) => button.text() === '建立分享連結')!
    expect(cartButton().attributes('disabled')).toBeUndefined()
    expect(shareButton().attributes('disabled')).toBeUndefined()

    await wrapper.find('#build-detail-name').setValue('改過的名字')

    expect(cartButton().attributes('disabled')).toBeDefined()
    expect(shareButton().attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('您有尚未儲存的變更')
    expect(mockCreateBuildShare).not.toHaveBeenCalled()
    expect(mockAddBuildToCart).not.toHaveBeenCalled()
  })

  it('disables share and add-to-cart while the item list has an unsaved edit (a swapped SKU)', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    await wrapper.findComponent(BuildItemsEditor).vm.$emit('update:items', [
      { skuPublicId: 'sku-cpu-2', quantity: 1, name: '換過的 CPU', categoryCode: 'CPU' },
    ])
    await wrapper.vm.$nextTick()

    const cartButton = wrapper.findAll('button').find((button) => button.text() === '加入購物車')!
    expect(cartButton.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('您有尚未儲存的變更')
  })

  it('re-enables share and add-to-cart once the edit is saved, reflecting the new server state', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    await wrapper.find('#build-detail-name').setValue('改過的名字')
    expect(wrapper.findAll('button').find((button) => button.text() === '加入購物車')!.attributes('disabled')).toBeDefined()

    mockUpdateBuildList.mockResolvedValueOnce(baseBuild({ name: '改過的名字', rowVersion: 'BBBB' }))

    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存變更')!
    await saveButton.trigger('click')
    await vi.waitFor(() => expect(mockUpdateBuildList).toHaveBeenCalledTimes(1))

    await vi.waitFor(() => expect(wrapper.text()).not.toContain('您有尚未儲存的變更'))
    expect(wrapper.findAll('button').find((button) => button.text() === '加入購物車')!.attributes('disabled')).toBeUndefined()
    expect(wrapper.findAll('button').find((button) => button.text() === '建立分享連結')!.attributes('disabled')).toBeUndefined()
  })

  it('does not disable share/add-to-cart when there is no unsaved edit (clean load)', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    expect(wrapper.text()).not.toContain('您有尚未儲存的變更')
    expect(wrapper.findAll('button').find((button) => button.text() === '加入購物車')!.attributes('disabled')).toBeUndefined()
  })
})

describe('BuildDetailPage — share flow (組長 PR #35 review, item 3)', () => {
  it('shows the real openable URL immediately after creating a share', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    mockCreateBuildShare.mockResolvedValue({
      sharePublicId: 'share-1', url: 'http://localhost:5173/builds/shared/abc123', expiresAtUtc: null,
    })

    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    const shareButton = wrapper.findAll('button').find((button) => button.text() === '建立分享連結')
    await shareButton!.trigger('click')

    await vi.waitFor(() => expect(wrapper.text()).toContain('http://localhost:5173/builds/shared/abc123'))
  })

  /**
   * The backend only ever persists the share token's hash, so a share recovered from a prior
   * session (via BuildListDto.activeShare on reload) can never have its URL displayed again —
   * only that one is active, offering revoke/regenerate.
   */
  it('shows that a share is active (recovered from the server) without pretending to know its URL', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild({
      activeShare: { sharePublicId: 'share-1', expiresAtUtc: null },
    }))

    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    expect(wrapper.text()).toContain('目前有作用中的分享連結')
    expect(wrapper.text()).not.toContain('http')
    expect(wrapper.findAll('button').some((button) => button.text() === '撤銷分享')).toBe(true)
    expect(wrapper.findAll('button').some((button) => button.text() === '重新產生連結')).toBe(true)
  })

  it('clears the active-share state after a successful revoke', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild({
      activeShare: { sharePublicId: 'share-1', expiresAtUtc: null },
    }))
    mockRevokeBuildShare.mockResolvedValue(undefined)

    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('目前有作用中的分享連結'))

    // Revoking invalidates the detail query — the refetch must report no active share afterward.
    mockGetBuildList.mockResolvedValue(baseBuild({ activeShare: null }))
    const revokeButton = wrapper.findAll('button').find((button) => button.text() === '撤銷分享')
    await revokeButton!.trigger('click')

    await vi.waitFor(() => expect(wrapper.text()).not.toContain('目前有作用中的分享連結'))
    expect(wrapper.findAll('button').some((button) => button.text() === '建立分享連結')).toBe(true)
  })
})

describe('BuildDetailPage — item validation (組長 PR #35 round-3 review, P1-2)', () => {
  it('disables 儲存變更 and shows a validation error when the edited item list has an out-of-bounds quantity', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    await wrapper.findComponent(BuildItemsEditor).vm.$emit('update:items', [
      { skuPublicId: 'sku-cpu', quantity: 9, name: '測試 CPU', categoryCode: 'CPU' },
    ])
    await wrapper.vm.$nextTick()

    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存變更')!
    expect(saveButton.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('1–8')
    expect(mockUpdateBuildList).not.toHaveBeenCalled()
  })

  it('disables 儲存變更 when the edited item list has more than 20 items', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    const tooMany = Array.from({ length: 21 }, (_, index) => ({
      skuPublicId: `sku-${index}`, quantity: 1, name: `品項 ${index}`, categoryCode: 'MEMORY',
    }))
    await wrapper.findComponent(BuildItemsEditor).vm.$emit('update:items', tooMany)
    await wrapper.vm.$nextTick()

    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存變更')!
    expect(saveButton.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('最多 20 項')
  })
})

describe('BuildDetailPage — post-save baseline resync (組長 PR #35 round-3 review)', () => {
  /**
   * updateBuildList's own onSuccess already writes the mutation's response into the query cache
   * (see useBuilds.ts), so `buildList.value` is the server's *canonical* (possibly merged) item
   * list by the time mutateAsync resolves. save() must re-apply THAT into the local editor state,
   * not just clear the dirty flag against whatever was locally submitted — otherwise a save that
   * the backend silently normalized (e.g. merging two rows of the same SKU into one, per P1-2)
   * would leave the editor showing the pre-merge, now-stale local list while still claiming "no
   * unsaved changes".
   */
  it('resyncs local items to the server-merged response after a save, not the locally-submitted list', async () => {
    mockGetBuildList.mockResolvedValue(baseBuild())
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    // Locally, this looks like two separate memory rows of the same SKU (a state the current
    // BuildItemsEditor.vue can no longer produce on its own, but a stale draft or race could).
    await wrapper.findComponent(BuildItemsEditor).vm.$emit('update:items', [
      { skuPublicId: 'sku-cpu', quantity: 1, name: '測試 CPU', categoryCode: 'CPU' },
      { skuPublicId: 'sku-mem', quantity: 1, name: '測試記憶體', categoryCode: 'MEMORY' },
      { skuPublicId: 'sku-mem', quantity: 1, name: '測試記憶體', categoryCode: 'MEMORY' },
    ])
    await wrapper.vm.$nextTick()

    // The backend merges the two sku-mem rows into one with quantity 2.
    const mergedServerBuild = baseBuild({
      rowVersion: 'BBBB',
      items: [
        {
          publicId: 'item-1', skuPublicId: 'sku-cpu', skuCode: 'CPU-1', name: '測試 CPU', categoryCode: 'CPU',
          quantity: 1, sortOrder: 0, unitPrice: 5000, lineTotal: 5000, availability: 'available',
        },
        {
          publicId: 'item-2', skuPublicId: 'sku-mem', skuCode: 'MEM-1', name: '測試記憶體', categoryCode: 'MEMORY',
          quantity: 2, sortOrder: 1, unitPrice: 1000, lineTotal: 2000, availability: 'available',
        },
      ],
    })
    mockUpdateBuildList.mockResolvedValueOnce(mergedServerBuild)

    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存變更')!
    await saveButton.trigger('click')
    await vi.waitFor(() => expect(mockUpdateBuildList).toHaveBeenCalledTimes(1))

    // isDirty must read false against the server's merged list, not the pre-merge local one —
    // proving the local baseline was actually replaced with mergedServerBuild.items, not just
    // "accepted as clean" while still holding the old, now-incorrect two-row shape.
    await vi.waitFor(() => expect(wrapper.text()).not.toContain('您有尚未儲存的變更'))
    expect(wrapper.findComponent(BuildItemsEditor).props('items')).toEqual([
      { skuPublicId: 'sku-cpu', quantity: 1, name: '測試 CPU', categoryCode: 'CPU' },
      { skuPublicId: 'sku-mem', quantity: 2, name: '測試記憶體', categoryCode: 'MEMORY' },
    ])
  })
})

describe('BuildDetailPage — route param identity switch (組長 PR #35 round-3 review, P2-5)', () => {
  /**
   * Vue Router reuses this component instance across /builds/:buildId navigations on the same
   * route record — it does not unmount/remount just because :buildId changed (same precedent as
   * ProductDetailPage.vue's selectedSkuPublicId fix, PR #24 review). Simulated here the same way
   * the router itself would update this page: setProps({ buildId }) on the SAME wrapper instance.
   */
  it('clears unsaved-edit / conflict / share / cart state left over from a previous build when buildId changes', async () => {
    mockGetBuildList.mockImplementation((publicId: string) => Promise.resolve(
      publicId === 'build-1'
        ? baseBuild()
        : baseBuild({ publicId: 'build-2', name: '另一份清單', rowVersion: 'ZZZZ' }),
    ))
    const wrapper = await mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('我的組裝'))

    // Leave behind exactly the kind of local state P2-5 is worried about: an unsaved name edit
    // (dirty state) plus a bumped cart quantity.
    await wrapper.find('#build-detail-name').setValue('改過但沒存的名字')
    await wrapper.find('#cart-quantity').setValue(5)
    expect(wrapper.text()).toContain('您有尚未儲存的變更')

    await wrapper.setProps({ buildId: 'build-2' })
    await vi.waitFor(() => expect(wrapper.text()).toContain('另一份清單'))

    // The stale dirty-edit banner from build-1 must not leak into build-2's clean, freshly-loaded
    // state, and the cart quantity must not carry build-1's typed value into build-2's request.
    expect(wrapper.text()).not.toContain('您有尚未儲存的變更')
    expect((wrapper.find('#cart-quantity').element as HTMLInputElement).value).toBe('1')
    expect((wrapper.find('#build-detail-name').element as HTMLInputElement).value).toBe('另一份清單')
  })
})

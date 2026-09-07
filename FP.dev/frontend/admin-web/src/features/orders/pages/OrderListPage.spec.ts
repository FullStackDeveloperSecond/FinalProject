import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const mockFetchOrders = vi.fn()

vi.mock('../api', async () => {
  const actual = await vi.importActual<typeof import('../api')>('../api')
  return { ...actual, fetchAdminOrders: mockFetchOrders }
})

const { default: OrderListPage } = await import('./OrderListPage.vue')
const { useAdminAuthStore } = await import('../../auth/stores/useAdminAuthStore')
const { batchShipmentSelection, clearBatchShipmentSelection }
  = await import('../../shipping/batchSelection')

function order(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'order-1',
    orderNumber: 'DS0001',
    buyerType: 'Member',
    maskedBuyerEmail: 'm***@example.test',
    orderStatus: 'Confirmed',
    paymentStatus: 'Paid',
    fulfillmentStatus: 'Pending',
    assemblyStatus: 'NotRequired',
    orderRefundStatus: 'None',
    summaryStatus: 'awaitingShipment',
    badges: [],
    grandTotal: 1000,
    currency: 'TWD',
    shippingMethodCode: 'home-delivery',
    createdAtUtc: '2026-09-01T00:00:00Z',
    paidAtUtc: '2026-09-01T00:05:00Z',
    shippedAtUtc: null,
    deliveredAtUtc: null,
    completedAtUtc: null,
    rowVersion: 'RV1',
    ...overrides,
  }
}

function list(items: unknown[], overrides: Record<string, unknown> = {}) {
  return { items, nextCursor: null, hasMore: false, ...overrides }
}

function signIn(roles: string[]) {
  const auth = useAdminAuthStore()
  auth.session = {
    isAuthenticated: true,
    user: {
      publicId: 'admin-1',
      displayName: 'Ops',
      emailMasked: 'o***@example.test',
      emailVerified: true,
      locale: 'zh-TW',
      roles,
    },
    expiresAtUtc: null,
    requiresTwoFactor: false,
  }
}

async function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/orders', component: OrderListPage },
      { path: '/orders/:publicId', name: 'admin-order-detail', component: { template: '<div />' } },
      { path: '/shipping/batches', component: { template: '<div />' } },
    ],
  })
  await router.push('/orders')
  await router.isReady()
  const wrapper = mount(OrderListPage, {
    global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
  })
  await flushPromises()
  return { wrapper, router, queryClient }
}

describe('OrderListPage 批次出貨勾選', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    clearBatchShipmentSelection()
  })

  afterEach(() => {
    mockFetchOrders.mockReset()
  })

  it('carries the selected orders and their row versions to the batch page', async () => {
    signIn(['OrderManager'])
    mockFetchOrders.mockResolvedValue(list([order(), order({ publicId: 'order-2', orderNumber: 'DS0002', rowVersion: 'RV2' })]))
    const { wrapper, router } = await mountPage()

    await wrapper.findAll('tbody input[type="checkbox"]')[0].setValue(true)
    await wrapper.find('p button').trigger('click')
    await flushPromises()

    expect(batchShipmentSelection.value).toEqual([
      expect.objectContaining({ publicId: 'order-1', orderNumber: 'DS0001', rowVersion: 'RV1' }),
    ])
    expect(router.currentRoute.value.path).toBe('/shipping/batches')
  })

  /**
   * 已出貨或已取消的訂單送過去只會逐筆失敗。讓它勾不動，管理員在列表上就看得出來，不必送一趟
   * 才發現。
   */
  it('only lets orders that are still awaiting shipment be selected', async () => {
    signIn(['OrderManager'])
    mockFetchOrders.mockResolvedValue(list([
      order(),
      order({ publicId: 'order-2', orderNumber: 'DS0002', summaryStatus: 'shipped' }),
    ]))
    const { wrapper } = await mountPage()

    const boxes = wrapper.findAll('tbody input[type="checkbox"]')
    expect(boxes[0].attributes('disabled')).toBeUndefined()
    expect(boxes[1].attributes('disabled')).toBeDefined()
  })

  /**
   * 換篩選條件或換頁後畫面上換了一批訂單，勾選卻還留著上一份——按下去送出的就不是眼前這些。
   * 這與 placeholderData 那組 review 是同一個問題：身分變了，舊資料就不能繼續是可操作的。
   */
  it('drops the selection when the list itself changes', async () => {
    signIn(['OrderManager'])
    mockFetchOrders.mockResolvedValue(list([order()]))
    const { wrapper } = await mountPage()

    await wrapper.findAll('tbody input[type="checkbox"]')[0].setValue(true)
    expect(wrapper.text()).toContain('已選取 1 筆')

    mockFetchOrders.mockResolvedValue(list([order({ publicId: 'order-9', orderNumber: 'DS0009' })]))
    await wrapper.findAll('fieldset input[type="checkbox"]')[0].trigger('change')
    await flushPromises()

    expect(wrapper.text()).not.toContain('已選取')
  })

  it('hides the batch entry from roles that cannot ship', async () => {
    signIn(['CatalogManager'])
    mockFetchOrders.mockResolvedValue(list([order()]))
    const { wrapper } = await mountPage()

    expect(wrapper.findAll('tbody input[type="checkbox"]')).toHaveLength(0)
  })

  /**
   * 換頁（cursor）跟換篩選是同一類「畫面換了一批訂單」，也該清空——不是只有 fieldset 篩選勾選
   * 才算。這條跟上面「drops the selection when the list itself changes」互補，各自對應清空的
   * 兩種真正觸發來源。
   */
  it('drops the selection when the page changes', async () => {
    signIn(['OrderManager'])
    mockFetchOrders.mockResolvedValue(list([order()], { nextCursor: 'cursor-2', hasMore: true }))
    const { wrapper } = await mountPage()

    await wrapper.findAll('tbody input[type="checkbox"]')[0].setValue(true)
    expect(wrapper.text()).toContain('已選取 1 筆')

    mockFetchOrders.mockResolvedValue(list([order({ publicId: 'order-9', orderNumber: 'DS0009' })]))
    await wrapper.find('table + button').trigger('click')
    await flushPromises()

    expect(wrapper.text()).not.toContain('已選取')
  })

  /**
   * alex 2026-09-05 對 `bug-order-list-batch-selection-race.md` 的裁定：同一組篩選條件下的
   * 背景 refetch（例如視窗重新取得焦點）不是換頁，仍在畫面上、仍可批次出貨的勾選要保留，
   * 不能無條件清空——這是原本 `watch(() => data.value, ...)` 的 bug，會把管理員剛勾好的
   * 選取整批清掉。
   */
  it('keeps a still-valid selection across a same-filter background refetch', async () => {
    signIn(['OrderManager'])
    mockFetchOrders.mockResolvedValue(list([order()]))
    const { wrapper, queryClient } = await mountPage()

    await wrapper.findAll('tbody input[type="checkbox"]')[0].setValue(true)
    expect(wrapper.text()).toContain('已選取 1 筆')

    // 篩選條件沒變，只是同一個 query key 被背景重新整理（例如視窗重新取得焦點）——這裡刻意讓
    // 回傳內容跟上一次不同（多一筆新訂單），確保 `data.value` 真的換了參考、watcher 真的觸發，
    // 而不是被 TanStack Query 的 structural sharing 擋下來、兩種實作看起來都「沒事」。
    mockFetchOrders.mockResolvedValue(list([order(), order({ publicId: 'order-3', orderNumber: 'DS0003' })]))
    await queryClient.invalidateQueries({ queryKey: ['admin-orders', 'list'] })
    await flushPromises()

    expect(wrapper.text()).toContain('已選取 1 筆')
    expect((wrapper.findAll('tbody input[type="checkbox"]')[0].element as HTMLInputElement).checked).toBe(true)
  })

  /**
   * alex 裁定第 3 點：refetch 之後如果勾選的訂單已經不在畫面上，要從勾選集合移除，避免送出
   * 一份畫面上看不到的舊選取。
   */
  it('removes a selection whose order disappeared from a refetch, but keeps other still-valid selections', async () => {
    signIn(['OrderManager'])
    mockFetchOrders.mockResolvedValue(list([order(), order({ publicId: 'order-2', orderNumber: 'DS0002' })]))
    const { wrapper, queryClient } = await mountPage()

    await wrapper.findAll('tbody input[type="checkbox"]')[0].setValue(true)
    await wrapper.findAll('tbody input[type="checkbox"]')[1].setValue(true)
    expect(wrapper.text()).toContain('已選取 2 筆')

    // order-2 refetch 後不見了（例如剛好被別人排除在這組篩選之外），order-1 還在、還可以批次出貨。
    // 只清 order-2、留著 order-1，才是跟舊版「整批清空」真正不一樣的地方——單一勾選項目測不出這個差異。
    mockFetchOrders.mockResolvedValue(list([order()]))
    await queryClient.invalidateQueries({ queryKey: ['admin-orders', 'list'] })
    await flushPromises()

    expect(wrapper.text()).toContain('已選取 1 筆')
    expect((wrapper.findAll('tbody input[type="checkbox"]')[0].element as HTMLInputElement).checked).toBe(true)
  })

  /**
   * alex 裁定第 3 點的另一半：訂單還在畫面上，但 refetch 後狀態已經不是可批次出貨（例如剛好
   * 被別人出貨），也要從勾選集合移除。
   */
  it('removes a selection whose order became ineligible after a refetch, but keeps other still-valid selections', async () => {
    signIn(['OrderManager'])
    mockFetchOrders.mockResolvedValue(list([order(), order({ publicId: 'order-2', orderNumber: 'DS0002' })]))
    const { wrapper, queryClient } = await mountPage()

    await wrapper.findAll('tbody input[type="checkbox"]')[0].setValue(true)
    await wrapper.findAll('tbody input[type="checkbox"]')[1].setValue(true)
    expect(wrapper.text()).toContain('已選取 2 筆')

    // order-2 還在畫面上，但 refetch 後狀態已經不是可批次出貨（例如剛好被別人出貨）；order-1
    // 沒變、還留著才是跟舊版「整批清空」不一樣的地方。
    mockFetchOrders.mockResolvedValue(list([order(), order({ publicId: 'order-2', orderNumber: 'DS0002', summaryStatus: 'shipped' })]))
    await queryClient.invalidateQueries({ queryKey: ['admin-orders', 'list'] })
    await flushPromises()

    expect(wrapper.text()).toContain('已選取 1 筆')
    expect((wrapper.findAll('tbody input[type="checkbox"]')[0].element as HTMLInputElement).checked).toBe(true)
  })
})

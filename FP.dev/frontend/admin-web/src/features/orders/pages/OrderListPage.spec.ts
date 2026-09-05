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
  return { wrapper, router }
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
})

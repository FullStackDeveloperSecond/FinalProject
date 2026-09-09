import { ApiError } from '@doselect/web-shared/api'
import PrimeVue from 'primevue/config'
import { chinesePaginationLocale } from '@doselect/web-shared/theme'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { OrderPageDto, OrderSummaryDto } from './api'

const { fetchOrders } = vi.hoisted(() => ({ fetchOrders: vi.fn() }))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return { ...actual, fetchOrders }
})

const { default: OrderListPage } = await import('./OrderListPage.vue')

function order(overrides: Partial<OrderSummaryDto> = {}): OrderSummaryDto {
  return {
    publicId: '11111111-1111-4111-8111-111111111111',
    orderNumber: 'DS20260908001',
    orderStatus: 'completed',
    paymentStatus: 'paid',
    fulfillmentStatus: 'delivered',
    itemCount: 2,
    grandTotal: 3980,
    currency: 'TWD',
    createdAtUtc: '2026-09-08T08:00:00Z',
    availableActions: ['requestReturn'],
    ...overrides,
  }
}

function page(items: OrderSummaryDto[], pageNumber = 1, totalCount = items.length): OrderPageDto {
  return { items, pageNumber, pageSize: 10, totalCount, totalPages: Math.ceil(totalCount / 10) }
}

function mountPage() {
  return mount(OrderListPage, { global: { plugins: [[PrimeVue, { locale: chinesePaginationLocale }]], stubs: { RouterLink: RouterLinkStub } } })
}

describe('OrderListPage', () => {
  beforeEach(() => fetchOrders.mockReset())

  it('shows a loading state while the first page is pending', async () => {
    let resolvePage!: (value: OrderPageDto) => void
    fetchOrders.mockReturnValue(new Promise(resolve => { resolvePage = resolve }))
    const wrapper = mountPage()
    expect(wrapper.text()).toContain('訂單載入中')

    resolvePage(page([]))
    await flushPromises()
    wrapper.unmount()
  })

  it('shows the empty state', async () => {
    fetchOrders.mockResolvedValueOnce(page([]))
    const wrapper = mountPage()
    await flushPromises()
    expect(wrapper.text()).toContain('目前沒有訂單')
  })

  it('shows all customer-facing statuses and links to detail', async () => {
    fetchOrders.mockResolvedValueOnce(page([order()]))
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('DS20260908001')
    expect(wrapper.text()).toContain('已付款')
    expect(wrapper.text()).toContain('已出貨')
    expect(wrapper.text()).toContain('已送達')
    expect(wrapper.text()).toContain('已結單')
    expect(wrapper.getComponent(RouterLinkStub).props('to')).toEqual({
      name: 'order-detail',
      params: { orderId: '11111111-1111-4111-8111-111111111111' },
    })
  })

  it('retries after an initial API error', async () => {
    fetchOrders
      .mockRejectedValueOnce(new ApiError('暫時無法載入', { status: 503, code: 'unavailable' }))
      .mockResolvedValueOnce(page([order()]))
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('無法載入訂單')
    await wrapper.get('button').trigger('click')
    await flushPromises()

    expect(fetchOrders).toHaveBeenCalledTimes(2)
    expect(wrapper.text()).toContain('DS20260908001')
  })

  it('loads the next page without requiring an order UUID', async () => {
    fetchOrders
      .mockResolvedValueOnce(page([order()], 1, 11))
      .mockResolvedValueOnce(page([order({
        publicId: '22222222-2222-4222-8222-222222222222',
        orderNumber: 'DS20260908002',
      })], 2, 11))
    const wrapper = mountPage()
    await flushPromises()

    await wrapper.get('button[aria-label="第 2 頁"]').trigger('click')
    await flushPromises()

    expect(fetchOrders).toHaveBeenNthCalledWith(1, 1, 10)
    expect(fetchOrders).toHaveBeenNthCalledWith(2, 2, 10)
    expect(wrapper.text()).not.toContain('DS20260908001')
    expect(wrapper.text()).toContain('DS20260908002')
    expect(wrapper.get('[aria-current="page"]').text()).toBe('2')
  })
})

import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import OrderDetailPage from './OrderDetailPage.vue'
import type { OrderDto } from './api'

const { fetchOrder, cancelOrder } = vi.hoisted(() => ({
  fetchOrder: vi.fn(),
  cancelOrder: vi.fn(),
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return { ...actual, fetchOrder, cancelOrder }
})

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return { ...actual, useRoute: () => ({ params: { orderId: 'order-public-id' } }) }
})

function buildOrder(overrides: Partial<OrderDto> = {}): OrderDto {
  return {
    publicId: 'order-public-id',
    orderNumber: 'DS20260825001',
    orderStatus: 'PendingPayment',
    paymentStatus: 'Pending',
    fulfillmentStatus: 'Pending',
    assemblyStatus: 'NotRequired',
    orderRefundStatus: 'None',
    items: [
      {
        publicId: 'item-1',
        skuCodeSnapshot: 'SKU-1',
        productNameSnapshot: '機械鍵盤',
        skuNameSnapshot: '青軸',
        quantity: 1,
        finalUnitPrice: 1990,
        lineTotal: 1990,
        returnableQuantity: 0,
        returnedQuantity: 0,
      },
    ],
    recipient: { recipientName: '王小明', shippingMethodCode: 'home-delivery', storeName: null },
    amounts: {
      merchandiseSubtotal: 1990,
      itemDiscountTotal: 0,
      shippingFee: 0,
      assemblyFee: 0,
      grandTotal: 1990,
      paidAmount: 0,
      refundedAmount: 0,
      currency: 'TWD',
    },
    availableActions: ['cancel'],
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides,
  }
}

describe('OrderDetailPage', () => {
  beforeEach(() => {
    fetchOrder.mockReset()
    cancelOrder.mockReset()
  })

  it('renders order items once loaded', async () => {
    fetchOrder.mockResolvedValueOnce(buildOrder())
    const wrapper = mount(OrderDetailPage)
    await flushPromises()

    expect(wrapper.text()).toContain('DS20260825001')
    expect(wrapper.text()).toContain('機械鍵盤')
  })

  it('shows the 401 status page when the caller is not authenticated', async () => {
    fetchOrder.mockRejectedValueOnce(new ApiError('Unauthorized', {
      status: 401,
      code: 'authentication_required',
    }))
    const wrapper = mount(OrderDetailPage)
    await flushPromises()

    expect(wrapper.text()).toContain('需要登入')
  })

  it('shows the 404 status page when the order does not belong to the caller', async () => {
    fetchOrder.mockRejectedValueOnce(new ApiError('Not Found', {
      status: 404,
      code: 'resource_not_found',
    }))
    const wrapper = mount(OrderDetailPage)
    await flushPromises()

    expect(wrapper.text()).toContain('找不到頁面')
  })

  it('cancels the order after picking a reason', async () => {
    fetchOrder.mockResolvedValueOnce(buildOrder())
    cancelOrder.mockResolvedValueOnce(buildOrder({ orderStatus: 'Cancelled', availableActions: [] }))
    const wrapper = mount(OrderDetailPage)
    await flushPromises()

    await wrapper.get('button').trigger('click')
    await wrapper.get('#cancel-reason').setValue('changed_mind')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(cancelOrder).toHaveBeenCalledWith('order-public-id', {
      reasonCode: 'changed_mind',
      note: undefined,
      orderRowVersion: 'AAAAAAAAB9E=',
    })
    expect(wrapper.text()).toContain('已取消')
  })

  it('shows a friendly message when cancellation is no longer allowed', async () => {
    fetchOrder.mockResolvedValueOnce(buildOrder())
    cancelOrder.mockRejectedValueOnce(new ApiError('Conflict', {
      status: 409,
      code: 'order_cancellation_not_allowed',
    }))
    const wrapper = mount(OrderDetailPage)
    await flushPromises()

    await wrapper.get('button').trigger('click')
    await wrapper.get('#cancel-reason').setValue('changed_mind')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('這筆訂單目前的狀態已無法自助取消。')
  })

  it('offers the return entry once the order has returnable items', async () => {
    fetchOrder.mockResolvedValueOnce(buildOrder({
      orderStatus: 'Completed',
      fulfillmentStatus: 'Delivered',
      deliveredAtUtc: '2026-08-01T00:00:00Z',
      returnRequestDeadlineUtc: '2026-08-08T00:00:00Z',
      availableActions: ['requestReturn'],
      items: [
        {
          publicId: 'item-1',
          skuCodeSnapshot: 'SKU-1',
          productNameSnapshot: '機械鍵盤',
          skuNameSnapshot: '青軸',
          quantity: 1,
          finalUnitPrice: 1990,
          lineTotal: 1990,
          returnableQuantity: 1,
          returnedQuantity: 0,
        },
      ],
    }))
    const wrapper = mount(OrderDetailPage)
    await flushPromises()

    expect(wrapper.text()).toContain('可退 1 件')
  })
})

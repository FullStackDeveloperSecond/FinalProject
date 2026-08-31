import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import OrderDetailPage from './OrderDetailPage.vue'

const orderMocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')

  return {
    order: ref<Record<string, unknown> | null>(null),
    orderPending: ref(false),
    orderError: ref(false),
    orderFailure: ref<unknown>(null),
    refetchOrder: vi.fn(),
    recipient: ref<Record<string, unknown> | null>(null),
    recipientPending: ref(false),
    recipientError: ref(false),
    recipientFailure: ref<unknown>(null),
    refetchRecipient: vi.fn(),
    actionMutation: {
      isPending: ref(false),
      isError: ref(false),
      error: ref<unknown>(null),
      mutateAsync: vi.fn(),
    },
  }
})

vi.mock('../queries/useAdminOrders', () => ({
  useAdminOrderDetailQuery: () => ({
    data: orderMocks.order,
    isPending: orderMocks.orderPending,
    isError: orderMocks.orderError,
    error: orderMocks.orderFailure,
    refetch: orderMocks.refetchOrder,
  }),
  useAdminOrderRecipientQuery: () => ({
    data: orderMocks.recipient,
    isPending: orderMocks.recipientPending,
    isError: orderMocks.recipientError,
    error: orderMocks.recipientFailure,
    refetch: orderMocks.refetchRecipient,
  }),
  useAdminOrderActionMutation: () => orderMocks.actionMutation,
}))

const orderPublicId = '018f2e6a-0000-7000-8000-000000000030'

function sampleOrder() {
  return {
    publicId: orderPublicId,
    orderNumber: 'ORD-20260828-0001',
    orderStatus: 'Confirmed',
    paymentStatus: 'Paid',
    fulfillmentStatus: 'Pending',
    assemblyStatus: 'NotRequired',
    orderRefundStatus: 'None',
    summaryStatus: 'awaitingShipment',
    badges: [],
    buyerType: 'Member',
    maskedBuyerEmail: 'a***@example.com',
    shippingMethodCode: 'home-delivery',
    storeName: null,
    items: [],
    amounts: {
      merchandiseSubtotal: 0,
      itemDiscountTotal: 0,
      shippingFee: 0,
      assemblyFee: 0,
      grandTotal: 0,
      paidAmount: 0,
      refundedAmount: 0,
      currency: 'TWD',
    },
    statusHistory: [],
    availableActions: ['startProcessing'],
    createdAtUtc: '2026-08-28T01:00:00Z',
    rowVersion: 'AAAAAAAAAAE=',
  }
}

async function mountPage() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/orders', component: { template: '<div />' } },
      { path: '/orders/:publicId', component: { template: '<div />' } },
    ],
  })
  await router.push(`/orders/${orderPublicId}`)
  await router.isReady()

  return mount(OrderDetailPage, {
    global: { plugins: [router] },
  })
}

describe('OrderDetailPage recipient error/retry', () => {
  beforeEach(() => {
    orderMocks.order.value = sampleOrder()
    orderMocks.orderPending.value = false
    orderMocks.orderError.value = false
    orderMocks.orderFailure.value = null
    orderMocks.refetchOrder.mockReset()
    orderMocks.recipient.value = null
    orderMocks.recipientPending.value = false
    orderMocks.recipientError.value = false
    orderMocks.recipientFailure.value = null
    orderMocks.refetchRecipient.mockReset()
  })

  it('shows an ErrorState with a retry action instead of a blank area when the recipient fetch fails', async () => {
    orderMocks.recipientError.value = true
    orderMocks.recipientFailure.value = new ApiError('Internal error', {
      status: 500,
      code: 'internal_error',
      correlationId: 'corr-1',
      traceId: 'trace-1',
    })

    const wrapper = await mountPage()
    await wrapper.find('button').trigger('click') // 查看完整收件資料
    await flushPromises()

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('corr-1')
    expect(wrapper.text()).toContain('trace-1')
    // Not a blank area: the recipient <dl> must not have rendered instead.
    expect(wrapper.text()).not.toContain('收件人')

    const retryButton = wrapper
      .findAll('button')
      .find(button => button.text() === '重試')
    expect(retryButton).toBeDefined()
    await retryButton!.trigger('click')
    expect(orderMocks.refetchRecipient).toHaveBeenCalledTimes(1)
  })

  it('lets the admin collapse back to the initial state after a recipient fetch failure', async () => {
    orderMocks.recipientError.value = true

    const wrapper = await mountPage()
    await wrapper.find('button').trigger('click')
    await flushPromises()

    const backButton = wrapper.findAll('button').find(button => button.text() === '返回')
    expect(backButton).toBeDefined()
    await backButton!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('查看完整收件資料')
  })
})

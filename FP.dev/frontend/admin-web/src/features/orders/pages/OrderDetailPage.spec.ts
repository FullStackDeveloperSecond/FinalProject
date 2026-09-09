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
    shipmentMutation: {
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
  useShipmentStatusActionMutation: () => orderMocks.shipmentMutation,
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

  it.each([
    ['HomeDelivery', '一般宅配'], ['home-delivery', '一般宅配'],
    ['StorePickup', '超商取貨'], ['HomeDeliveryAssembly', '組裝電腦宅配'],
    ['future-method', '配送方式待確認'],
  ])('localizes shipping method %s', async (code, label) => {
    orderMocks.order.value = { ...sampleOrder(), shippingMethodCode: code }
    const wrapper = await mountPage()
    expect(wrapper.text()).toContain(label)
    expect(wrapper.text()).not.toContain(code)
  })

  it('renders order state values in Traditional Chinese', async () => {
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('已付款')
    expect(wrapper.text()).toContain('待處理')
    expect(wrapper.text()).toContain('不需組裝')
    expect(wrapper.text()).toContain('尚無退款')
    expect(wrapper.text()).not.toContain('Paid')
    expect(wrapper.text()).not.toContain('NotRequired')
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

describe('OrderDetailPage assembly progress', () => {
  beforeEach(() => {
    orderMocks.order.value = {
      ...sampleOrder(), assemblyStatus: 'Started', availableActions: [],
      assemblyJobs: [
        { publicId: 'job-first', status: 'ReadyToShip', rowVersion: 'first-version', availableActions: [] },
        { publicId: 'job-second', status: 'Started', rowVersion: 'second-version', availableActions: ['assemblyTesting', 'assemblyFailed'] },
      ],
    }
    orderMocks.orderPending.value = false
    orderMocks.orderError.value = false
    orderMocks.actionMutation.isPending.value = false
    orderMocks.actionMutation.mutateAsync.mockReset().mockResolvedValue(undefined)
  })

  it('confirms the selected job with both order and job versions, without exposing an unavailable shortcut', async () => {
    const wrapper = await mountPage()
    expect(wrapper.text()).not.toContain('測試通過，確認可出貨')
    const button = wrapper.findAll('button').find(item => item.text() === '組裝完成，開始測試')!
    await button.trigger('click')
    expect(orderMocks.actionMutation.mutateAsync).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('僅更新選取的這一台電腦')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(orderMocks.actionMutation.mutateAsync).toHaveBeenCalledExactlyOnceWith({
      publicId: orderPublicId, actionName: 'assemblyTesting',
      request: {
        reasonCode: undefined, note: undefined, rowVersion: 'AAAAAAAAAAE=',
        assemblyJobPublicId: 'job-second', assemblyJobRowVersion: 'second-version',
      },
    })
  })

  it('requires a failure reason and preserves the form on a concurrency conflict', async () => {
    orderMocks.actionMutation.mutateAsync.mockRejectedValue(new ApiError('conflict', { status: 409, code: 'concurrency_conflict' }))
    const wrapper = await mountPage()
    await wrapper.findAll('button').find(item => item.text() === '標記組裝／測試失敗')!.trigger('click')
    await wrapper.get('form').trigger('submit')
    expect(orderMocks.actionMutation.mutateAsync).not.toHaveBeenCalled()
    await wrapper.get('#action-reason').setValue('other')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('訂單資料已被更新')
    expect(wrapper.find('form').exists()).toBe(true)
  })
})

/** M-11 物流狀態命令（組長 2026-09-04 裁定 A1／C1）：按鈕只照後端 availableActions、必填原因、Idempotency-Key 沿用。 */
describe('OrderDetailPage shipment status commands', () => {
  function shippedOrder() {
    return {
      ...sampleOrder(),
      fulfillmentStatus: 'InTransit',
      shipment: {
        publicId: 'shipment-1',
        shipmentNumber: 'SH-0001',
        trackingNumber: 'TRK-0001',
        status: 'InTransit',
        shippingMethodCode: 'home-delivery',
        shippedAtUtc: '2026-09-04T01:00:00Z',
        deliveredAtUtc: null,
        history: [
          { fromStatus: 'Preparing', toStatus: 'Shipped', actorPublicId: null, occurredAtUtc: '2026-09-04T01:00:00Z' },
          { fromStatus: 'Shipped', toStatus: 'InTransit', actorPublicId: null, occurredAtUtc: '2026-09-04T02:00:00Z' },
        ],
        availableActions: ['delivered', 'delivery-failed'],
        rowVersion: 'AAAAAAAAABE=',
      },
    }
  }

  beforeEach(() => {
    orderMocks.order.value = shippedOrder()
    orderMocks.orderPending.value = false
    orderMocks.orderError.value = false
    orderMocks.orderFailure.value = null
    orderMocks.shipmentMutation.mutateAsync.mockReset()
    orderMocks.shipmentMutation.isPending.value = false
  })

  it('renders the shipment summary, history and only the backend-provided actions', async () => {
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('SH-0001')
    expect(wrapper.text()).toContain('TRK-0001')
    expect(wrapper.find('ul[aria-label="物流歷程"]').text()).toContain('已出貨 → 配送中')
    expect(wrapper.text()).toContain('一般宅配')
    expect(wrapper.text()).not.toContain('home-delivery')
    const labels = wrapper.findAll('button').map(button => button.text())
    expect(labels).toContain('宅配送達')
    expect(labels).toContain('配送失敗')
    expect(labels).not.toContain('超商到店')
  })

  it('submits the command with the shipment RowVersion and a generated Idempotency-Key', async () => {
    orderMocks.shipmentMutation.mutateAsync.mockResolvedValueOnce(shippedOrder())
    const wrapper = await mountPage()

    await wrapper.findAll('button').find(button => button.text() === '宅配送達')!.trigger('click')
    await wrapper.find('form[aria-label="物流狀態命令"]').trigger('submit')
    await flushPromises()

    expect(orderMocks.shipmentMutation.mutateAsync).toHaveBeenCalledTimes(1)
    const input = orderMocks.shipmentMutation.mutateAsync.mock.calls[0]![0] as {
      orderPublicId: string
      shipmentPublicId: string
      shipmentAction: string
      request: { shipmentRowVersion: string; reasonCode?: string; note?: string }
      idempotencyKey: string
    }
    expect(input.orderPublicId).toBe(orderPublicId)
    expect(input.shipmentPublicId).toBe('shipment-1')
    expect(input.shipmentAction).toBe('delivered')
    expect(input.request).toEqual({ shipmentRowVersion: 'AAAAAAAAABE=', reasonCode: undefined, note: undefined })
    expect(input.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/)
  })

  it('requires a reason for delivery-failed and reuses the same Idempotency-Key when retrying after a failure', async () => {
    orderMocks.shipmentMutation.mutateAsync
      .mockRejectedValueOnce(new ApiError('conflict', { status: 409, code: 'concurrency_conflict' }))
      .mockResolvedValueOnce(shippedOrder())
    const wrapper = await mountPage()

    await wrapper.findAll('button').find(button => button.text() === '配送失敗')!.trigger('click')
    const form = wrapper.find('form[aria-label="物流狀態命令"]')
    // 沒選原因不送。
    await form.trigger('submit')
    expect(orderMocks.shipmentMutation.mutateAsync).not.toHaveBeenCalled()

    await form.find('select').setValue('recipient_absent')
    await form.find('input').setValue('無人簽收')
    await form.trigger('submit')
    await flushPromises()
    expect(wrapper.find('[role="alert"]').text()).toContain('重新整理')

    await form.trigger('submit')
    await flushPromises()

    const calls = orderMocks.shipmentMutation.mutateAsync.mock.calls
    expect(calls).toHaveLength(2)
    const first = calls[0]![0] as { idempotencyKey: string; request: { reasonCode?: string; note?: string } }
    const second = calls[1]![0] as { idempotencyKey: string }
    expect(first.request).toEqual({ shipmentRowVersion: 'AAAAAAAAABE=', reasonCode: 'recipient_absent', note: '無人簽收' })
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
  })

  it('shows an empty state when the order has no shipment yet', async () => {
    orderMocks.order.value = { ...sampleOrder(), shipment: null }
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('尚未建立物流單')
    expect(wrapper.find('form[aria-label="物流狀態命令"]').exists()).toBe(false)
  })
})

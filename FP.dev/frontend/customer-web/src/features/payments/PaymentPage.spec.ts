import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PaymentPage from './PaymentPage.vue'
import type { OrderDto } from '../orders/api'
import type { PaymentAttemptDto } from './api'

const {
  fetchOrder,
  createPaymentAttempt,
  completeSimulatedPayment,
  fetchLatestPaymentAttempt,
} = vi.hoisted(() => ({
  fetchOrder: vi.fn(),
  createPaymentAttempt: vi.fn(),
  completeSimulatedPayment: vi.fn(),
  fetchLatestPaymentAttempt: vi.fn(),
}))

vi.mock('../orders/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../orders/api')>()
  return { ...actual, fetchOrder }
})

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return { ...actual, createPaymentAttempt, completeSimulatedPayment, fetchLatestPaymentAttempt }
})

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({ params: { orderId: 'order-public-id' } }),
  }
})

function buildOrder(overrides: Partial<OrderDto> = {}): OrderDto {
  return {
    publicId: 'order-public-id',
    orderNumber: 'DS20260901001',
    orderStatus: 'pendingPayment',
    paymentStatus: 'awaitingPayment',
    fulfillmentStatus: 'pending',
    assemblyStatus: 'notRequired',
    orderRefundStatus: 'none',
    items: [],
    recipient: { recipientName: '王小明', shippingMethodCode: 'home-delivery' },
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

function buildAttempt(overrides: Partial<PaymentAttemptDto> = {}): PaymentAttemptDto {
  return {
    publicId: 'attempt-public-id',
    method: 'creditCard',
    status: 'awaitingPayment',
    amount: 1990,
    currency: 'TWD',
    instruction: {
      type: 'redirect',
      maskedAccount: null,
      code: null,
      expiresAtUtc: '2026-09-01T08:15:00Z',
    },
    createdAtUtc: '2026-09-01T08:00:00Z',
    paidAtUtc: null,
    rowVersion: 'AAAAAAAAB9I=',
    ...overrides,
  }
}

describe('PaymentPage', () => {
  beforeEach(() => {
    fetchOrder.mockReset()
    createPaymentAttempt.mockReset()
    completeSimulatedPayment.mockReset()
    fetchLatestPaymentAttempt.mockReset()
    // 預設「這張訂單還沒有付款嘗試」，也就是恢復功能之前的行為。
    fetchLatestPaymentAttempt.mockResolvedValue(undefined)
  })

  it('creates a payment attempt from the trusted order version and shows its instruction', async () => {
    fetchOrder.mockResolvedValue(buildOrder())
    createPaymentAttempt.mockResolvedValue(buildAttempt({
      method: 'atm',
      instruction: {
        type: 'virtualAccount',
        maskedAccount: '***12345',
        code: null,
        expiresAtUtc: '2026-09-02T08:00:00Z',
      },
    }))
    const wrapper = mount(PaymentPage)
    await flushPromises()

    await wrapper.get('#payment-method').setValue('atm')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(createPaymentAttempt).toHaveBeenCalledWith(
      'order-public-id',
      { method: 'atm', orderRowVersion: 'AAAAAAAAB9E=' },
      expect.any(String),
    )
    expect(wrapper.text()).toContain('***12345')
  })

  it('reuses the same idempotency key when a failed create request is retried', async () => {
    fetchOrder.mockResolvedValue(buildOrder())
    createPaymentAttempt
      .mockRejectedValueOnce(new Error('network error'))
      .mockResolvedValueOnce(buildAttempt())
    const wrapper = mount(PaymentPage)
    await flushPromises()

    await wrapper.get('form').trigger('submit')
    await flushPromises()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(createPaymentAttempt).toHaveBeenCalledTimes(2)
    expect(createPaymentAttempt.mock.calls[0]![2]).toBe(createPaymentAttempt.mock.calls[1]![2])
  })

  it('completes a non-COD payment in Demo and refreshes the order truth', async () => {
    fetchOrder
      .mockResolvedValueOnce(buildOrder())
      .mockResolvedValueOnce(buildOrder({
        orderStatus: 'confirmed',
        paymentStatus: 'paid',
        amounts: { ...buildOrder().amounts, paidAmount: 1990 },
      }))
    createPaymentAttempt.mockResolvedValue(buildAttempt())
    completeSimulatedPayment.mockResolvedValue(buildAttempt({
      status: 'paid',
      paidAtUtc: '2026-09-01T08:01:00Z',
    }))
    const wrapper = mount(PaymentPage)
    await flushPromises()

    await wrapper.get('form').trigger('submit')
    await flushPromises()
    await wrapper.get('[data-test="complete-payment"]').trigger('click')
    await flushPromises()

    expect(completeSimulatedPayment).toHaveBeenCalledWith(
      'attempt-public-id',
      { outcome: 'succeeded', simulationKey: expect.any(String) },
    )
    expect(fetchOrder).toHaveBeenCalledTimes(2)
    expect(wrapper.text()).toContain('付款已完成')
  })

  it('explains when the simulated completion endpoint is disabled', async () => {
    fetchOrder.mockResolvedValue(buildOrder())
    createPaymentAttempt.mockResolvedValue(buildAttempt())
    completeSimulatedPayment.mockRejectedValue(new ApiError('Not Found', {
      status: 404,
      code: 'resource_not_found',
    }))
    const wrapper = mount(PaymentPage)
    await flushPromises()

    await wrapper.get('form').trigger('submit')
    await flushPromises()
    await wrapper.get('[data-test="complete-payment"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('目前環境未開放模擬付款完成')
  })

  it('does not offer the Demo completion action for cash on delivery', async () => {
    fetchOrder.mockResolvedValue(buildOrder())
    createPaymentAttempt.mockResolvedValue(buildAttempt({
      method: 'cashOnDelivery',
      status: 'pending',
      instruction: null,
    }))
    const wrapper = mount(PaymentPage)
    await flushPromises()

    await wrapper.get('#payment-method').setValue('cashOnDelivery')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('貨到付款會在完成配送或取貨時入帳')
    expect(wrapper.find('[data-test="complete-payment"]').exists()).toBe(false)
  })

  it('restores the previous attempt on load instead of showing an empty form', async () => {
    // 恢復功能的核心：重新整理後付款方式、金額、狀態與付款指示都還在。
    fetchOrder.mockResolvedValue(buildOrder())
    fetchLatestPaymentAttempt.mockResolvedValue(buildAttempt({
      method: 'atm',
      instruction: {
        type: 'virtualAccount',
        maskedAccount: null,
        code: '9556123456789',
        expiresAtUtc: '2026-09-02T08:00:00Z',
      },
    }))

    const wrapper = mount(PaymentPage)
    await flushPromises()

    expect(fetchLatestPaymentAttempt).toHaveBeenCalledWith('order-public-id')
    // ATM 代碼是使用者要拿去繳費的東西，正是最不能掉的欄位。
    expect(wrapper.text()).toContain('9556123456789')
    expect(wrapper.text()).toContain('等待付款')
    // 恢復不等於重新建立 —— 多建一筆會讓同一張訂單出現兩筆付款嘗試。
    expect(createPaymentAttempt).not.toHaveBeenCalled()
  })

  it('keeps showing the create form when there is no attempt yet', async () => {
    // 對照組。少了它，一個「永遠顯示已恢復畫面」的實作也會讓上面那條過。
    fetchOrder.mockResolvedValue(buildOrder())
    fetchLatestPaymentAttempt.mockResolvedValue(undefined)

    const wrapper = mount(PaymentPage)
    await flushPromises()

    expect(wrapper.find('#payment-method').exists()).toBe(true)
  })

  it('lets the shopper retry after a failed attempt without losing the failure', async () => {
    // Issue #86 A1：終態要保留，但仍可重試。只保留不給重試，等於重新整理之後
    // 就再也付不了款；只給重試不保留，使用者會不知道剛才發生什麼事。
    fetchOrder.mockResolvedValue(buildOrder())
    fetchLatestPaymentAttempt.mockResolvedValue(buildAttempt({ status: 'failed' }))

    const wrapper = mount(PaymentPage)
    await flushPromises()

    expect(wrapper.text()).toContain('付款失敗')
    expect(wrapper.find('#payment-method').exists()).toBe(true)
  })

  it('shows an expired attempt and still offers a retry', async () => {
    fetchOrder.mockResolvedValue(buildOrder())
    fetchLatestPaymentAttempt.mockResolvedValue(buildAttempt({ status: 'expired' }))

    const wrapper = mount(PaymentPage)
    await flushPromises()

    expect(wrapper.text()).toContain('已逾期')
    expect(wrapper.find('#payment-method').exists()).toBe(true)
  })

  it('offers no retry once the order is paid', async () => {
    // 已付款不該再出現建立表單。
    fetchOrder.mockResolvedValue(buildOrder({ paymentStatus: 'paid' }))
    fetchLatestPaymentAttempt.mockResolvedValue(buildAttempt({ status: 'paid' }))

    const wrapper = mount(PaymentPage)
    await flushPromises()

    expect(wrapper.find('#payment-method').exists()).toBe(false)
    expect(wrapper.text()).toContain('付款已完成')
  })

  it('still renders the page when restoring the attempt fails', async () => {
    // 恢復是加分功能，不該讓整頁掛掉 —— 訂單本身載入成功就要看得到付款流程。
    fetchOrder.mockResolvedValue(buildOrder())
    fetchLatestPaymentAttempt.mockRejectedValue(
      new ApiError('boom', { status: 500, code: 'unexpected_error' }))

    const wrapper = mount(PaymentPage)
    await flushPromises()

    expect(wrapper.text()).toContain('DS20260901001')
  })

  it('can retry loading the payment status after it failed', async () => {
    // 交接單 §5.3：查詢失敗的狀態要可以重試，不能只留一則死掉的訊息。
    fetchOrder.mockResolvedValue(buildOrder())
    fetchLatestPaymentAttempt
      .mockRejectedValueOnce(
        new ApiError('boom', { status: 500, code: 'unexpected_error' }))
      .mockResolvedValueOnce(buildAttempt({ status: 'awaitingPayment' }))

    const wrapper = mount(PaymentPage)
    await flushPromises()
    expect(wrapper.text()).toContain('無法載入先前的付款狀態')

    await wrapper.get('button[type="button"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).not.toContain('無法載入先前的付款狀態')
    expect(wrapper.text()).toContain('等待付款')
  })
})

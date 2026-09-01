import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PaymentPage from './PaymentPage.vue'
import type { OrderDto } from '../orders/api'
import type { PaymentAttemptDto } from './api'

const { fetchOrder, createPaymentAttempt, completeSimulatedPayment } = vi.hoisted(() => ({
  fetchOrder: vi.fn(),
  createPaymentAttempt: vi.fn(),
  completeSimulatedPayment: vi.fn(),
}))

vi.mock('../orders/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../orders/api')>()
  return { ...actual, fetchOrder }
})

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return { ...actual, createPaymentAttempt, completeSimulatedPayment }
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
})

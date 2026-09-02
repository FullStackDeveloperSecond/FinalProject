import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import { useSessionStore } from '../stores/session'
import type { CartDto, CartValidationDto } from '../features/cart/types'
import type {
  AcceptedPolicyVersions,
  CreateOrderRequest,
  OrderDto,
} from '../features/checkout/api'

const mockRevalidateCart = vi.fn<() => Promise<CartValidationDto>>()
const mockGetShippingOptions = vi.fn()
const mockGetCheckoutPolicyVersions = vi.fn<() => Promise<AcceptedPolicyVersions>>()
const mockCreateOrder = vi.fn<(
  body: CreateOrderRequest,
  idempotencyKey: string,
  guestCartKey?: string,
) => Promise<OrderDto>>()

vi.mock('../features/cart/api', () => ({
  getCart: vi.fn(),
  addCartItem: vi.fn(),
  updateCartItemQuantity: vi.fn(),
  removeCartItem: vi.fn(),
  removeCartAssemblyGroup: vi.fn(),
  revalidateCart: () => mockRevalidateCart(),
  mergeCartOnLogin: vi.fn(),
}))

vi.mock('../features/shipping/api', () => ({
  getShippingOptions: (guestCartKey?: string, couponCode?: string) =>
    mockGetShippingOptions(guestCartKey, couponCode),
  searchConvenienceStores: vi.fn(),
}))

vi.mock('../features/checkout/api', async (importOriginal) => {
  const original = await importOriginal<typeof import('../features/checkout/api')>()
  return {
    ...original,
    getCheckoutPolicyVersions: () => mockGetCheckoutPolicyVersions(),
    createOrder: (
      body: CreateOrderRequest,
      idempotencyKey: string,
      guestCartKey?: string,
    ) => mockCreateOrder(body, idempotencyKey, guestCartKey),
  }
})

vi.mock('../features/cart/guestCartKey', () => ({
  getOrCreateGuestCartKey: () => 'guest-checkout-key',
  clearGuestCartKey: vi.fn(),
}))

const cart: CartDto = {
  publicId: '11111111-1111-4111-8111-111111111111',
  items: [{
    publicId: 'item-1',
    skuPublicId: 'sku-1',
    skuCode: 'SKU-1',
    name: 'RTX 4070',
    quantity: 1,
    unitPrice: 18000,
    lineTotal: 18000,
    availability: 'available',
    priceChanged: false,
    maxPurchasableQuantity: 5,
    assemblyGroupKey: null,
    rowVersion: 'AAAA',
  }],
  coupon: null,
  amounts: {
    subtotal: 18000,
    itemDiscount: 0,
    couponDiscount: 0,
    shippingEstimate: null,
    assemblyFee: 0,
    totalEstimate: 18000,
    currency: 'TWD',
  },
  warnings: [],
  rowVersion: 'AAAA',
}

const policies: AcceptedPolicyVersions = { terms: 3, return: 2, privacy: 4 }

function readyValidation(): CartValidationDto {
  return { cart, isCheckoutReady: true, issues: [], validatedAtUtc: '2026-09-02T00:00:00Z' }
}

function shippingOptions(allowedPaymentMethods = ['creditCard', 'cashOnDelivery']) {
  return {
    cartPublicId: cart.publicId,
    options: [{
      methodCode: 'HOME_DELIVERY',
      name: '宅配',
      fee: 120,
      isEligible: true,
      ineligibleReasonCode: null,
      freeShippingThreshold: null,
      requiresAddress: true,
      requiresStore: false,
      allowedPaymentMethods,
    }],
    evaluatedAtUtc: '2026-09-02T00:00:00Z',
    cartRowVersion: cart.rowVersion,
  }
}

function createdOrder(status: 'pendingPayment' | 'confirmed' = 'pendingPayment'): OrderDto {
  return {
    publicId: '22222222-2222-4222-8222-222222222222',
    orderNumber: 'ORD-20260902-0001',
    orderStatus: status,
    paymentStatus: status === 'confirmed' ? 'awaitingPayment' : 'pending',
    fulfillmentStatus: 'pending',
    assemblyStatus: 'notRequired',
    orderRefundStatus: 'none',
    items: [],
    recipient: { recipientName: '王小明', shippingMethodCode: 'HOME_DELIVERY', storeName: null },
    amounts: {
      merchandiseSubtotal: 18000,
      itemDiscountTotal: 0,
      shippingFee: 120,
      assemblyFee: 0,
      grandTotal: 18120,
      paidAmount: 0,
      refundedAmount: 0,
      currency: 'TWD',
    },
    paymentDueAtUtc: null,
    confirmedAtUtc: null,
    paidAtUtc: null,
    shippedAtUtc: null,
    deliveredAtUtc: null,
    completedAtUtc: null,
    cancelledAtUtc: null,
    returnRequestDeadlineUtc: null,
    availableActions: [],
    rowVersion: 'BBBB',
  }
}

async function mountCheckoutPage(options: {
  authenticated?: boolean
  failNavigationTo?: 'order-detail' | 'order-payment'
} = {}) {
  const { default: CheckoutPage } = await import('./CheckoutPage.vue')
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/checkout', name: 'checkout', component: CheckoutPage },
      { path: '/cart', name: 'cart', component: { template: '<div>cart</div>' } },
      { path: '/orders/:orderId', name: 'order-detail', component: { template: '<div>order</div>' } },
      { path: '/orders/:orderId/payment', name: 'order-payment', component: { template: '<div>payment</div>' } },
    ],
  })
  await router.push('/checkout')
  await router.isReady()
  if (options.failNavigationTo) {
    vi.spyOn(router, 'push').mockRejectedValue(new Error('simulated route failure'))
  }

  const pinia = createPinia()
  setActivePinia(pinia)
  if (options.authenticated) {
    useSessionStore().status = 'authenticated'
    useSessionStore().user = {
      publicId: 'member-1',
      displayName: '測試會員',
      emailMasked: 'm***@example.com',
      emailVerified: true,
      locale: 'zh-TW',
    }
  } else {
    useSessionStore().status = 'anonymous'
  }
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return {
    wrapper: mount(CheckoutPage, {
      global: { plugins: [[VueQueryPlugin, { queryClient }], pinia, router] },
    }),
    router,
  }
}

async function fillValidHomeDeliveryForm(wrapper: Awaited<ReturnType<typeof mountCheckoutPage>>['wrapper']) {
  await wrapper.get('input[value="HOME_DELIVERY"]').trigger('change')
  await wrapper.get('#buyer-email').setValue('buyer@example.com')
  await wrapper.get('#buyer-name').setValue('王小明')
  await wrapper.get('#buyer-phone').setValue('0912345678')
  await wrapper.get('#recipient-name').setValue('王小明')
  await wrapper.get('#recipient-phone').setValue('0912345678')
  await wrapper.get('#postal-code').setValue('100')
  await wrapper.get('#city').setValue('臺北市')
  await wrapper.get('#district').setValue('中正區')
  await wrapper.get('#address-line1').setValue('忠孝西路一段 1 號')
  await wrapper.get('input[name="payment-method"][value="creditCard"]').trigger('change')
  await wrapper.get('#accept-terms').setValue(true)
  await wrapper.get('#accept-return').setValue(true)
  await wrapper.get('#accept-privacy').setValue(true)
}

beforeEach(() => {
  mockRevalidateCart.mockReset()
  mockGetShippingOptions.mockReset()
  mockGetCheckoutPolicyVersions.mockReset()
  mockCreateOrder.mockReset()
  mockRevalidateCart.mockResolvedValue(readyValidation())
  mockGetCheckoutPolicyVersions.mockResolvedValue(policies)
  mockGetShippingOptions.mockResolvedValue(shippingOptions())
})

describe('CheckoutPage', () => {
  it('keeps a loading state until the authoritative cart and policy versions resolve', async () => {
    mockRevalidateCart.mockReturnValue(new Promise(() => {}))
    mockGetCheckoutPolicyVersions.mockReturnValue(new Promise(() => {}))

    const { wrapper } = await mountCheckoutPage()

    expect(wrapper.text()).toContain('結帳資料載入中')
    expect(mockGetShippingOptions).not.toHaveBeenCalled()
  })

  it('only renders the concrete payment methods allowed by the selected backend option', async () => {
    mockGetShippingOptions.mockResolvedValue(shippingOptions(['creditCard', 'cashOnDelivery']))
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    await wrapper.get('input[value="HOME_DELIVERY"]').trigger('change')

    expect(wrapper.find('input[name="payment-method"][value="creditCard"]').exists()).toBe(true)
    expect(wrapper.find('input[name="payment-method"][value="cashOnDelivery"]').exists()).toBe(true)
    expect(wrapper.find('input[name="payment-method"][value="atm"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('prepaid')
  })

  it('re-evaluates shipping fees and COD from the backend after applying a coupon', async () => {
    mockGetShippingOptions.mockImplementation((_guestCartKey?: string, couponCode?: string) =>
      Promise.resolve(shippingOptions(
        couponCode === 'SAVE1000'
          ? ['creditCard', 'cashOnDelivery']
          : ['creditCard'],
      )))
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await wrapper.get('input[value="HOME_DELIVERY"]').trigger('change')
    expect(wrapper.find('input[name="payment-method"][value="cashOnDelivery"]').exists()).toBe(false)

    await wrapper.get('#coupon-code').setValue(' save1000 ')
    await wrapper.get('[data-test="apply-coupon"]').trigger('click')

    await vi.waitFor(() => expect(mockGetShippingOptions).toHaveBeenLastCalledWith(
      'guest-checkout-key',
      'SAVE1000',
    ))
    await vi.waitFor(() => expect(
      wrapper.find('input[name="payment-method"][value="cashOnDelivery"]').exists(),
    ).toBe(true))
    expect(wrapper.text()).toContain('已套用 SAVE1000')
  })

  it('submits identifiers and shopper input without client prices, then routes prepaid orders to payment', async () => {
    mockCreateOrder.mockResolvedValue(createdOrder())
    const { wrapper, router } = await mountCheckoutPage({ authenticated: true })
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await fillValidHomeDeliveryForm(wrapper)

    await wrapper.get('.checkout-page__submit').trigger('submit')
    await vi.waitFor(() => expect(mockCreateOrder).toHaveBeenCalledTimes(1))

    const [body, idempotencyKey, guestKey] = mockCreateOrder.mock.calls[0]!
    expect(body).toEqual(expect.objectContaining({
      cartPublicId: cart.publicId,
      cartRowVersion: cart.rowVersion,
      paymentMethod: 'creditCard',
      acceptPolicyVersions: policies,
    }))
    expect(JSON.stringify(body)).not.toMatch(/price|amount|fee|total/i)
    expect(idempotencyKey).toBeTruthy()
    expect(guestKey).toBe('guest-checkout-key')
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('order-payment'))
  })

  it('reuses the same idempotency key when the same failed submission is retried', async () => {
    mockCreateOrder
      .mockRejectedValueOnce(new ApiError('temporary', { status: 503, code: 'service_unavailable' }))
      .mockResolvedValueOnce(createdOrder())
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await fillValidHomeDeliveryForm(wrapper)

    await wrapper.get('.checkout-page__submit').trigger('submit')
    await vi.waitFor(() => expect(wrapper.text()).toContain('訂單建立失敗'))
    await wrapper.get('.checkout-page__submit').trigger('submit')
    await vi.waitFor(() => expect(mockCreateOrder).toHaveBeenCalledTimes(2))

    expect(mockCreateOrder.mock.calls[1]?.[1]).toBe(mockCreateOrder.mock.calls[0]?.[1])
  })

  it('routes a successful cash-on-delivery order directly to order detail', async () => {
    mockCreateOrder.mockResolvedValue(createdOrder('confirmed'))
    const { wrapper, router } = await mountCheckoutPage({ authenticated: true })
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await fillValidHomeDeliveryForm(wrapper)
    await wrapper.get('input[name="payment-method"][value="cashOnDelivery"]').trigger('change')

    await wrapper.get('.checkout-page__submit').trigger('submit')

    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('order-detail'))
  })

  it.each([
    {
      label: 'prepaid',
      orderStatus: 'pendingPayment' as const,
      paymentMethod: 'creditCard',
      targetRoute: 'order-payment' as const,
      recoveryHref: '/orders/22222222-2222-4222-8222-222222222222/payment',
      recoveryLabel: '前往付款',
    },
    {
      label: 'cash on delivery',
      orderStatus: 'confirmed' as const,
      paymentMethod: 'cashOnDelivery',
      targetRoute: 'order-detail' as const,
      recoveryHref: '/orders/22222222-2222-4222-8222-222222222222',
      recoveryLabel: '查看訂單',
    },
  ])('keeps the created $label order recoverable when navigation fails', async ({
    orderStatus,
    paymentMethod,
    targetRoute,
    recoveryHref,
    recoveryLabel,
  }) => {
    mockCreateOrder.mockResolvedValue(createdOrder(orderStatus))
    const { wrapper, router } = await mountCheckoutPage({
      authenticated: true,
      failNavigationTo: targetRoute,
    })
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await fillValidHomeDeliveryForm(wrapper)
    if (paymentMethod === 'cashOnDelivery') {
      await wrapper.get('input[name="payment-method"][value="cashOnDelivery"]').trigger('change')
    }

    await wrapper.get('.checkout-page__submit').trigger('submit')

    await vi.waitFor(() => expect(wrapper.text()).toContain('ORD-20260902-0001'))
    expect(wrapper.text()).toContain('訂單已經建立成功')
    expect(wrapper.text()).not.toContain('訂單建立失敗')
    expect(wrapper.get(`a[href="${recoveryHref}"]`).text()).toContain(recoveryLabel)
    expect(mockCreateOrder).toHaveBeenCalledTimes(1)
    expect(router.currentRoute.value.name).toBe('checkout')
  })

  it('keeps a guest on a success handoff with the order number and verification entry point', async () => {
    mockCreateOrder.mockResolvedValue(createdOrder())
    const { wrapper, router } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await fillValidHomeDeliveryForm(wrapper)

    await wrapper.get('.checkout-page__submit').trigger('submit')

    await vi.waitFor(() => expect(wrapper.text()).toContain('ORD-20260902-0001'))
    expect(wrapper.text()).toContain('驗證訂單後繼續付款')
    expect(wrapper.get('a[href="/guest-orders/access"]').attributes('href')).toBe('/guest-orders/access')
    expect(router.currentRoute.value.name).toBe('checkout')
  })
})

import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
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
  PaymentMethod,
} from '../features/checkout/api'

const mockRevalidateCart = vi.fn<() => Promise<CartValidationDto>>()
const mockGetShippingOptions = vi.fn()
const mockGetCheckoutPolicyVersions = vi.fn<() => Promise<AcceptedPolicyVersions>>()
const mockCreateOrder = vi.fn<(
  body: CreateOrderRequest,
  idempotencyKey: string,
  guestCartKey?: string,
) => Promise<OrderDto>>()
const mockGetGuestEmailStatus = vi.fn()
const mockRequestGuestEmailVerification = vi.fn()
const mockVerifyGuestEmail = vi.fn()
const mockFetchProfile = vi.fn()
const mockFetchAddresses = vi.fn()

vi.mock('../features/members/api', () => ({
  fetchProfile: () => mockFetchProfile(),
  fetchAddresses: () => mockFetchAddresses(),
}))

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
    getGuestCheckoutEmailVerificationStatus: () => mockGetGuestEmailStatus(),
    requestGuestCheckoutEmailVerification: (email: string, guestCartKey: string) =>
      mockRequestGuestEmailVerification(email, guestCartKey),
    verifyGuestCheckoutEmail: (requestPublicId: string, code: string, guestCartKey: string) =>
      mockVerifyGuestEmail(requestPublicId, code, guestCartKey),
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
      amounts: {
        merchandiseSubtotal: 18000,
        itemDiscountTotal: 0,
        shippingFee: 120,
        assemblyFee: 0,
        grandTotal: 18120,
        currency: 'TWD',
      },
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
  queryClient?: QueryClient
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
  const queryClient = options.queryClient
    ?? new QueryClient({ defaultOptions: { queries: { retry: false } } })

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

/** C-13 已套用優惠碼的購物車快取內容。 */
function withCoupon(code: string): CartDto {
  return {
    ...cart,
    coupon: {
      code,
      discountAmount: 0,
      isFreeShipping: true,
      isAssemblyFreeShipping: false,
    },
  }
}

/** 讓測試自己決定 revalidate 與政策版本的先後與成敗。 */
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

/**
 * 在<b>已掛載的同一個</b> CheckoutPage 上切換身分。
 *
 * 重新 mount 只能證明初始讀取有沒有隔離；真正會出事的是掛載期間登入、登出或換帳號，
 * 因為那時候代碼已經在元件 state 裡了。
 */
async function signInAs(publicId: string): Promise<void> {
  const sessionStore = useSessionStore()
  sessionStore.status = 'authenticated'
  sessionStore.user = {
    publicId,
    displayName: '測試會員',
    emailMasked: 'm***@example.com',
    emailVerified: true,
    locale: 'zh-TW',
  }
  await nextTick()
}

beforeEach(() => {
  mockFetchProfile.mockReset().mockResolvedValue({ displayName: '測試會員', phone: null })
  mockFetchAddresses.mockReset().mockResolvedValue([])
  mockRevalidateCart.mockReset()
  mockGetShippingOptions.mockReset()
  mockGetCheckoutPolicyVersions.mockReset()
  mockCreateOrder.mockReset()
  mockGetGuestEmailStatus.mockReset()
  mockRequestGuestEmailVerification.mockReset()
  mockVerifyGuestEmail.mockReset()
  mockRevalidateCart.mockResolvedValue(readyValidation())
  mockGetCheckoutPolicyVersions.mockResolvedValue(policies)
  mockGetGuestEmailStatus.mockResolvedValue({
    verified: true,
    email: 'buyer@example.com',
    expiresAtUtc: '2026-09-09T12:10:00Z',
  })
  mockGetShippingOptions.mockResolvedValue(shippingOptions())
})

describe('CheckoutPage', () => {
  it('immediately explains invalid mobile/email/postal inputs and clears corrected messages', async () => {
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.find('#buyer-phone').exists()).toBe(true))
    await fillValidHomeDeliveryForm(wrapper)
    await wrapper.get('#buyer-phone').setValue('0000')
    expect(wrapper.text()).toContain('手機號碼輸入錯誤，請輸入 09 開頭的 10 位數字')
    expect(wrapper.get('#buyer-phone').attributes('aria-invalid')).toBe('true')
    expect(wrapper.get('#buyer-phone').attributes('aria-describedby')).toBe('buyer-phone-error')
    expect(wrapper.get('#buyer-phone-error').element.previousElementSibling?.id).toBe('buyer-phone')
    expect(wrapper.findAll('#buyer-phone-error')).toHaveLength(1)
    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()
    await wrapper.get('#buyer-phone').setValue('0912345678')
    expect(wrapper.text()).not.toContain('手機號碼輸入錯誤')
    await wrapper.get('#buyer-email').setValue('wrong@')
    expect(wrapper.text()).toContain('電子郵件格式不正確')
    await wrapper.get('#postal-code').setValue('12')
    expect(wrapper.text()).toContain('郵遞區號請輸入 3、5 或 6 位數字')
    await wrapper.get('#buyer-name').setValue('')
    expect(wrapper.text()).toContain('姓名為必填')
    expect(wrapper.text()).toContain('地址補充（選填）')
    expect(wrapper.text()).toContain('配送備註（選填）')
    wrapper.unmount()
  })
  it('opens each Demo policy without accepting it, submitting, or clearing the form', async () => {
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.find('#accept-terms').exists()).toBe(true))
    await wrapper.get('#buyer-name').setValue('測試姓名')
    const dialog = wrapper.get('dialog').element as HTMLDialogElement
    // jsdom has no top-layer implementation; browser focus/ESC behavior is a separate check.
    dialog.showModal = vi.fn(() => dialog.setAttribute('open', ''))
    dialog.close = vi.fn(() => dialog.removeAttribute('open'))
    for (const [kind, title] of [['terms', '服務條款'], ['return', '退換貨政策'], ['privacy', '隱私權政策']]) {
      await wrapper.get(`[data-test="read-${kind}"]`).trigger('click')
      expect(dialog.open).toBe(true)
      expect(wrapper.get('dialog h1').text()).toBe(title)
      expect(wrapper.get('dialog').text()).toContain('Demo 引導範例')
      expect((wrapper.get(`#accept-${kind}`).element as HTMLInputElement).checked).toBe(false)
      await wrapper.get('[data-test="close-policy"]').trigger('click')
      expect(dialog.open).toBe(false)
      expect((wrapper.get('#buyer-name').element as HTMLInputElement).value).toBe('測試姓名')
    }
    expect(mockCreateOrder).not.toHaveBeenCalled()
  })
  it('prefills the saved default address for members and clears it when identity changes', async () => {
    mockFetchAddresses.mockResolvedValue([{
      publicId: 'address-1', label: '住家', isDefault: true,
      recipientName: '合成收件人', phone: '0912345678', postalCode: '100',
      city: '臺北市', district: '中正區', addressLine1: '測試路一號', addressLine2: null,
    }])
    const { wrapper } = await mountCheckoutPage({ authenticated: true })
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await wrapper.get('input[value="HOME_DELIVERY"]').trigger('change')
    await vi.waitFor(() => expect((wrapper.get('#recipient-name').element as HTMLInputElement).value).toBe('合成收件人'))
    expect((wrapper.get('#city').element as HTMLSelectElement).value).toBe('臺北市')
    useSessionStore().status = 'anonymous'
    useSessionStore().user = undefined
    await nextTick()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await wrapper.get('input[value="HOME_DELIVERY"]').trigger('change')
    expect((wrapper.get('#recipient-name').element as HTMLInputElement).value).toBe('')
  })

  it('rejects malformed phone, postal code and district before order submission', async () => {
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await fillValidHomeDeliveryForm(wrapper)
    for (const [selector, invalid, valid] of [
      ['#buyer-phone', 'abcdefghi', '0912345678'],
      ['#recipient-phone', '123456', '0912345678'],
      ['#postal-code', 'ABCDE', '100'],
      ['#district', '123', '中正區'],
    ]) {
      await wrapper.get(selector!).setValue(invalid)
      await wrapper.get('form').trigger('submit')
      expect(mockCreateOrder).not.toHaveBeenCalled()
      await wrapper.get(selector!).setValue(valid)
    }
    expect(wrapper.text()).toContain('收件手機號碼 *')
  })

  it('restores the recent receipt on return to an empty checkout within the same session', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    mockCreateOrder.mockResolvedValue(createdOrder())
    const first = await mountCheckoutPage({ queryClient })
    await vi.waitFor(() => expect(first.wrapper.text()).toContain('宅配'))
    await fillValidHomeDeliveryForm(first.wrapper)
    await first.wrapper.get('form').trigger('submit')
    await vi.waitFor(() => expect(mockCreateOrder).toHaveBeenCalledOnce())
    first.wrapper.unmount()
    mockRevalidateCart.mockResolvedValue({ ...readyValidation(), cart: { ...cart, items: [] } })
    const second = await mountCheckoutPage({ queryClient })
    await vi.waitFor(() => expect(second.wrapper.text()).toContain('ORD-20260902-0001'))
    expect(second.wrapper.text()).not.toContain('購物車是空的')
  })

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
    expect(wrapper.text()).toContain('商品小計：NT$18,000')
    expect(wrapper.text()).toContain('應付總額：NT$18,120')
    expect(wrapper.text()).not.toContain('最終金額、折扣、運費及庫存以後端建立訂單時重新計算為準')
  })

  it('sorts every payment method consistently and shows its payment deadline', async () => {
    const apiOrder: PaymentMethod[] = [
      'cashOnDelivery',
      'googlePay',
      'convenienceCode',
      'atm',
      'applePay',
      'linePay',
      'creditCard',
    ]
    mockGetShippingOptions.mockResolvedValue(shippingOptions(apiOrder))
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await wrapper.get('input[value="HOME_DELIVERY"]').trigger('change')

    const choices = wrapper
      .get('section[aria-labelledby="payment-title"]')
      .findAll('.checkout-page__choice')
    expect(choices.map(choice => choice.get('input').attributes('value'))).toEqual([
      'creditCard',
      'linePay',
      'applePay',
      'googlePay',
      'atm',
      'convenienceCode',
      'cashOnDelivery',
    ])
    expect(choices).toHaveLength(7)
    expect(choices.every(choice => choice.text().includes('付款期限：'))).toBe(true)
    expect(choices[0]!.text()).toContain('建立付款後 15 分鐘內')
    expect(choices[4]!.text()).toContain('建立付款後 3 天內')
    expect(choices[6]!.text()).toContain('收貨或取貨時')
  })

  it('does not provide a second coupon entry point on checkout', async () => {
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    expect(wrapper.find('#coupon-code').exists()).toBe(false)
    expect(wrapper.find('[data-test="apply-coupon"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('請返回購物車輸入')
    expect(wrapper.text()).not.toContain('SCHOOL2026')
  })

  it('limits districts to the selected city and clears an incompatible previous choice', async () => {
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await wrapper.get('input[value="HOME_DELIVERY"]').trigger('change')

    await wrapper.get('#city').setValue('臺北市')
    expect(wrapper.get('#district').text()).toContain('中正區')
    expect(wrapper.get('#district').text()).not.toContain('新店區')
    await wrapper.get('#district').setValue('中正區')

    await wrapper.get('#city').setValue('新北市')
    expect((wrapper.get('#district').element as HTMLSelectElement).value).toBe('')
    expect(wrapper.get('#district').text()).toContain('新店區')
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

  it('routes a verified guest directly to payment instead of the guest-order lookup page', async () => {
    mockCreateOrder.mockResolvedValue(createdOrder())
    const { wrapper, router } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await fillValidHomeDeliveryForm(wrapper)

    await wrapper.get('.checkout-page__submit').trigger('submit')

    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('order-payment'))
    expect(mockCreateOrder).toHaveBeenCalledTimes(1)
  })

  it('blocks guest submission until the entered email has been verified', async () => {
    mockGetGuestEmailStatus.mockResolvedValue({ verified: false, email: null, expiresAtUtc: null })
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await fillValidHomeDeliveryForm(wrapper)

    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()
    expect(mockCreateOrder).not.toHaveBeenCalled()
  })

  it('carries a coupon applied on the cart page into checkout and re-quotes shipping with it', async () => {
    // alex #97 A1：同一 SPA、同一身分、同一有效版本才沿用；沿用的只是代碼，
    // 運費仍由後端用那個代碼重算。
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(['cart', 'guest', 'guest-checkout-key'], {
      ...cart,
      coupon: { code: 'FREESHIP', discountAmount: 0, isFreeShipping: true, isAssemblyFreeShipping: false },
    })

    const { wrapper } = await mountCheckoutPage({ queryClient })
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    expect(wrapper.find('#coupon-code').exists()).toBe(false)
    expect(wrapper.text()).toContain('帶入優惠碼 FREESHIP')
    expect(mockGetShippingOptions).toHaveBeenCalledWith('guest-checkout-key', 'FREESHIP')
  })

  it('does not carry a coupon whose cart version no longer matches', async () => {
    // 版本變了代表原本的 quote 已經失效 —— 沿用會讓顧客看到一個算不出來的折扣。
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(['cart', 'guest', 'guest-checkout-key'], {
      ...cart,
      rowVersion: 'STALE',
      coupon: { code: 'FREESHIP', discountAmount: 0, isFreeShipping: true, isAssemblyFreeShipping: false },
    })

    const { wrapper } = await mountCheckoutPage({ queryClient })
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    expect(wrapper.find('#coupon-code').exists()).toBe(false)
    expect(wrapper.text()).toContain('未套用優惠券')
    expect(mockGetShippingOptions).toHaveBeenCalledWith('guest-checkout-key', undefined)
  })

  it('does not carry a coupon when there is no in-memory cart (a fresh page load)', async () => {
    // 重新整理與跨裝置都是這一條：記憶體快取不存在，就不沿用。
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    expect(wrapper.find('#coupon-code').exists()).toBe(false)
    expect(mockGetShippingOptions).toHaveBeenCalledWith('guest-checkout-key', undefined)
  })

  it('does not read another identity\'s cached coupon', async () => {
    // 快取鍵含身分：會員讀不到訪客那份，換帳號也讀不到上一個人的。
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(['cart', 'guest', 'guest-checkout-key'], {
      ...cart,
      coupon: { code: 'FREESHIP', discountAmount: 0, isFreeShipping: true, isAssemblyFreeShipping: false },
    })

    const { wrapper } = await mountCheckoutPage({ authenticated: true, queryClient })
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    expect(wrapper.find('#coupon-code').exists()).toBe(false)
  })


  it('drops the previous identity\'s coupon when the identity changes on the mounted page', async () => {
    // alex #97 第二輪第 1 點：身分化的 query cache 只隔離得了快取，隔離不了已經複製到
    // 元件 state 的那一份。訪客套券進 C-14 之後在同一個掛載中登入，舊代碼不能繼續被
    // 送進 Shipping Options，更不能進建單請求。
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(['cart', 'guest', 'guest-checkout-key'], withCoupon('FREESHIP'))

    const { wrapper } = await mountCheckoutPage({ queryClient })
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    expect(wrapper.text()).toContain('帶入優惠碼 FREESHIP')

    mockGetShippingOptions.mockClear()
    await signInAs('member-1')
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    expect(wrapper.text()).toContain('未套用優惠券')
    expect(mockGetShippingOptions).not.toHaveBeenCalledWith(expect.anything(), 'FREESHIP')
  })

  it('still carries the coupon when the first load fails halfway and the customer retries', async () => {
    // alex #97 第二輪第 2 點：revalidate 成功會把不帶優惠碼的權威購物車寫回同一個快取鍵，
    // 而政策版本失敗會讓整個 Promise.all 進錯誤分支。候選值若只活在單次 loadCheckout 裡，
    // 重試時讀到的就是那份無券購物車，同身分同版本也沿用不了。
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(['cart', 'guest', 'guest-checkout-key'], withCoupon('FREESHIP'))

    const revalidated = deferred<CartValidationDto>()
    const policiesFailed = deferred<AcceptedPolicyVersions>()
    mockRevalidateCart.mockReturnValueOnce(revalidated.promise)
    mockGetCheckoutPolicyVersions.mockReturnValueOnce(policiesFailed.promise)

    const { wrapper } = await mountCheckoutPage({ queryClient })

    // 先讓 revalidate 成功落地（快取被無券的權威購物車覆蓋），政策版本才失敗。
    revalidated.resolve(readyValidation())
    await vi.waitFor(() =>
      expect(queryClient.getQueryData<CartDto>(['cart', 'guest', 'guest-checkout-key'])?.coupon)
        .toBeNull())
    policiesFailed.reject(new Error('policy versions unavailable'))
    await vi.waitFor(() => expect(wrapper.text()).toContain('無法載入資料'))

    await wrapper.get('.shared-state--error button').trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    expect(wrapper.text()).toContain('帶入優惠碼 FREESHIP')
    expect(mockGetShippingOptions).toHaveBeenCalledWith('guest-checkout-key', 'FREESHIP')
  })

})

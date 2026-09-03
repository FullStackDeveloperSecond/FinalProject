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
    const order = createdOrder()
    order.amounts = {
      merchandiseSubtotal: 59900,
      itemDiscountTotal: 2000,
      shippingFee: 0,
      assemblyFee: 300,
      grandTotal: 58200,
      paidAmount: 0,
      refundedAmount: 0,
      currency: 'TWD',
    }
    mockCreateOrder.mockResolvedValue(order)
    const { wrapper, router } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await fillValidHomeDeliveryForm(wrapper)

    await wrapper.get('.checkout-page__submit').trigger('submit')

    await vi.waitFor(() => expect(wrapper.text()).toContain('ORD-20260902-0001'))
    expect(wrapper.text()).toContain('商品小計：NT$59,900')
    expect(wrapper.text()).toContain('優惠折扣：−NT$2,000')
    expect(wrapper.text()).toContain('配送費：NT$0')
    expect(wrapper.text()).toContain('組裝費：NT$300')
    expect(wrapper.text()).toContain('應付總額：NT$58,200')
    expect(wrapper.text()).toContain('驗證訂單後繼續付款')
    expect(wrapper.get('a[href="/guest-orders/access"]').attributes('href')).toBe('/guest-orders/access')
    expect(router.currentRoute.value.name).toBe('checkout')
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

    expect((wrapper.get('#coupon-code').element as HTMLInputElement).value).toBe('FREESHIP')
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

    expect((wrapper.get('#coupon-code').element as HTMLInputElement).value).toBe('')
    expect(mockGetShippingOptions).toHaveBeenCalledWith('guest-checkout-key', undefined)
  })

  it('does not carry a coupon when there is no in-memory cart (a fresh page load)', async () => {
    // 重新整理與跨裝置都是這一條：記憶體快取不存在，就不沿用。
    const { wrapper } = await mountCheckoutPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    expect((wrapper.get('#coupon-code').element as HTMLInputElement).value).toBe('')
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

    expect((wrapper.get('#coupon-code').element as HTMLInputElement).value).toBe('')
  })


  it('drops the previous identity\'s coupon when the identity changes on the mounted page', async () => {
    // alex #97 第二輪第 1 點：身分化的 query cache 只隔離得了快取，隔離不了已經複製到
    // 元件 state 的那一份。訪客套券進 C-14 之後在同一個掛載中登入，舊代碼不能繼續被
    // 送進 Shipping Options，更不能進建單請求。
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(['cart', 'guest', 'guest-checkout-key'], withCoupon('FREESHIP'))

    const { wrapper } = await mountCheckoutPage({ queryClient })
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    expect((wrapper.get('#coupon-code').element as HTMLInputElement).value).toBe('FREESHIP')

    mockGetShippingOptions.mockClear()
    await signInAs('member-1')
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    expect((wrapper.get('#coupon-code').element as HTMLInputElement).value).toBe('')
    expect(mockGetShippingOptions).not.toHaveBeenCalledWith(expect.anything(), 'FREESHIP')
  })

  it('drops a coupon typed on the checkout page itself when the account changes', async () => {
    // 沿用來的代碼與顧客自己在 C-14 打的代碼一樣是「上一個身分的東西」，
    // 換帳號時兩者都要清掉 —— 這條走的是後者，證明清除不是只針對交接來的值。
    const { wrapper } = await mountCheckoutPage({ authenticated: true })
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))
    await wrapper.get('#coupon-code').setValue('MEMBERA100')
    await wrapper.get('[data-test="apply-coupon"]').trigger('click')
    await vi.waitFor(() =>
      expect(mockGetShippingOptions).toHaveBeenCalledWith(expect.anything(), 'MEMBERA100'))

    mockGetShippingOptions.mockClear()
    await signInAs('member-2')
    await vi.waitFor(() => expect(wrapper.text()).toContain('宅配'))

    expect((wrapper.get('#coupon-code').element as HTMLInputElement).value).toBe('')
    expect(mockGetShippingOptions).not.toHaveBeenCalledWith(expect.anything(), 'MEMBERA100')
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

    expect((wrapper.get('#coupon-code').element as HTMLInputElement).value).toBe('FREESHIP')
    expect(mockGetShippingOptions).toHaveBeenCalledWith('guest-checkout-key', 'FREESHIP')
  })

})

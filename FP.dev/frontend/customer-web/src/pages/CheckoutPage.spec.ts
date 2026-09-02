import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import { useSessionStore } from '../stores/session'
import type { CartDto, CartValidationDto } from '../features/cart/types'
import type { ShippingOptionsDto } from '../features/shipping/types'
import type { CheckoutPolicyVersions } from '../features/checkout/api'

const mockRevalidateCart = vi.fn()
vi.mock('../features/cart/api', () => ({
  revalidateCart: () => mockRevalidateCart(),
}))

const mockGetShippingOptions = vi.fn()
const mockSearchConvenienceStores = vi.fn()
vi.mock('../features/shipping/api', () => ({
  getShippingOptions: () => mockGetShippingOptions(),
  searchConvenienceStores: (...args: unknown[]) => mockSearchConvenienceStores(...args),
}))

vi.mock('../features/cart/guestCartKey', () => ({
  getOrCreateGuestCartKey: () => 'guest-test-key',
}))

const mockFetchCheckoutPolicyVersions = vi.fn<() => Promise<CheckoutPolicyVersions>>()
const mockSubmitCheckout = vi.fn()
vi.mock('../features/checkout/api', () => ({
  fetchCheckoutPolicyVersions: () => mockFetchCheckoutPolicyVersions(),
  submitCheckout: (...args: unknown[]) => mockSubmitCheckout(...args),
}))

const mockFetchProfile = vi.fn()
const mockFetchAddresses = vi.fn()
vi.mock('../features/members/api', () => ({
  fetchProfile: () => mockFetchProfile(),
  fetchAddresses: () => mockFetchAddresses(),
}))

const oneItemCart: CartDto = {
  publicId: 'cart-1',
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
    maxPurchasableQuantity: 99,
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

function readyValidation(cart: CartDto = oneItemCart): CartValidationDto {
  return { cart, isCheckoutReady: true, issues: [], validatedAtUtc: '2026-01-01T00:00:00Z' }
}

const homeDeliveryOptions: ShippingOptionsDto = {
  cartPublicId: 'cart-1',
  cartRowVersion: 'AAAA',
  evaluatedAtUtc: '2026-01-01T00:00:00Z',
  options: [
    {
      methodCode: 'HomeDelivery',
      name: '宅配到府',
      fee: 100,
      isEligible: true,
      ineligibleReasonCode: null,
      freeShippingThreshold: null,
      requiresAddress: true,
      requiresStore: false,
      // EfShippingOptionsService.BuildOption 回傳的是粗分類（"prepaid"／"cashOnDelivery"），不是
      // 具體付款閘道——這裡刻意跟後端真實回應的詞彙一致，而不是隨便掰一組看起來像的字串。
      allowedPaymentMethods: ['prepaid', 'cashOnDelivery'],
    },
    {
      methodCode: 'StorePickup',
      name: '超商取貨',
      fee: 60,
      isEligible: true,
      ineligibleReasonCode: null,
      freeShippingThreshold: null,
      requiresAddress: false,
      requiresStore: true,
      allowedPaymentMethods: ['prepaid'],
    },
  ],
}

const policyVersions: CheckoutPolicyVersions = { terms: 1, return: 1, privacy: 1 }

async function mountCheckoutPage(options: { queryClient?: QueryClient, authenticated?: boolean } = {}) {
  const { default: CheckoutPage } = await import('./CheckoutPage.vue')
  const queryClient = options.queryClient ?? new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div />' } },
      { path: '/products', name: 'products', component: { template: '<div />' } },
      { path: '/cart', name: 'cart', component: { template: '<div />' } },
      { path: '/checkout', name: 'checkout', component: CheckoutPage },
      { path: '/orders/:orderId/payment', name: 'order-payment', component: { template: '<div />' } },
      { path: '/guest-orders/access', name: 'guest-order-access', component: { template: '<div />' } },
    ],
  })

  const pinia = createPinia()
  setActivePinia(pinia)
  const sessionStore = useSessionStore()
  if (options.authenticated) {
    sessionStore.status = 'authenticated'
    sessionStore.user = {
      publicId: 'member-1',
      displayName: '測試會員',
      emailMasked: 'm***@example.com',
      emailVerified: true,
      locale: 'zh-TW',
    }
  } else {
    sessionStore.status = 'anonymous'
  }

  await router.push('/checkout')
  await router.isReady()

  const wrapper = mount(CheckoutPage, {
    global: {
      plugins: [[VueQueryPlugin, { queryClient }], pinia, router],
    },
  })
  return { wrapper, router }
}

beforeEach(() => {
  mockRevalidateCart.mockReset()
  mockGetShippingOptions.mockReset()
  mockSearchConvenienceStores.mockReset()
  mockFetchCheckoutPolicyVersions.mockReset()
  mockSubmitCheckout.mockReset()
  mockFetchProfile.mockReset()
  mockFetchAddresses.mockReset()
  mockFetchCheckoutPolicyVersions.mockResolvedValue(policyVersions)
  // Only actually queried when authenticated (see useProfileQuery/useAddressesQuery's `enabled`),
  // but must resolve rather than hang/undefined whenever a member-mode test does trigger them.
  mockFetchProfile.mockResolvedValue({
    publicId: 'member-1', displayName: '測試會員', emailMasked: 'm***@example.com',
    emailVerified: true, phone: null, locale: 'zh-TW', createdAtUtc: '2026-01-01T00:00:00Z', rowVersion: 'AAAA',
  })
  mockFetchAddresses.mockResolvedValue([])
})

describe('CheckoutPage', () => {
  it('shows an empty-cart state when the cart has no items', async () => {
    mockRevalidateCart.mockResolvedValueOnce(readyValidation({ ...oneItemCart, items: [] }))
    const { wrapper } = await mountCheckoutPage()
    await flushPromises()

    expect(wrapper.text()).toContain('購物車是空的')
  })

  it('shows a not-ready state and a link back to the cart when checkout is not ready', async () => {
    mockRevalidateCart.mockResolvedValueOnce({
      cart: oneItemCart,
      isCheckoutReady: false,
      issues: [{ itemPublicId: 'item-1', code: 'cart_item_requires_attention', severity: 'blocking', availableActions: ['reduce-quantity'] }],
      validatedAtUtc: '2026-01-01T00:00:00Z',
    })
    const { wrapper } = await mountCheckoutPage()
    await flushPromises()

    expect(wrapper.text()).toContain('尚未通過結帳前檢查')
    expect(wrapper.get('a[href="/cart"]')).toBeTruthy()
  })

  it('submits a home-delivery order with the exact expected payload and navigates a member straight to the payment page', async () => {
    mockRevalidateCart.mockResolvedValueOnce(readyValidation())
    mockGetShippingOptions.mockResolvedValueOnce(homeDeliveryOptions)
    mockSubmitCheckout.mockResolvedValueOnce({ publicId: 'order-123', orderNumber: 'DS20260101001' })

    // A member's own session already satisfies order-detail/payment's Owner Member authorization,
    // so only a member goes straight there — see the guest test below for why a guest doesn't.
    const { wrapper, router } = await mountCheckoutPage({ authenticated: true })
    await flushPromises()

    await wrapper.get('#checkout-buyer-name').setValue('王小明')
    await wrapper.get('#checkout-buyer-email').setValue('buyer@example.test')
    await wrapper.get('#checkout-buyer-phone').setValue('0912345678')

    await wrapper.get('input[type="radio"][value="HomeDelivery"]').setValue()
    await flushPromises()

    await wrapper.get('#checkout-recipient-name').setValue('王小明')
    await wrapper.get('#checkout-recipient-phone').setValue('0912345678')
    await wrapper.get('#checkout-postal-code').setValue('100')
    await wrapper.get('#checkout-city').setValue('台北市')
    await wrapper.get('#checkout-district').setValue('中正區')
    await wrapper.get('#checkout-address-line1').setValue('測試路一號')

    await wrapper.get('input[name="payment-method"][value="creditCard"]').setValue()
    await wrapper.get('input[type="checkbox"]').setValue(true)

    const submitButton = wrapper.get('button[type="submit"]')
    expect(submitButton.attributes('disabled')).toBeUndefined()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(mockSubmitCheckout).toHaveBeenCalledWith(
      {
        cartPublicId: 'cart-1',
        cartRowVersion: 'AAAA',
        buyer: { email: 'buyer@example.test', name: '王小明', phone: '0912345678' },
        shipping: {
          methodCode: 'HomeDelivery',
          address: {
            recipientName: '王小明',
            phone: '0912345678',
            postalCode: '100',
            city: '台北市',
            district: '中正區',
            addressLine1: '測試路一號',
            addressLine2: null,
          },
          storePublicId: null,
          deliveryNote: null,
        },
        paymentMethod: 'creditCard',
        couponCode: null,
        invoice: {
          type: 'simulated',
          buyerType: 'personal',
          carrierType: null,
          carrierValue: null,
          companyTaxId: null,
          companyName: null,
        },
        acceptPolicyVersions: policyVersions,
      },
      expect.any(String),
      'guest-test-key',
    )

    expect(router.currentRoute.value.name).toBe('order-payment')
    expect(router.currentRoute.value.params.orderId).toBe('order-123')
  })

  // A guest who just checked out does not hold a verified GuestOrderAccessToken (that's only ever
  // issued by the separate WP-H03 guest-order-access journey) — OrdersController's own class doc
  // says order detail/payment always requires a member session or that token, with no exception
  // for "just created it". Sending a guest straight to /orders/{id}/payment would 401 immediately,
  // so this page shows an inline confirmation with a link into the real guest-access journey
  // instead of navigating anywhere it can't actually see.
  it('shows an inline confirmation with the order number for a guest instead of navigating to a page that would 401', async () => {
    mockRevalidateCart.mockResolvedValueOnce(readyValidation())
    mockGetShippingOptions.mockResolvedValueOnce(homeDeliveryOptions)
    mockSubmitCheckout.mockResolvedValueOnce({ publicId: 'order-123', orderNumber: 'DS20260101001' })

    const { wrapper, router } = await mountCheckoutPage()
    await flushPromises()

    await wrapper.get('#checkout-buyer-name').setValue('王小明')
    await wrapper.get('#checkout-buyer-email').setValue('buyer@example.test')
    await wrapper.get('#checkout-buyer-phone').setValue('0912345678')
    await wrapper.get('input[type="radio"][value="HomeDelivery"]').setValue()
    await flushPromises()
    await wrapper.get('#checkout-recipient-name').setValue('王小明')
    await wrapper.get('#checkout-recipient-phone').setValue('0912345678')
    await wrapper.get('#checkout-postal-code').setValue('100')
    await wrapper.get('#checkout-city').setValue('台北市')
    await wrapper.get('#checkout-district').setValue('中正區')
    await wrapper.get('#checkout-address-line1').setValue('測試路一號')
    await wrapper.get('input[name="payment-method"][value="creditCard"]').setValue()
    await wrapper.get('input[type="checkbox"]').setValue(true)

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('checkout')
    expect(wrapper.text()).toContain('DS20260101001')
    expect(wrapper.get('a[href="/guest-orders/access"]')).toBeTruthy()
  })

  it('disables submit until the required fields are filled', async () => {
    mockRevalidateCart.mockResolvedValueOnce(readyValidation())
    mockGetShippingOptions.mockResolvedValueOnce(homeDeliveryOptions)
    const { wrapper } = await mountCheckoutPage()
    await flushPromises()

    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()

    await wrapper.get('#checkout-buyer-name').setValue('王小明')
    await wrapper.get('#checkout-buyer-email').setValue('buyer@example.test')
    await wrapper.get('#checkout-buyer-phone').setValue('0912345678')
    await wrapper.get('input[type="radio"][value="HomeDelivery"]').setValue()
    await flushPromises()

    // Address and payment method are still missing.
    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('renders the store picker for a pickup shipping method instead of address fields', async () => {
    mockRevalidateCart.mockResolvedValueOnce(readyValidation())
    mockGetShippingOptions.mockResolvedValueOnce(homeDeliveryOptions)
    mockSearchConvenienceStores.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0 })
    const { wrapper } = await mountCheckoutPage()
    await flushPromises()

    await wrapper.get('input[type="radio"][value="StorePickup"]').setValue()
    await flushPromises()

    expect(wrapper.find('#checkout-recipient-name').exists()).toBe(false)
    expect(wrapper.text()).toContain('選擇取貨門市')
  })

  it('only offers the payment methods the selected shipping option allows', async () => {
    mockRevalidateCart.mockResolvedValueOnce(readyValidation())
    mockGetShippingOptions.mockResolvedValueOnce(homeDeliveryOptions)
    mockSearchConvenienceStores.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0 })
    const { wrapper } = await mountCheckoutPage()
    await flushPromises()

    await wrapper.get('input[type="radio"][value="HomeDelivery"]').setValue()
    await flushPromises()
    // HomeDelivery's fixture allows both "prepaid" (every gateway) and "cashOnDelivery".
    expect(wrapper.find('input[name="payment-method"][value="creditCard"]').exists()).toBe(true)
    expect(wrapper.find('input[name="payment-method"][value="atm"]').exists()).toBe(true)
    expect(wrapper.find('input[name="payment-method"][value="cashOnDelivery"]').exists()).toBe(true)

    await wrapper.get('input[type="radio"][value="StorePickup"]').setValue()
    await flushPromises()
    // StorePickup's fixture only allows "prepaid" — every gateway, but never cashOnDelivery.
    expect(wrapper.find('input[name="payment-method"][value="atm"]').exists()).toBe(true)
    expect(wrapper.find('input[name="payment-method"][value="cashOnDelivery"]').exists()).toBe(false)
  })

  it('shows a specific message and does not navigate when the cart changed underneath the shopper', async () => {
    mockRevalidateCart.mockResolvedValueOnce(readyValidation())
    mockGetShippingOptions.mockResolvedValueOnce(homeDeliveryOptions)
    mockSubmitCheckout.mockRejectedValueOnce(new ApiError('conflict', { status: 409, code: 'concurrency_conflict' }))

    const { wrapper, router } = await mountCheckoutPage()
    await flushPromises()

    await wrapper.get('#checkout-buyer-name').setValue('王小明')
    await wrapper.get('#checkout-buyer-email').setValue('buyer@example.test')
    await wrapper.get('#checkout-buyer-phone').setValue('0912345678')
    await wrapper.get('input[type="radio"][value="HomeDelivery"]').setValue()
    await flushPromises()
    await wrapper.get('#checkout-recipient-name').setValue('王小明')
    await wrapper.get('#checkout-recipient-phone').setValue('0912345678')
    await wrapper.get('#checkout-postal-code').setValue('100')
    await wrapper.get('#checkout-city').setValue('台北市')
    await wrapper.get('#checkout-district').setValue('中正區')
    await wrapper.get('#checkout-address-line1').setValue('測試路一號')
    await wrapper.get('input[name="payment-method"][value="creditCard"]').setValue()
    await wrapper.get('input[type="checkbox"]').setValue(true)

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('購物車內容已更新')
    expect(router.currentRoute.value.name).toBe('checkout')
  })
})

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import { useSessionStore } from '../../stores/session'
import type { CartDto } from '../cart/types'
import type { OrderDto, ShippingMethodOptionDto } from './api'

const mockGetCart = vi.fn<() => Promise<CartDto>>()
const mockFetchShippingOptions = vi.fn<() => Promise<ShippingMethodOptionDto[]>>()
const mockCreateOrder = vi.fn<(...args: unknown[]) => Promise<OrderDto>>()

vi.mock('../cart/api', () => ({
  getCart: () => mockGetCart(),
}))

vi.mock('../cart/guestCartKey', () => ({
  getOrCreateGuestCartKey: () => 'guest-test-key-000000000000000000000000',
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    fetchShippingOptions: () => mockFetchShippingOptions(),
    createOrder: (...args: unknown[]) => mockCreateOrder(...args),
  }
})

const { useRouter, routerPush } = vi.hoisted(() => ({
  useRouter: vi.fn(),
  routerPush: vi.fn(),
}))
vi.mock('vue-router', () => ({ useRouter }))

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

const oneItemCart: CartDto = {
  publicId: 'cart-1',
  items: [{
    publicId: 'item-1',
    skuPublicId: 'sku-1',
    skuCode: 'SKU-1',
    name: 'RTX 4070',
    quantity: 2,
    unitPrice: 18000,
    lineTotal: 36000,
    availability: 'available',
    priceChanged: false,
    maxPurchasableQuantity: 99,
    assemblyGroupKey: null,
    rowVersion: 'AAAA',
  }],
  coupon: null,
  amounts: {
    subtotal: 36000,
    itemDiscount: 0,
    couponDiscount: 0,
    shippingEstimate: null,
    assemblyFee: 0,
    totalEstimate: 36000,
    currency: 'TWD',
  },
  warnings: [],
  rowVersion: 'AAAA',
}

const homeMethod: ShippingMethodOptionDto = {
  code: 'HOME-STD',
  nameZhTw: '一般宅配',
  kind: 'HomeDeliveryStandard',
  baseFee: 150,
  freeShippingThreshold: 5000,
  allowsCod: true,
  requiresPrepayment: false,
}

async function mountCheckoutForm() {
  const { default: CheckoutForm } = await import('./CheckoutForm.vue')
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const pinia = createPinia()
  setActivePinia(pinia)
  useSessionStore().status = 'anonymous'

  return mount(CheckoutForm, {
    global: {
      plugins: [[VueQueryPlugin, { queryClient }], pinia],
      stubs: globalStubs,
    },
  })
}

beforeEach(() => {
  mockGetCart.mockReset()
  mockFetchShippingOptions.mockReset()
  mockCreateOrder.mockReset()
  routerPush.mockReset().mockResolvedValue(undefined)
  useRouter.mockReturnValue({ push: routerPush })
})

describe('CheckoutForm', () => {
  it('shows an empty-cart message when the cart has no items', async () => {
    mockGetCart.mockResolvedValueOnce({ ...oneItemCart, items: [] })
    mockFetchShippingOptions.mockResolvedValueOnce([homeMethod])

    const wrapper = await mountCheckoutForm()
    await flushPromises()

    expect(wrapper.text()).toContain('購物車是空的')
  })

  it('renders the cart summary and shipping options once both load', async () => {
    mockGetCart.mockResolvedValueOnce(oneItemCart)
    mockFetchShippingOptions.mockResolvedValueOnce([homeMethod])

    const wrapper = await mountCheckoutForm()
    await flushPromises()

    expect(wrapper.text()).toContain('RTX 4070')
    expect(wrapper.text()).toContain('一般宅配')
    expect(wrapper.find('#checkout-shipping-method').element).toBeTruthy()
  })

  it('keeps submit disabled until the policy checkbox is accepted', async () => {
    mockGetCart.mockResolvedValueOnce(oneItemCart)
    mockFetchShippingOptions.mockResolvedValueOnce([homeMethod])

    const wrapper = await mountCheckoutForm()
    await flushPromises()

    const submitButton = wrapper.get('button[type="submit"]')
    expect((submitButton.element as HTMLButtonElement).disabled).toBe(true)

    await wrapper.get('input[type="checkbox"]').setValue(true)
    expect((submitButton.element as HTMLButtonElement).disabled).toBe(false)
  })

  it('submits the order and navigates to the order detail page on success', async () => {
    mockGetCart.mockResolvedValueOnce(oneItemCart)
    mockFetchShippingOptions.mockResolvedValueOnce([homeMethod])
    mockCreateOrder.mockResolvedValueOnce({ publicId: 'order-public-id' } as OrderDto)

    const wrapper = await mountCheckoutForm()
    await flushPromises()

    await wrapper.get('input[type="checkbox"]').setValue(true)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(mockCreateOrder).toHaveBeenCalledTimes(1)
    const [body] = mockCreateOrder.mock.calls[0]!
    expect(body).toMatchObject({
      cartPublicId: 'cart-1',
      cartRowVersion: 'AAAA',
      shipping: { methodCode: 'HOME-STD' },
    })
    expect(routerPush).toHaveBeenCalledWith({
      name: 'order-detail',
      params: { orderId: 'order-public-id' },
    })
  })

  it('shows a mapped message when the backend rejects the order', async () => {
    mockGetCart.mockResolvedValueOnce(oneItemCart)
    mockFetchShippingOptions.mockResolvedValueOnce([homeMethod])
    mockCreateOrder.mockRejectedValueOnce(new ApiError('Conflict', {
      status: 409,
      code: 'inventory_insufficient',
    }))

    const wrapper = await mountCheckoutForm()
    await flushPromises()

    await wrapper.get('input[type="checkbox"]').setValue(true)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('庫存不足')
    expect(routerPush).not.toHaveBeenCalled()
  })
})

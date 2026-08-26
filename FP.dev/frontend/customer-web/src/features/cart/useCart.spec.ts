import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSessionStore } from '../../stores/session'
import type { CartDto } from './types'

const mockGetCart = vi.fn<() => Promise<CartDto>>()
const mockAddCartItem = vi.fn<() => Promise<CartDto>>()
const mockRemoveCartItem = vi.fn<() => Promise<CartDto>>()

vi.mock('./api', () => ({
  getCart: (...args: unknown[]) => mockGetCart(...(args as [])),
  addCartItem: (...args: unknown[]) => mockAddCartItem(...(args as [])),
  updateCartItemQuantity: vi.fn(),
  removeCartItem: (...args: unknown[]) => mockRemoveCartItem(...(args as [])),
  revalidateCart: vi.fn(),
  mergeCartOnLogin: vi.fn(),
}))

vi.mock('./guestCartKey', () => ({
  getOrCreateGuestCartKey: () => 'guest-test-key',
  clearGuestCartKey: vi.fn(),
}))

const emptyCart: CartDto = {
  publicId: 'cart-1',
  items: [],
  coupon: null,
  amounts: {
    subtotal: 0,
    itemDiscount: 0,
    couponDiscount: 0,
    shippingEstimate: null,
    assemblyFee: 0,
    totalEstimate: 0,
    currency: 'TWD',
  },
  warnings: [],
  rowVersion: 'AAAA',
}

function withQueryClient(setup: () => unknown) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const pinia = createPinia()
  setActivePinia(pinia)
  useSessionStore().status = 'anonymous'
  const TestComponent = defineComponent({ setup: () => { setup(); return () => h('div') } })
  return mount(TestComponent, { global: { plugins: [[VueQueryPlugin, { queryClient }], pinia] } })
}

beforeEach(() => {
  mockGetCart.mockReset()
  mockAddCartItem.mockReset()
  mockRemoveCartItem.mockReset()
})

describe('useCart', () => {
  it('fetches the cart using the guest key', async () => {
    mockGetCart.mockResolvedValue(emptyCart)
    const { useCart } = await import('./useCart')
    let query: ReturnType<typeof useCart> | undefined

    withQueryClient(() => { query = useCart() })
    await vi.waitFor(() => expect(query!.isSuccess.value).toBe(true))

    expect(mockGetCart).toHaveBeenCalledWith('guest-test-key')
    expect(query!.data.value).toEqual(emptyCart)
  })

  it('updates the cache with the mutation result on add', async () => {
    const cartWithItem: CartDto = { ...emptyCart, items: [{
      publicId: 'item-1',
      skuPublicId: 'sku-1',
      skuCode: 'SKU-1',
      name: '測試商品',
      quantity: 1,
      unitPrice: 100,
      lineTotal: 100,
      availability: 'available',
      priceChanged: false,
      maxPurchasableQuantity: 99,
      assemblyGroupKey: null,
      rowVersion: 'BBBB',
    }] }
    mockAddCartItem.mockResolvedValue(cartWithItem)
    const { useAddCartItem, useCart } = await import('./useCart')
    let addMutation: ReturnType<typeof useAddCartItem> | undefined
    let cartQuery: ReturnType<typeof useCart> | undefined

    withQueryClient(() => {
      addMutation = useAddCartItem()
      cartQuery = useCart()
    })

    await addMutation!.mutateAsync({ skuPublicId: 'sku-1', quantity: 1, cartRowVersion: null })

    expect(mockAddCartItem).toHaveBeenCalledWith('sku-1', 1, null, 'guest-test-key')
    expect(cartQuery!.data.value).toEqual(cartWithItem)
  })
})

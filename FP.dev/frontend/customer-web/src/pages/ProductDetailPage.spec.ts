import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { ApiError } from '@doselect/web-shared/api'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSessionStore } from '../stores/session'

const mockGetProductDetail = vi.fn()
const mockAddCartItem = vi.fn()

vi.mock('../features/catalog/api', () => ({
  getProductDetail: mockGetProductDetail,
  searchProducts: vi.fn(),
  getCatalogFilterOptions: vi.fn(),
}))

vi.mock('../features/cart/api', () => ({
  getCart: vi.fn(),
  addCartItem: (...args: unknown[]) => mockAddCartItem(...args),
  updateCartItemQuantity: vi.fn(),
  removeCartItem: vi.fn(),
  revalidateCart: vi.fn(),
  mergeCartOnLogin: vi.fn(),
}))

vi.mock('../features/cart/guestCartKey', () => ({
  getOrCreateGuestCartKey: () => 'guest-test-key',
  clearGuestCartKey: vi.fn(),
}))

const { default: ProductDetailPage } = await import('./ProductDetailPage.vue')

function productDetail(overrides: Record<string, unknown> = {}) {
  return {
    productPublicId: 'p1',
    defaultSkuPublicId: 'sku-1',
    productCode: 'P1',
    skuCode: 'SKU-1',
    name: 'Test Product',
    brand: { code: 'ACME', name: 'Acme' },
    category: { code: 'CAT', name: 'Category' },
    price: { list: 1000, sale: null, currency: 'TWD' },
    availability: 'inStock',
    primaryImage: null,
    badges: [],
    description: null,
    tags: [],
    images: [],
    skus: [{
      publicId: 'sku-1',
      skuCode: 'SKU-1',
      name: 'Default',
      price: { list: 1000, sale: null, currency: 'TWD' },
      availability: 'inStock',
      maxPurchasableQuantity: 10,
      specifications: [],
      dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null },
      isDefault: true,
    }],
    specificationGroups: [],
    shippingRestrictions: [],
    warrantyMonths: null,
    ...overrides,
  }
}

function setUpAnonymousSession(): void {
  const pinia = createPinia()
  setActivePinia(pinia)
  useSessionStore().status = 'anonymous'
}

async function mountPage(id = 'p1') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/products/:productId', name: 'product-detail', component: ProductDetailPage, props: true },
    ],
  })
  await router.push(`/products/${id}`)
  await router.isReady()
  setUpAnonymousSession()
  return mount(ProductDetailPage, { global: { plugins: [[VueQueryPlugin, { queryClient }], router] } })
}

describe('ProductDetailPage', () => {
  beforeEach(() => {
    mockAddCartItem.mockReset()
  })

  /** PR #24 review: C-03's DTO already carries images, but the detail page never rendered them. */
  it('renders the product images gallery', async () => {
    mockGetProductDetail.mockResolvedValue(productDetail({
      images: [{ url: 'https://example.com/a.jpg', alt: 'Front', width: 800, height: 600, isPrimary: true }],
    }))

    const wrapper = await mountPage()
    await flushPromises()

    const image = wrapper.find('.product-detail__gallery img')
    expect(image.exists()).toBe(true)
    expect(image.attributes('src')).toBe('https://example.com/a.jpg')
  })

  /** PR #24 review: C-03's DTO already carries warrantyMonths, but the detail page never rendered it. */
  it('shows the warranty period when present', async () => {
    mockGetProductDetail.mockResolvedValue(productDetail({ warrantyMonths: 24 }))

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('保固 24 個月')
  })

  it('does not show a warranty line when warrantyMonths is null', async () => {
    mockGetProductDetail.mockResolvedValue(productDetail({ warrantyMonths: null }))

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.find('.product-detail__warranty').exists()).toBe(false)
  })

  /** PR #24 review: C-03's DTO already carries shippingRestrictions, but the detail page never rendered them. */
  it('renders shipping restrictions', async () => {
    mockGetProductDetail.mockResolvedValue(productDetail({
      shippingRestrictions: [{ method: 'convenience_store', allowed: false, reasonCode: 'oversized' }],
    }))

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('不可配送')
    expect(wrapper.text()).toContain('oversized')
  })

  /**
   * Regression test (組長 PR #24 review round 10, P3; strengthened round 11, P3): `selectedSkuPublicId`
   * never reset when the product data changed. useRoute()'s productPublicId is reactive independent
   * of how this component is mounted, so a param-only navigation on the same route record
   * (/products/A -> /products/B) re-fetches and swaps `product` in place — but the SKU `<select>`
   * stayed bound to whichever publicId was selected on A, which doesn't match any of B's options.
   *
   * Round 11 review caught that the original version of this test gave B only a single SKU, so
   * `#sku-select` wasn't rendered at all after navigating — the assertions on price/spec text
   * passed regardless of whether the fix's `watch(product, ...)` existed, because `selectedSku`'s
   * pre-existing fallback to `product.skus[0]` produces the same result by coincidence. B now has
   * two SKUs (matching A's shape) so the `<select>` stays rendered post-navigation, and the test
   * asserts its `.value` directly landed on B's default — which only the watch can produce, since
   * without it the `<select>`'s v-model would still hold A's stale selected publicId ('a2'), a
   * value that isn't one of B's `<option>`s.
   */
  it('resets the selected SKU to the new product\'s default after navigating to a different product', async () => {
    const productA = productDetail({
      productPublicId: 'p1',
      skus: [
        {
          publicId: 'a-default', skuCode: 'A-DEFAULT', name: 'A Default',
          price: { list: 1000, sale: null, currency: 'TWD' }, availability: 'inStock',
          maxPurchasableQuantity: 10, specifications: [], dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null },
          isDefault: true,
        },
        {
          publicId: 'a2', skuCode: 'A2', name: 'A Variant 2',
          price: { list: 1200, sale: null, currency: 'TWD' }, availability: 'inStock',
          maxPurchasableQuantity: 10, specifications: [], dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null },
          isDefault: false,
        },
      ],
    })
    const productB = productDetail({
      productPublicId: 'p2',
      skus: [
        {
          publicId: 'b-default', skuCode: 'B-DEFAULT', name: 'B Default',
          price: { list: 2000, sale: null, currency: 'TWD' }, availability: 'inStock',
          maxPurchasableQuantity: 10, specifications: [{ semanticKey: 'color', label: '顏色', value: 'Black', unit: null }],
          dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null },
          isDefault: true,
        },
        {
          publicId: 'b2', skuCode: 'B2', name: 'B Variant 2',
          price: { list: 2500, sale: null, currency: 'TWD' }, availability: 'inStock',
          maxPurchasableQuantity: 10, specifications: [{ semanticKey: 'color', label: '顏色', value: 'White', unit: null }],
          dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null },
          isDefault: false,
        },
      ],
    })
    mockGetProductDetail.mockImplementation((id: string) => Promise.resolve(id === 'p2' ? productB : productA))

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/products/:productId', name: 'product-detail', component: ProductDetailPage, props: true }],
    })
    await router.push('/products/p1')
    await router.isReady()
    setUpAnonymousSession()
    const wrapper = mount(ProductDetailPage, { global: { plugins: [[VueQueryPlugin, { queryClient }], router] } })
    await flushPromises()

    await wrapper.find('#sku-select').setValue('a2')
    await flushPromises()
    expect((wrapper.find('#sku-select').element as HTMLSelectElement).value).toBe('a2')

    await router.push('/products/p2')
    await flushPromises()

    // B's dropdown still renders (2 SKUs) — its value must have been reset to B's default
    // ('b-default'), not left holding A's stale 'a2' (which isn't one of B's options at all).
    expect(wrapper.find('#sku-select').exists()).toBe(true)
    expect((wrapper.find('#sku-select').element as HTMLSelectElement).value).toBe('b-default')
    expect(wrapper.text()).toContain('NT$2,000')
    expect(wrapper.text()).toContain('Black')
  })

  /**
   * 組長 PR #29 review round 3, P1: `useAddCartItem()` existed but nothing on this page ever
   * called it — the "加入購物車" button was permanently disabled with a "coming soon" label, so
   * the primary product -> cart flow (UC-CART-01) didn't work for any shopper. This drives the
   * real click handler end to end: selecting a SKU, clicking add, and confirming the mutation
   * fires with that SKU and the button reflects pending/success state.
   */
  it('adds the selected SKU to the cart when "加入購物車" is clicked', async () => {
    mockGetProductDetail.mockResolvedValue(productDetail({
      skus: [
        {
          publicId: 'sku-1', skuCode: 'SKU-1', name: 'Default',
          price: { list: 1000, sale: null, currency: 'TWD' }, availability: 'inStock',
          maxPurchasableQuantity: 10, specifications: [], dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null },
          isDefault: true,
        },
        {
          publicId: 'sku-2', skuCode: 'SKU-2', name: 'Variant',
          price: { list: 1200, sale: null, currency: 'TWD' }, availability: 'inStock',
          maxPurchasableQuantity: 10, specifications: [], dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null },
          isDefault: false,
        },
      ],
    }))
    let resolveAddCartItem!: (cart: unknown) => void
    mockAddCartItem.mockReturnValue(new Promise((resolve) => { resolveAddCartItem = resolve }))

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('#sku-select').setValue('sku-2')
    await flushPromises()

    const addButton = wrapper.findAll('button').find((button) => button.text().includes('加入購物車'))!
    await addButton.trigger('click')
    await flushPromises()

    expect(mockAddCartItem).toHaveBeenCalledWith('sku-2', 1, null, 'guest-test-key')
    expect(addButton.text()).toContain('加入中')
    expect(addButton.attributes('disabled')).toBeDefined()

    resolveAddCartItem({ publicId: 'cart-1' })
    await flushPromises()

    expect(wrapper.text()).toContain('已加入購物車')
  })

  it('shows a retryable error when adding to the cart fails', async () => {
    mockGetProductDetail.mockResolvedValue(productDetail())
    mockAddCartItem.mockRejectedValue(new ApiError('unavailable', { status: 409, code: 'sku_unavailable' }))

    const wrapper = await mountPage()
    await flushPromises()

    const addButton = wrapper.findAll('button').find((button) => button.text().includes('加入購物車'))!
    await addButton.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('此規格已下架')
  })

  /** 組長 PR #29 review round 3, P1: an out-of-stock SKU must not be addable at all. */
  it('disables "加入購物車" when the selected SKU is out of stock', async () => {
    mockGetProductDetail.mockResolvedValue(productDetail({
      skus: [{
        publicId: 'sku-1', skuCode: 'SKU-1', name: 'Default',
        price: { list: 1000, sale: null, currency: 'TWD' }, availability: 'outOfStock',
        maxPurchasableQuantity: 0, specifications: [], dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null },
        isDefault: true,
      }],
    }))

    const wrapper = await mountPage()
    await flushPromises()

    const addButton = wrapper.findAll('button').find((button) => button.text().includes('加入購物車'))!
    expect(addButton.attributes('disabled')).toBeDefined()

    await addButton.trigger('click')
    await flushPromises()
    expect(mockAddCartItem).not.toHaveBeenCalled()
  })
})

import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'

const mockGetProductDetail = vi.fn()

vi.mock('../features/catalog/api', () => ({
  getProductDetail: mockGetProductDetail,
  searchProducts: vi.fn(),
  getCatalogFilterOptions: vi.fn(),
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
  return mount(ProductDetailPage, { global: { plugins: [[VueQueryPlugin, { queryClient }], router] } })
}

describe('ProductDetailPage', () => {
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
})

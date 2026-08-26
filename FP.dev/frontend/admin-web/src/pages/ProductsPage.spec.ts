import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'

const mockListAdminProducts = vi.fn()
const mockListBrands = vi.fn()
const mockListCategories = vi.fn()

vi.mock('../features/products/api', () => ({
  listAdminProducts: mockListAdminProducts,
  getAdminProduct: vi.fn(),
  createProduct: vi.fn(),
  updateProduct: vi.fn(),
}))
vi.mock('../features/brands/api', () => ({ listBrands: mockListBrands }))
vi.mock('../features/categories/api', () => ({ listCategories: mockListCategories }))

const { default: ProductsPage } = await import('./ProductsPage.vue')

async function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/products', name: 'products', component: ProductsPage },
      { path: '/products/new', name: 'product-new', component: { template: '<div />' } },
      { path: '/products/:productId', name: 'product-edit', component: { template: '<div />' } },
    ],
  })
  await router.push('/products')
  await router.isReady()
  return mount(ProductsPage, { global: { plugins: [[VueQueryPlugin, { queryClient }], router] } })
}

describe('ProductsPage', () => {
  it('loads all active brand and category pages for the filter pickers', async () => {
    mockListAdminProducts.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })
    mockListBrands.mockImplementation(({ pageNumber }: { pageNumber?: number }) => Promise.resolve(
      pageNumber === 2
        ? { items: [{ publicId: 'brand-101', code: 'BRAND-101', nameZhTw: 'Brand 101' }], pageNumber: 2, pageSize: 100, totalCount: 101 }
        : { items: [{ publicId: 'brand-1', code: 'BRAND-1', nameZhTw: 'Brand 1' }], pageNumber: 1, pageSize: 100, totalCount: 101 },
    ))
    mockListCategories.mockImplementation(({ pageNumber }: { pageNumber?: number }) => Promise.resolve(
      pageNumber === 2
        ? { items: [{ publicId: 'category-101', code: 'CAT-101', nameZhTw: 'Category 101' }], pageNumber: 2, pageSize: 100, totalCount: 101 }
        : { items: [{ publicId: 'category-1', code: 'CAT-1', nameZhTw: 'Category 1' }], pageNumber: 1, pageSize: 100, totalCount: 101 },
    ))

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.find('select[aria-label="品牌"]').text()).toContain('Brand 101')
    expect(wrapper.find('select[aria-label="分類"]').text()).toContain('Category 101')
  })

  it('offers every Product lifecycle status in the filter', async () => {
    mockListBrands.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListCategories.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListAdminProducts.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })

    const wrapper = await mountPage()
    await flushPromises()

    const statusOptions = wrapper.find('select[aria-label="狀態"]').findAll('option').map((option) => option.attributes('value'))
    expect(statusOptions).toEqual(['', 'Draft', 'Published', 'Unpublished', 'Discontinued'])
  })

  /** UC-ADM-PROD-01 acceptance: "價格顯示最低至最高區間" — AdminProductSummaryDto already carries minPrice/maxPrice, but the table never showed them. */
  it('renders the min-to-max price range for a multi-SKU product', async () => {
    mockListBrands.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListCategories.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListAdminProducts.mockResolvedValue({
      items: [{
        publicId: 'p1',
        productCode: 'P1',
        nameZhTw: 'Product 1',
        brand: { code: 'ACME', name: 'Acme' },
        category: { code: 'CAT', name: 'Category' },
        status: 'Published',
        skuCount: 3,
        minPrice: 1000,
        maxPrice: 3000,
        totalOnHandQuantity: 10,
        primaryImage: null,
        updatedAtUtc: '2026-01-01T00:00:00Z',
        rowVersion: 'AAA=',
      }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
    })

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('NT$1,000 - NT$3,000')
  })

  it('renders a single price when min and max are equal', async () => {
    mockListBrands.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListCategories.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListAdminProducts.mockResolvedValue({
      items: [{
        publicId: 'p1',
        productCode: 'P1',
        nameZhTw: 'Product 1',
        brand: { code: 'ACME', name: 'Acme' },
        category: { code: 'CAT', name: 'Category' },
        status: 'Published',
        skuCount: 1,
        minPrice: 1500,
        maxPrice: 1500,
        totalOnHandQuantity: 10,
        primaryImage: null,
        updatedAtUtc: '2026-01-01T00:00:00Z',
        rowVersion: 'AAA=',
      }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
    })

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('NT$1,500')
    expect(wrapper.text()).not.toContain('NT$1,500 - NT$1,500')
  })

  /**
   * Regression test (組長 PR #24 review round 3, item 3): typing into the keyword field used to
   * re-run the search on every keystroke, before "套用篩選" ever pushed the URL — so the visible
   * results, the URL, and a shared/reloaded link all disagreed with each other.
   */
  it('does not issue a new search while the keyword field is edited but not yet applied', async () => {
    mockListBrands.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListCategories.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListAdminProducts.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })

    const wrapper = await mountPage()
    await flushPromises()
    const callsBeforeEdit = mockListAdminProducts.mock.calls.length

    await wrapper.find('input[aria-label="關鍵字"]').setValue('RTX')
    await flushPromises()

    expect(mockListAdminProducts.mock.calls.length).toBe(callsBeforeEdit)
  })

  /** Companion to the test above: submitting "套用篩選" must push the URL and re-search with the same value. */
  it('updates the URL and re-searches with the applied keyword once submitted', async () => {
    mockListBrands.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListCategories.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListAdminProducts.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('input[aria-label="關鍵字"]').setValue('RTX')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockListAdminProducts).toHaveBeenLastCalledWith(expect.objectContaining({ q: 'RTX' }))
    expect(wrapper.vm.$route.query.q).toBe('RTX')
  })
})

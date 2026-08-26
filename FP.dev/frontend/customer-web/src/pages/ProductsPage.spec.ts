import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'

const mockSearchProducts = vi.fn()
const mockGetCatalogFilterOptions = vi.fn()

vi.mock('../features/catalog/api', () => ({
  searchProducts: mockSearchProducts,
  getCatalogFilterOptions: mockGetCatalogFilterOptions,
  getProductDetail: vi.fn(),
}))

const { default: ProductsPage } = await import('./ProductsPage.vue')

const emptyResult = { items: [], pageNumber: 1, pageSize: 20, totalCount: 0 }

const filterOptionsWithSpec = {
  categories: [
    { publicId: 'c1', code: 'default-category', name: 'Default' },
    { publicId: 'c2', code: 'other-category', name: 'Other' },
  ],
  brands: [],
  priceRange: { min: 100, max: 9000 },
  specificationFilters: [
    {
      semanticKey: 'CPU_SOCKET',
      label: 'CPU 腳位',
      valueType: 'Option',
      unit: null,
      operators: ['eq', 'in'],
      options: [{ code: 'AM5', label: 'AM5' }, { code: 'LGA1700', label: 'LGA1700' }],
    },
    {
      semanticKey: 'MEMORY_CAPACITY_GB',
      label: '記憶體容量',
      valueType: 'Decimal',
      unit: 'GB',
      operators: ['eq', 'gte', 'lte'],
      options: null,
    },
    {
      semanticKey: 'IS_RGB',
      label: 'RGB 燈效',
      valueType: 'Boolean',
      unit: null,
      operators: ['eq'],
      options: null,
    },
  ],
  sortOptions: ['relevance', 'priceAsc', 'priceDesc', 'newest'],
}

async function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/products', name: 'products', component: ProductsPage },
      { path: '/products/:productId', name: 'product-detail', component: { template: '<div />' } },
    ],
  })
  await router.push('/products')
  await router.isReady()
  return mount(ProductsPage, { global: { plugins: [[VueQueryPlugin, { queryClient }], router] } })
}

describe('ProductsPage', () => {
  it('shows retry and clear actions when catalog filter options fail to load', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)
    mockGetCatalogFilterOptions.mockRejectedValue(new Error('lookup failed'))

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('目錄篩選資料載入失敗')
    expect(wrapper.findAll('button').some((button) => button.text() === '重試')).toBe(true)
    expect(wrapper.findAll('button').some((button) => button.text() === '清除全部篩選')).toBe(true)
  })

  /** PR #24 review: C-02 requires price-range filtering — the API already supports MinPrice/MaxPrice, but the page had no input for it. */
  it('sends minPrice and maxPrice when the range inputs are filled in and submitted', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)
    mockGetCatalogFilterOptions.mockResolvedValue(filterOptionsWithSpec)

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('input[aria-label="最低價"]').setValue('1000')
    await wrapper.find('input[aria-label="最高價"]').setValue('5000')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockSearchProducts).toHaveBeenLastCalledWith(expect.objectContaining({ minPrice: 1000, maxPrice: 5000 }))
  })

  /** PR #24 review: C-02 requires whitelist spec filtering — the API supports Specs but no UI ever sent it. */
  it('sends a Specs "in" filter when an option-based spec checkbox is selected', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)
    mockGetCatalogFilterOptions.mockResolvedValue(filterOptionsWithSpec)

    const wrapper = await mountPage()
    await flushPromises()

    const specCheckbox = wrapper.find('fieldset input[type="checkbox"]')
    await specCheckbox.setValue(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockSearchProducts).toHaveBeenLastCalledWith(expect.objectContaining({
      specs: [{ semanticKey: 'CPU_SOCKET', operator: 'in', values: ['AM5'] }],
    }))
  })

  /** PR #24 review round 2: valueType Decimal specs (no options) need a range (gte/lte) control, not just the options whitelist. */
  it('sends gte/lte Specs filters when a decimal range spec is filled in', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)
    mockGetCatalogFilterOptions.mockResolvedValue(filterOptionsWithSpec)

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('input[aria-label="記憶體容量 最小值"]').setValue('16')
    await wrapper.find('input[aria-label="記憶體容量 最大值"]').setValue('64')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockSearchProducts).toHaveBeenLastCalledWith(expect.objectContaining({
      specs: expect.arrayContaining([
        { semanticKey: 'MEMORY_CAPACITY_GB', operator: 'gte', value: '16' },
        { semanticKey: 'MEMORY_CAPACITY_GB', operator: 'lte', value: '64' },
      ]),
    }))
  })

  /** PR #24 review round 2: valueType Boolean specs (no options) need an eq control. */
  it('sends an eq Specs filter when a boolean spec is selected', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)
    mockGetCatalogFilterOptions.mockResolvedValue(filterOptionsWithSpec)

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('select[aria-label="RGB 燈效"]').setValue('true')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockSearchProducts).toHaveBeenLastCalledWith(expect.objectContaining({
      specs: expect.arrayContaining([{ semanticKey: 'IS_RGB', operator: 'eq', value: 'true' }]),
    }))
  })

  /**
   * PR #24 review round 2: switching category used to leave the old category's spec
   * selections in state and the URL — the backend rejects a SemanticKey the new category
   * doesn't offer with search_filter_unsupported, and the UI gave no way to see/clear it.
   */
  it('clears a spec selection that the newly selected category no longer offers', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)
    mockGetCatalogFilterOptions.mockImplementation((category?: string) =>
      Promise.resolve(category === 'other-category'
        ? { ...filterOptionsWithSpec, specificationFilters: [] }
        : filterOptionsWithSpec))

    const wrapper = await mountPage()
    await flushPromises()

    const specCheckbox = wrapper.find('fieldset input[type="checkbox"]')
    await specCheckbox.setValue(true)
    await flushPromises()
    expect((specCheckbox.element as HTMLInputElement).checked).toBe(true)

    await wrapper.find('select[aria-label="分類"]').setValue('other-category')
    await flushPromises()

    expect(wrapper.find('fieldset').exists()).toBe(false)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockSearchProducts).toHaveBeenLastCalledWith(expect.objectContaining({ specs: undefined }))
  })

  /**
   * Regression test (組長 PR #24 review round 3, item 3): typing into the keyword field, or
   * switching brand/price, used to re-run the search on every change — before "套用篩選" ever
   * pushed the URL — so the visible results, the URL, and a shared/reloaded link all disagreed.
   */
  it('does not issue a new search while filters are edited but not yet applied', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)
    mockGetCatalogFilterOptions.mockResolvedValue(filterOptionsWithSpec)

    const wrapper = await mountPage()
    await flushPromises()
    const callsBeforeEdit = mockSearchProducts.mock.calls.length

    await wrapper.find('input[aria-label="關鍵字"]').setValue('4090')
    await wrapper.find('input[aria-label="最低價"]').setValue('1000')
    await flushPromises()

    expect(mockSearchProducts.mock.calls.length).toBe(callsBeforeEdit)
  })

  /** Companion to the test above: submitting "套用篩選" must push the URL and re-search with the same values. */
  it('updates the URL and re-searches with the applied keyword once submitted', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)
    mockGetCatalogFilterOptions.mockResolvedValue(filterOptionsWithSpec)

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('input[aria-label="關鍵字"]').setValue('4090')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockSearchProducts).toHaveBeenLastCalledWith(expect.objectContaining({ q: '4090' }))
    expect(wrapper.vm.$route.query.q).toBe('4090')
  })
})

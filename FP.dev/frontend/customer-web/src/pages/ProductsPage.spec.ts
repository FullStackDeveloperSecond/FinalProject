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

async function mountPage(initialLocation = '/products') {
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
  await router.push(initialLocation)
  await router.isReady()
  return mount(ProductsPage, { global: { plugins: [[VueQueryPlugin, { queryClient }], router] } })
}

describe('ProductsPage', () => {
  it('shows labelled all-category and all-brand defaults without a query', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)
    mockGetCatalogFilterOptions.mockResolvedValue(filterOptionsWithSpec)
    const wrapper = await mountPage()
    await flushPromises()
    for (const label of ['分類', '品牌']) {
      const select = wrapper.get(`select[aria-label="${label}"]`)
      expect((select.element as HTMLSelectElement).value).toBe('')
      expect((select.element as HTMLSelectElement).selectedOptions[0]?.textContent).toContain(`全部${label}`)
      expect(select.element.closest('label')?.textContent).toContain(label)
    }
  })
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

  /**
   * 從首頁分類卡進來時 route query 已經有 category=CPU，但 filter-options 還在飛。
   *
   * 而且 `/api/v1/catalog/filter-options` 的 `categories` 是「可再往下鑽的分類」：
   * 未選分類時回頂層清單，**已選分類時回該分類的子分類**
   * （EfCatalogFilterOptionsService.GetCategoriesAsync）。seeded 的 CPU／GPU 等
   * 都是沒有子分類的頂層分類，所以帶 Category=CPU 查回來的 `categories` 是空陣列 ——
   * 回應裡永遠不會有 CPU 這個 option，原生 <select> 只能落回空值。
   */
  it('keeps the deep-linked category selected once filter options resolve late', async () => {
    mockSearchProducts.mockClear()
    mockSearchProducts.mockResolvedValue(emptyResult)

    let resolveOptions: (value: unknown) => void = () => {}
    mockGetCatalogFilterOptions.mockReturnValue(new Promise((resolve) => { resolveOptions = resolve }))

    const wrapper = await mountPage('/products?category=CPU')
    await flushPromises()

    const select = () => wrapper.find('select[aria-label="分類"]')
    const optionValues = () => select().findAll('option').map((option) => option.attributes('value'))

    // 選項還沒到就必須已經顯示 CPU，不能先閃一下「全部分類」
    expect(optionValues()).toContain('CPU')
    expect((select().element as HTMLSelectElement).value).toBe('CPU')

    // 真實契約：帶了 Category 就只回子分類，CPU 自己不在裡面
    resolveOptions({ ...filterOptionsWithSpec, categories: [] })
    await flushPromises()
    await wrapper.vm.$nextTick()

    // 套用中的分類仍必須出現在選項裡，而且是被選中的那一個
    expect(optionValues()).toContain('CPU')
    expect((select().element as HTMLSelectElement).value).toBe('CPU')
    // 名稱走本地對照表，深連結進來也顯示中文而不是代碼
    expect(select().find('option[value="CPU"]').text()).toBe('處理器')

    // route query 仍是唯一來源，載入選項不得 push route
    expect(wrapper.vm.$route.query.category).toBe('CPU')
    expect(wrapper.vm.$route.fullPath).toBe('/products?category=CPU')
    // 也不得因為同步控制項而多打一次搜尋
    expect(mockSearchProducts).toHaveBeenCalledTimes(1)
    expect(mockSearchProducts).toHaveBeenLastCalledWith(expect.objectContaining({ category: 'CPU' }))
  })

  it('does not clobber an unapplied category edit when filter options arrive', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)

    let resolveOptions: (value: unknown) => void = () => {}
    mockGetCatalogFilterOptions.mockReturnValue(new Promise((resolve) => { resolveOptions = resolve }))

    const wrapper = await mountPage('/products?category=CPU')
    await flushPromises()

    // 使用者在選項到齊前就先把草稿改回「全部分類」
    await wrapper.find('select[aria-label="分類"]').setValue('')

    resolveOptions({
      ...filterOptionsWithSpec,
      categories: [
        { publicId: 'c-cpu', code: 'CPU', name: '處理器' },
        { publicId: 'c-gpu', code: 'GPU', name: '顯示卡' },
      ],
    })
    await flushPromises()
    await wrapper.vm.$nextTick()

    // 尚未套用的編輯不得被選項載入蓋掉
    expect((wrapper.find('select[aria-label="分類"]').element as HTMLSelectElement).value).toBe('')
    // route query 沒被動到
    expect(wrapper.vm.$route.query.category).toBe('CPU')
  })

  it('re-syncs the category control on query-only back/forward navigation', async () => {
    mockSearchProducts.mockResolvedValue(emptyResult)
    mockGetCatalogFilterOptions.mockResolvedValue({ ...filterOptionsWithSpec, categories: [] })

    const wrapper = await mountPage('/products?category=CPU')
    await flushPromises()
    await wrapper.vm.$nextTick()

    const select = () => wrapper.find('select[aria-label="分類"]')
    expect((select().element as HTMLSelectElement).value).toBe('CPU')

    await wrapper.vm.$router.push('/products?category=GPU')
    await flushPromises()
    await wrapper.vm.$nextTick()

    expect((select().element as HTMLSelectElement).value).toBe('GPU')

    await wrapper.vm.$router.back()
    await flushPromises()
    await wrapper.vm.$nextTick()

    expect((select().element as HTMLSelectElement).value).toBe('CPU')
  })
})

import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
// isApiError 用 instanceof 判斷，所以測試裡必須是真的 ApiError 實例。
import { ApiError } from '@doselect/web-shared/api'

const mockListAdminProducts = vi.fn()
const mockListBrands = vi.fn()
const mockListCategories = vi.fn()
const mockApplyBulkProductAction = vi.fn()
const mockExportAdminProducts = vi.fn()

vi.mock('../features/products/api', () => ({
  listAdminProducts: mockListAdminProducts,
  getAdminProduct: vi.fn(),
  createProduct: vi.fn(),
  updateProduct: vi.fn(),
  applyBulkProductAction: (...args: unknown[]) => mockApplyBulkProductAction(...args),
  exportAdminProducts: (...args: unknown[]) => mockExportAdminProducts(...args),
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

  // -------------------------------------------------------------------------
  // UC-ADM-PROD-02 批次操作
  // -------------------------------------------------------------------------

  function product(overrides: Record<string, unknown> = {}) {
    return {
      publicId: 'product-1',
      productCode: 'PROD-1',
      nameZhTw: '測試商品',
      brand: { code: 'BRAND-1', name: 'Brand 1' },
      category: { code: 'CAT-1', name: 'Category 1' },
      status: 'Draft',
      skuCount: 1,
      minPrice: 100,
      maxPrice: 100,
      totalOnHandQuantity: 5,
      primaryImage: null,
      updatedAtUtc: '2026-09-01T00:00:00Z',
      rowVersion: 'AAAAAAAAB9E=',
      ...overrides,
    }
  }

  function page(items: unknown[]) {
    return { items, pageNumber: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }
  }

  function stubLookups() {
    mockListBrands.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListCategories.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
  }

  it('sends the selected products with their RowVersions when publishing in bulk', async () => {
    stubLookups()
    mockListAdminProducts.mockResolvedValue(page([
      product({ publicId: 'product-1', rowVersion: 'RV-1' }),
      product({ publicId: 'product-2', productCode: 'PROD-2', nameZhTw: '另一項', rowVersion: 'RV-2' }),
    ]))
    mockApplyBulkProductAction.mockResolvedValue({
      action: 'publish', affectedProductCount: 1, affectedSkuCount: 0,
    })

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('input[aria-label="選取 測試商品"]').setValue(true)
    await wrapper.findAll('button').find((button) => button.text() === '批次上架')!.trigger('click')
    await flushPromises()

    // RowVersion 必須跟著送：後端靠它做樂觀鎖，只送 PublicId 的話別人的修改會被無聲覆蓋。
    expect(mockApplyBulkProductAction).toHaveBeenCalledWith('publish', expect.objectContaining({
      productPublicIds: ['product-1'],
      rowVersions: [{ productPublicId: 'product-1', rowVersion: 'RV-1' }],
    }))
    expect(wrapper.text()).toContain('已上架 1 項商品')
  })

  /**
   * 組長在 PR #78 item 1 與 PR #79 item 1／3 指出的同一個反模式。這裡是它最尖銳的形式：改了篩選
   * 之後，若畫面還留著上一組結果，那些列是「勾得動」的——管理員會對根本不在眼前清單裡的商品
   * 批次上下架。第二次請求刻意卡住，斷言舊列在 pending 期間就已經不在畫面上。
   */
  it('drops the previous page selection and rows while a new filter is still loading', async () => {
    stubLookups()
    mockListAdminProducts.mockResolvedValueOnce(page([
      product({ publicId: 'product-1', nameZhTw: '舊篩選商品' }),
    ]))

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('input[aria-label="選取 舊篩選商品"]').setValue(true)
    expect(wrapper.text()).toContain('已選取 1 項')

    let releaseSecond!: (value: unknown) => void
    mockListAdminProducts.mockImplementationOnce(
      () => new Promise((resolve) => { releaseSecond = resolve }))

    await wrapper.find('input[aria-label="關鍵字"]').setValue('新條件')
    await wrapper.find('form[aria-label="商品篩選"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).not.toContain('舊篩選商品')
    expect(wrapper.find('input[aria-label="選取 舊篩選商品"]').exists()).toBe(false)

    releaseSecond(page([product({ publicId: 'product-9', nameZhTw: '新篩選商品' })]))
    await flushPromises()
    expect(wrapper.text()).toContain('新篩選商品')
    // 換了清單就不該還記著上一組的選取。
    expect(wrapper.text()).toContain('已選取 0 項')
  })

  it('requires a mode, value and reason before a bulk price change can be submitted', async () => {
    stubLookups()
    mockListAdminProducts.mockResolvedValue(page([product()]))

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('input[aria-label="選取 測試商品"]').setValue(true)
    await wrapper.findAll('button').find((button) => button.text() === '批次調價')!.trigger('click')

    const submit = wrapper.findAll('button').find((button) => button.text() === '套用調價')!
    expect(submit.attributes('disabled')).toBeDefined()

    await wrapper.find('input[aria-label="調價百分比"]').setValue('-10')
    await wrapper.find('input[aria-label="調價原因"]').setValue('季末促銷')
    expect(wrapper.findAll('button').find((button) => button.text() === '套用調價')!
      .attributes('disabled')).toBeUndefined()

    // v-model.number 清空後給的是空字串而不是 null——只比對 null 的話按鈕會重新啟用，
    // 送出一次 value: 0 的無效調價。
    await wrapper.find('input[aria-label="調價百分比"]').setValue('')
    expect(wrapper.findAll('button').find((button) => button.text() === '套用調價')!
      .attributes('disabled')).toBeDefined()
  })

  it('sends the price adjustment mode, value and reason', async () => {
    stubLookups()
    mockListAdminProducts.mockResolvedValue(page([product({ rowVersion: 'RV-1' })]))
    mockApplyBulkProductAction.mockResolvedValue({
      action: 'adjust-price', affectedProductCount: 1, affectedSkuCount: 3,
    })

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('input[aria-label="選取 測試商品"]').setValue(true)
    await wrapper.findAll('button').find((button) => button.text() === '批次調價')!.trigger('click')
    await wrapper.find('input[aria-label="調價百分比"]').setValue('-10')
    await wrapper.find('input[aria-label="調價原因"]').setValue('季末促銷')
    await wrapper.find('form[aria-label="批次調價"]').trigger('submit')
    await flushPromises()

    expect(mockApplyBulkProductAction).toHaveBeenCalledWith('adjust-price', expect.objectContaining({
      priceAdjustment: { mode: 'percentage', value: -10, reason: '季末促銷' },
    }))
    expect(wrapper.text()).toContain('共 3 個 SKU')
  })

  /**
   * 後端是整批單一交易，所以失敗訊息一定要說「整批未執行」——否則管理員不知道是不是有一半已經
   * 生效，只能一個一個去翻。
   */
  it('says the whole batch was rejected when a discontinued product is included', async () => {
    stubLookups()
    mockListAdminProducts.mockResolvedValue(page([product()]))
    mockApplyBulkProductAction.mockRejectedValue(
      new ApiError('conflict', { status: 409, code: 'product_unavailable' }))

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('input[aria-label="選取 測試商品"]').setValue(true)
    await wrapper.findAll('button').find((button) => button.text() === '批次上架')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('已停產')
    expect(wrapper.text()).toContain('整批未執行')
  })

  /**
   * 「匯出沿用目前 Filter」：送出去的必須是已套用的篩選（Route Query），不是草稿表單裡還沒按
   * 「套用篩選」的值。
   */
  it('exports with the applied filters rather than the draft form values', async () => {
    stubLookups()
    mockListAdminProducts.mockResolvedValue(page([product()]))
    mockExportAdminProducts.mockResolvedValue(new Blob(['code'], { type: 'text/csv' }))

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('input[aria-label="關鍵字"]').setValue('已套用')
    await wrapper.find('form[aria-label="商品篩選"]').trigger('submit')
    await flushPromises()

    // 套用之後又改了草稿但沒再送出——匯出不能帶到這個值。
    await wrapper.find('input[aria-label="關鍵字"]').setValue('只是草稿')
    await wrapper.findAll('button').find((button) => button.text() === '匯出 CSV')!.trigger('click')
    await flushPromises()

    expect(mockExportAdminProducts).toHaveBeenCalledWith(
      expect.objectContaining({ q: '已套用' }),
      'csv',
    )
  })

  it('exports xlsx when the xlsx button is used', async () => {
    stubLookups()
    mockListAdminProducts.mockResolvedValue(page([product()]))
    mockExportAdminProducts.mockResolvedValue(new Blob(['PK'], { type: 'application/vnd.ms-excel' }))

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '匯出 XLSX')!.trigger('click')
    await flushPromises()

    expect(mockExportAdminProducts).toHaveBeenCalledWith(expect.anything(), 'xlsx')
  })
})

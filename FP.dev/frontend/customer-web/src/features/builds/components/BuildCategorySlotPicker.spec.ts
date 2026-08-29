import { mount } from '@vue/test-utils'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import BuildCategorySlotPicker from './BuildCategorySlotPicker.vue'
import type { ProductCardDto, ProductDetailDto } from '../../catalog/types'

const mockSearchProducts = vi.fn()
const mockGetProductDetail = vi.fn()

vi.mock('../../catalog/api', () => ({
  searchProducts: (...args: unknown[]) => mockSearchProducts(...args),
  getProductDetail: (...args: unknown[]) => mockGetProductDetail(...args),
}))

function productCard(overrides: Partial<ProductCardDto> = {}): ProductCardDto {
  return {
    productPublicId: 'p1',
    defaultSkuPublicId: 'sku-default',
    productCode: 'P1',
    skuCode: 'SKU-DEFAULT',
    name: '測試商品',
    brand: { code: 'ACME', name: 'Acme' },
    category: { code: 'CPU', name: 'CPU' },
    price: { list: 1000, sale: null, currency: 'TWD' },
    availability: 'inStock',
    primaryImage: null,
    badges: [],
    ...overrides,
  }
}

function productDetail(overrides: Partial<ProductDetailDto> = {}): ProductDetailDto {
  return {
    productPublicId: 'p1',
    defaultSkuPublicId: 'sku-default',
    productCode: 'P1',
    skuCode: 'SKU-DEFAULT',
    name: '測試商品',
    brand: { code: 'ACME', name: 'Acme' },
    category: { code: 'CPU', name: 'CPU' },
    price: { list: 1000, sale: null, currency: 'TWD' },
    availability: 'inStock',
    primaryImage: null,
    badges: [],
    description: null,
    tags: [],
    images: [],
    skus: [{
      publicId: 'sku-default',
      skuCode: 'SKU-DEFAULT',
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

beforeEach(() => {
  mockSearchProducts.mockReset()
  mockGetProductDetail.mockReset()
})

async function typeQuery(wrapper: ReturnType<typeof mount>, value: string): Promise<void> {
  await wrapper.find('input').setValue(value)
  await new Promise((resolve) => setTimeout(resolve, 320))
}

describe('BuildCategorySlotPicker', () => {
  /**
   * 組長 PR #35 round-2 review, P1-3: picking a search result used to emit
   * `product.defaultSkuPublicId` directly, so a product's non-default SKUs (variants) were never
   * reachable through this picker at all — a product pick was being treated as a SKU pick. A
   * product with more than one SKU must show them and let the shopper choose, and picking a
   * non-default one must emit *that* SKU's own PublicId, not the product's default.
   */
  it('lists a multi-SKU product\'s real SKUs after picking it, and emits the chosen (non-default) SKU\'s own PublicId', async () => {
    mockSearchProducts.mockResolvedValue({ items: [productCard()], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    mockGetProductDetail.mockResolvedValue(productDetail({
      skus: [
        {
          publicId: 'sku-default', skuCode: 'SKU-DEFAULT', name: 'Default', price: { list: 1000, sale: null, currency: 'TWD' },
          availability: 'inStock', maxPurchasableQuantity: 10, specifications: [],
          dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null }, isDefault: true,
        },
        {
          publicId: 'sku-variant', skuCode: 'SKU-VARIANT', name: 'Variant (32GB)', price: { list: 1500, sale: null, currency: 'TWD' },
          availability: 'inStock', maxPurchasableQuantity: 10, specifications: [],
          dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null }, isDefault: false,
        },
      ],
    }))

    const wrapper = mount(BuildCategorySlotPicker, { props: { categoryCode: 'CPU' } })
    await typeQuery(wrapper, '測試')
    await vi.waitFor(() => expect(wrapper.text()).toContain('測試商品'))

    await wrapper.findAll('button').find((button) => button.text().includes('測試商品'))!.trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('Variant (32GB)'))
    // Both SKUs are offered, not just the default — proving the product pick did not collapse to it.
    expect(wrapper.text()).toContain('Default')

    const emitted = wrapper.emitted('select')
    expect(emitted).toBeUndefined()

    await wrapper.findAll('button').find((button) => button.text().includes('Variant (32GB)'))!.trigger('click')

    expect(wrapper.emitted('select')![0]).toEqual([{ skuPublicId: 'sku-variant', skuCode: 'SKU-VARIANT', name: 'Variant (32GB)' }])
  })

  it('resolves immediately without an extra SKU-list step when the picked product has only one SKU', async () => {
    mockSearchProducts.mockResolvedValue({ items: [productCard()], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    mockGetProductDetail.mockResolvedValue(productDetail())

    const wrapper = mount(BuildCategorySlotPicker, { props: { categoryCode: 'CPU' } })
    await typeQuery(wrapper, '測試')
    await vi.waitFor(() => expect(wrapper.text()).toContain('測試商品'))

    await wrapper.findAll('button').find((button) => button.text().includes('測試商品'))!.trigger('click')

    await vi.waitFor(() => expect(wrapper.emitted('select')).toBeDefined())
    expect(wrapper.emitted('select')![0]).toEqual([{ skuPublicId: 'sku-default', skuCode: 'SKU-DEFAULT', name: 'Default' }])
  })

  /**
   * 組長 PR #35 round-2 review, P2-9: `searchToken` used to increment only once a debounced
   * search actually started, so clearing/changing the query while a *previous* search was still
   * in flight didn't invalidate it — only cancelled the not-yet-fired debounce timer. That
   * in-flight response could still land after the query changed and re-open results for a query
   * the input no longer shows.
   */
  it('does not show a stale search\'s results after the query was cleared before that search resolved', async () => {
    let resolveSearchA!: (page: unknown) => void
    mockSearchProducts.mockImplementationOnce(() => new Promise((resolve) => { resolveSearchA = resolve }))

    const wrapper = mount(BuildCategorySlotPicker, { props: { categoryCode: 'CPU' } })
    await wrapper.find('input').setValue('A')
    await new Promise((resolve) => setTimeout(resolve, 320))
    expect(mockSearchProducts).toHaveBeenCalledTimes(1)

    // The shopper clears the box before A's (slow) search comes back.
    await wrapper.find('input').setValue('')

    resolveSearchA({ items: [productCard({ name: 'A 的結果' })], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(wrapper.text()).not.toContain('A 的結果')
    expect(wrapper.find('.slot-picker__results').exists()).toBe(false)
  })

  it('does not show search A\'s results even if A resolves after B\'s own (different) results already rendered', async () => {
    let resolveSearchA!: (page: unknown) => void
    mockSearchProducts.mockImplementationOnce(() => new Promise((resolve) => { resolveSearchA = resolve }))

    const wrapper = mount(BuildCategorySlotPicker, { props: { categoryCode: 'CPU' } })
    await wrapper.find('input').setValue('A')
    await new Promise((resolve) => setTimeout(resolve, 320))

    mockSearchProducts.mockResolvedValueOnce({
      items: [productCard({ name: 'B 的結果' })], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1,
    })
    await wrapper.find('input').setValue('B')
    await new Promise((resolve) => setTimeout(resolve, 320))
    await vi.waitFor(() => expect(wrapper.text()).toContain('B 的結果'))

    // A's slow response finally arrives — after B's own, correct results are already showing.
    resolveSearchA({ items: [productCard({ name: 'A 的結果' })], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(wrapper.text()).toContain('B 的結果')
    expect(wrapper.text()).not.toContain('A 的結果')
  })
})

describe('BuildCategorySlotPicker — stale loading/error state after invalidation (組長 PR #35 round-6 review, P2-1)', () => {
  it('does not stay stuck showing "搜尋中…" after the query is cleared while that search is still in flight', async () => {
    let resolveSearchA!: (page: unknown) => void
    mockSearchProducts.mockImplementationOnce(() => new Promise((resolve) => { resolveSearchA = resolve }))

    const wrapper = mount(BuildCategorySlotPicker, { props: { categoryCode: 'CPU' } })
    await wrapper.find('input').setValue('A')
    await new Promise((resolve) => setTimeout(resolve, 320))
    expect(wrapper.text()).toContain('搜尋中')

    // Cleared before A's (slow) search resolves — the cleared-query branch never fires a new
    // request, so nothing else would ever flip `isSearching` back to false on its own.
    await wrapper.find('input').setValue('')
    expect(wrapper.text()).not.toContain('搜尋中')

    resolveSearchA({ items: [productCard()], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(wrapper.text()).not.toContain('搜尋中')
  })

  it('does not stay stuck showing "載入規格中…" (with results disabled) after the query changes while a SKU-detail fetch is still in flight', async () => {
    mockSearchProducts.mockResolvedValue({ items: [productCard()], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    let resolveDetail!: (detail: unknown) => void
    mockGetProductDetail.mockImplementationOnce(() => new Promise((resolve) => { resolveDetail = resolve }))

    const wrapper = mount(BuildCategorySlotPicker, { props: { categoryCode: 'CPU' } })
    await typeQuery(wrapper, '測試')
    await vi.waitFor(() => expect(wrapper.text()).toContain('測試商品'))

    await wrapper.findAll('button').find((button) => button.text().includes('測試商品'))!.trigger('click')
    expect(wrapper.text()).toContain('載入規格中')

    // The shopper starts a new search before the multi-SKU product's detail fetch resolves.
    mockSearchProducts.mockResolvedValueOnce({ items: [], pageNumber: 1, pageSize: 10, totalCount: 0, totalPages: 0 })
    await wrapper.find('input').setValue('別的商品')
    expect(wrapper.text()).not.toContain('載入規格中')

    resolveDetail(productDetail({
      skus: [
        {
          publicId: 'sku-default', skuCode: 'SKU-DEFAULT', name: 'Default', price: { list: 1000, sale: null, currency: 'TWD' },
          availability: 'inStock', maxPurchasableQuantity: 10, specifications: [],
          dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null }, isDefault: true,
        },
        {
          publicId: 'sku-variant', skuCode: 'SKU-VARIANT', name: 'Variant', price: { list: 1500, sale: null, currency: 'TWD' },
          availability: 'inStock', maxPurchasableQuantity: 10, specifications: [],
          dimensions: { weightKg: null, lengthCm: null, widthCm: null, heightCm: null }, isDefault: false,
        },
      ],
    }))
    await new Promise((resolve) => setTimeout(resolve, 0))
    // Late detail response must not reopen the abandoned product's SKU list either.
    expect(wrapper.text()).not.toContain('載入規格中')
    expect(wrapper.text()).not.toContain('Variant')
  })

  it('does not leave a stale "搜尋失敗，請重試。" showing after the query is cleared following a failed search', async () => {
    let rejectSearchA!: (error: unknown) => void
    mockSearchProducts.mockImplementationOnce(() => new Promise((_resolve, reject) => { rejectSearchA = reject }))

    const wrapper = mount(BuildCategorySlotPicker, { props: { categoryCode: 'CPU' } })
    await wrapper.find('input').setValue('A')
    await new Promise((resolve) => setTimeout(resolve, 320))
    rejectSearchA(new Error('boom'))
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(wrapper.text()).toContain('搜尋失敗')

    await wrapper.find('input').setValue('')
    expect(wrapper.text()).not.toContain('搜尋失敗')
  })
})

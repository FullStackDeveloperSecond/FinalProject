import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'

const mockGetAdminProduct = vi.fn()
const mockCreateProduct = vi.fn()
const mockUpdateProduct = vi.fn()
const mockListBrands = vi.fn()
const mockListCategories = vi.fn()
const mockListTags = vi.fn()

vi.mock('../features/products/api', () => ({
  getAdminProduct: mockGetAdminProduct,
  updateProduct: mockUpdateProduct,
  createProduct: mockCreateProduct,
  listAdminProducts: vi.fn(),
}))
vi.mock('../features/brands/api', () => ({ listBrands: mockListBrands }))
vi.mock('../features/categories/api', () => ({ listCategories: mockListCategories }))
vi.mock('../features/tags/api', () => ({ listTags: mockListTags }))
const mockCreateSku = vi.fn()
vi.mock('../features/skus/api', () => ({ createSku: mockCreateSku, updateSku: vi.fn(), deleteSku: vi.fn() }))

const { default: ProductEditPage } = await import('./ProductEditPage.vue')

const product = {
  publicId: 'p1',
  productCode: 'P1',
  nameZhTw: 'Product 1',
  brand: { code: 'ACME', name: 'Acme' },
  category: { code: 'CAT-A', name: 'Category A' },
  descriptionZhTw: null,
  warrantyMonths: null,
  status: 'Draft',
  isFeatured: false,
  // This tag is no longer in the active tag lookup below (deactivated after being assigned).
  tags: [{ code: 'LEGACY-TAG', name: 'Legacy Tag' }],
  images: [],
  skus: [],
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: '2026-01-01T00:00:00Z',
  rowVersion: 'AAA=',
}

function pendingForever() {
  return new Promise(() => {})
}

async function mountPage(id: string | null = 'p1') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/products/:productId', name: 'product-edit', component: ProductEditPage, props: true },
      { path: '/products/new', name: 'product-new', component: ProductEditPage },
    ],
  })
  await router.push(id ? `/products/${id}` : '/products/new')
  await router.isReady()
  return mount(ProductEditPage, {
    props: id ? { productId: id } : {},
    global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
  })
}

describe('ProductEditPage', () => {
  it('creates a product together with its first default SKU', async () => {
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockCreateProduct.mockResolvedValue({
      ...product,
      publicId: 'created-product',
      skus: [{ publicId: 'created-sku', isDefault: true }],
    })

    const wrapper = await mountPage(null)
    await flushPromises()

    await wrapper.find('input[aria-label="商品代碼"]').setValue('PROD-1')
    await wrapper.find('input[aria-label="商品名稱"]').setValue('Product 1')
    await wrapper.find('select[aria-label="品牌"]').setValue('brand-1')
    await wrapper.find('select[aria-label="分類"]').setValue('cat-1')
    await wrapper.find('input[aria-label="預設 SKU 代碼"]').setValue('SKU-1')
    await wrapper.find('input[aria-label="預設 SKU 名稱"]').setValue('標準版')
    await wrapper.find('input[aria-label="預設 SKU 售價"]').setValue('1000')
    await wrapper.find('input[aria-label="預設 SKU 成本"]').setValue('700')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockCreateProduct).toHaveBeenCalledWith(expect.objectContaining({
      productCode: 'PROD-1',
      defaultSku: expect.objectContaining({
        skuCode: 'SKU-1',
        nameZhTw: '標準版',
        listPrice: 1000,
        unitCost: 700,
        isDefault: true,
      }),
    }))
  })

  it('offers every Product and SKU lifecycle state', async () => {
    mockListBrands.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListCategories.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListTags.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })

    const wrapper = await mountPage(null)
    await flushPromises()

    const productStatuses = wrapper.find('select[aria-label="商品狀態"]').findAll('option').map((option) => option.attributes('value'))
    const skuStatuses = wrapper.find('select[aria-label="預設 SKU 狀態"]').findAll('option').map((option) => option.attributes('value'))
    expect(productStatuses).toEqual(['Draft', 'Published', 'Unpublished', 'Discontinued'])
    expect(skuStatuses).toEqual(['Draft', 'Published', 'Unpublished'])
  })

  /**
   * PR #24 review: a tag deactivated after being assigned must not silently drop off the
   * form's tagCodes just because it's missing from the isActive-only, page-capped lookup.
   */
  it('preserves an existing tag association that is no longer in the active tag lookup', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    // The active-tag lookup no longer includes LEGACY-TAG, but the fetch removes the
    // isActive filter so it still resolves — this row represents that inactive tag.
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockUpdateProduct.mockResolvedValueOnce(product)

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockUpdateProduct).toHaveBeenCalledWith('p1', expect.objectContaining({
      tagPublicIds: ['tag-legacy'],
    }))
  })

  /** PR #24 review: submit must be blocked until brand/category/tag lookups finish, not just the product itself. */
  it('disables the save button while the brand/category/tag lookups are still pending', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockReturnValue(pendingForever())
    mockListCategories.mockReturnValue(pendingForever())
    mockListTags.mockReturnValue(pendingForever())

    const wrapper = await mountPage()
    await flushPromises()

    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存')
    expect(saveButton!.attributes('disabled')).toBeDefined()
  })

  /**
   * PR #24 review round 2: `CatalogLookupListRequest.PageSize` is capped at [Range(1, 100)]
   * server-side — a flat pageSize:500 request is rejected outright (400 validation_failed).
   * Asserts against the real backend constraint, not just a mocked "it resolved" response.
   */
  it('never requests more than the backend-allowed pageSize when resolving lookups', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockImplementation((params: { pageSize?: number }) => {
      if ((params.pageSize ?? 0) > 100) {
        return Promise.reject(new Error('pageSize must be between 1 and 100'))
      }
      return Promise.resolve({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: params.pageSize, totalCount: 1 })
    })
    mockListCategories.mockImplementation((params: { pageSize?: number }) => {
      if ((params.pageSize ?? 0) > 100) {
        return Promise.reject(new Error('pageSize must be between 1 and 100'))
      }
      return Promise.resolve({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: params.pageSize, totalCount: 1 })
    })
    mockListTags.mockImplementation((params: { pageSize?: number }) => {
      if ((params.pageSize ?? 0) > 100) {
        return Promise.reject(new Error('pageSize must be between 1 and 100'))
      }
      return Promise.resolve({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: params.pageSize, totalCount: 1 })
    })

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.findAll('button').find((button) => button.text() === '儲存')!.attributes('disabled')).toBeUndefined()
  })

  /** PR #24 review round 2: a lookup failure must not leave submit silently enabled — it can no longer prove existing associations resolve safely. */
  it('disables save and shows a retry when a lookup fails', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockRejectedValue(new Error('network error'))
    mockListCategories.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockListTags.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })

    const wrapper = await mountPage()
    await flushPromises()

    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存')
    expect(saveButton!.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('載入失敗')
  })

  it('restores the existing brand and category after a failed lookup retry succeeds', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands
      .mockRejectedValueOnce(new Error('network error'))
      .mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })

    const wrapper = await mountPage()
    await flushPromises()
    expect(wrapper.text()).toContain('載入失敗')

    await wrapper.findAll('button').find((button) => button.text() === '重試')!.trigger('click')
    await flushPromises()

    expect((wrapper.find('select[aria-label="品牌"]').element as HTMLSelectElement).value).toBe('brand-1')
    expect((wrapper.find('select[aria-label="分類"]').element as HTMLSelectElement).value).toBe('cat-1')
  })

  /**
   * Regression test (組長 PR #24 review round 3, item 4): a background refetch of a lookup
   * (e.g. a brand created elsewhere invalidating ['brands'], or Vue Query's window-refocus
   * refetch) used to re-run the whole product-field initializer, silently discarding whatever
   * the admin had typed into the form and replacing it with the last-saved server values.
   */
  it('does not discard an in-progress edit when a lookup refetches in the background', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/products/:productId', name: 'product-edit', component: ProductEditPage, props: true }],
    })
    await router.push('/products/p1')
    await router.isReady()
    const wrapper = mount(ProductEditPage, {
      props: { productId: 'p1' },
      global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
    })
    await flushPromises()

    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    await nameInput.setValue('Product 1（管理員正在編輯中）')
    expect(nameInput.element.value).toBe('Product 1（管理員正在編輯中）')

    // Simulate a brand created in another tab invalidating the shared 'brands' cache — the
    // full-list picker on this page refetches as a background query while the admin is typing.
    mockListBrands.mockResolvedValueOnce({
      items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }, { publicId: 'brand-2', code: 'OTHER', nameZhTw: 'Other' }],
      pageNumber: 1,
      pageSize: 100,
      totalCount: 2,
    })
    await queryClient.invalidateQueries({ queryKey: ['brands'] })
    await flushPromises()

    expect(nameInput.element.value).toBe('Product 1（管理員正在編輯中）')
  })

  /**
   * Regression test (組長 PR #24 round 7 review, P1). Round 6 stopped a background refetch from
   * re-populating the form fields, but submitUpdate() still read product.value.rowVersion live —
   * so a refetch caused by something other than this admin's own action (another admin's edit
   * landing via window-refocus, an unrelated cache invalidation) would silently swap in a newer
   * token. Submitting would then send that newer token, the server's optimistic-concurrency check
   * would see a "match" against the row's current state, and accept the write — discarding
   * whatever the other change was with no 409 ever raised. The token actually used on submit must
   * stay pinned to what it was when editing began, exactly like the form fields.
   */
  it('submits with the RowVersion captured when editing began, not one picked up by an unrelated background refetch', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockUpdateProduct.mockResolvedValueOnce(product)

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/products/:productId', name: 'product-edit', component: ProductEditPage, props: true }],
    })
    await router.push('/products/p1')
    await router.isReady()
    const wrapper = mount(ProductEditPage, {
      props: { productId: 'p1' },
      global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
    })
    await flushPromises()

    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    await nameInput.setValue('Product 1（管理員正在編輯中）')
    expect(nameInput.element.value).toBe('Product 1（管理員正在編輯中）')

    // Simulates a cause unrelated to this admin's own actions on this page: another admin saved
    // a change to the same product elsewhere, and this page's query picked it up via an
    // unrelated invalidation/window-refocus refetch.
    mockGetAdminProduct.mockResolvedValueOnce({ ...product, rowVersion: 'BBB=' })
    await queryClient.invalidateQueries({ queryKey: ['admin-products', 'detail', 'p1'] })
    await flushPromises()

    expect(nameInput.element.value).toBe('Product 1（管理員正在編輯中）')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockUpdateProduct).toHaveBeenCalledWith('p1', expect.objectContaining({
      nameZhTw: 'Product 1（管理員正在編輯中）',
      rowVersion: 'AAA=',
    }))
  })

  /**
   * Regression test (組長 PR #24 round 7 review, P1, point 3): the one deliberate exception to
   * the rule above — a SKU mutation this admin performs *on this page* legitimately advances the
   * Product's RowVersion (Product.Touch(), round 5), and the next product save should be checked
   * against that new value rather than being stuck on a token the server will now reject as
   * stale. Distinguishing this from an arbitrary background refetch is the point: this only
   * happens via the explicit onSuccess handler of a mutation the admin themselves just fired.
   */
  it('syncs the concurrency token, but not the form fields, after this page creates a SKU', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockCreateSku.mockResolvedValueOnce({ publicId: 'sku-new', skuCode: 'NEW-1', isDefault: false })
    mockUpdateProduct.mockResolvedValueOnce(product)

    const wrapper = await mountPage()
    await flushPromises()

    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    await nameInput.setValue('Product 1（管理員正在編輯中）')

    // The mutation's own onSuccess invalidation and this page's explicit sync refetch both race
    // to refetch the same query — like two real idempotent GETs against an unchanged server
    // state, both must consistently see the same post-mutation value.
    mockGetAdminProduct.mockResolvedValue({ ...product, rowVersion: 'BBB=' })
    await wrapper.find('input[aria-label="新 SKU 代碼"]').setValue('NEW-1')
    await wrapper.find('input[aria-label="新 SKU 名稱"]').setValue('New SKU')
    await wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!.trigger('click')
    await flushPromises()

    expect(nameInput.element.value).toBe('Product 1（管理員正在編輯中）')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockUpdateProduct).toHaveBeenCalledWith('p1', expect.objectContaining({
      nameZhTw: 'Product 1（管理員正在編輯中）',
      rowVersion: 'BBB=',
    }))
  })

  /**
   * Regression test (組長 PR #24 review round 7, P2): useCreateSku(props.productId ?? '') used
   * to capture the productId once as a plain string at setup time. Vue Router reuses this same
   * component instance across a param-only navigation on the same route record — the query and
   * form already switch to the new product, but the mutation's captured productId stayed on the
   * old one, so "新增 SKU" on a page visibly showing product B could silently write the new SKU
   * onto product A instead and invalidate A's cache entry rather than B's.
   */
  it('creates a new SKU against the product currently shown, not the one this instance first loaded', async () => {
    mockGetAdminProduct.mockImplementation((publicId: string) =>
      Promise.resolve({ ...product, publicId, productCode: publicId.toUpperCase() }))
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockCreateSku.mockResolvedValueOnce({ publicId: 'sku-new', skuCode: 'NEW-1', isDefault: false })

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/products/:productId', name: 'product-edit', component: ProductEditPage, props: true }],
    })
    await router.push('/products/p1')
    await router.isReady()
    // Mounted through RouterView (not with a static `props:` object) so `props: true` on the
    // route actually re-injects the current route param reactively on navigation, the same way
    // it does in the real app — a directly-mounted instance with a fixed prop would never
    // exercise this bug at all.
    const wrapper = mount(RouterView, {
      global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
    })
    await flushPromises()

    // Param-only navigation on the same matched route record — Vue Router reuses this instance
    // rather than remounting it.
    await router.push('/products/p2')
    await flushPromises()

    await wrapper.find('input[aria-label="新 SKU 代碼"]').setValue('NEW-1')
    await wrapper.find('input[aria-label="新 SKU 名稱"]').setValue('New SKU')
    await wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!.trigger('click')
    await flushPromises()

    expect(mockCreateSku).toHaveBeenCalledWith('p2', expect.objectContaining({ skuCode: 'NEW-1' }))
  })

  /**
   * Regression test (組長 PR #24 round 4 review, item 5): GetById genuinely returns 404 for an
   * unknown product, so isError fired first and "找不到這個商品" was unreachable dead code.
   */
  it('shows a 404 page instead of the generic error state when the product does not exist', async () => {
    mockGetAdminProduct.mockRejectedValue(new ApiError('Not found', { status: 404, code: 'resource_not_found' }))

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('找不到頁面')
    expect(wrapper.text()).not.toContain('無法載入')
  })

  it('shows the generic retryable error state for a non-404 failure', async () => {
    mockGetAdminProduct.mockRejectedValue(new ApiError('Server error', { status: 500, code: 'unexpected_error' }))

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).not.toContain('找不到頁面')
  })
})

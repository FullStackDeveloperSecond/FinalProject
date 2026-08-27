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
   * Regression test (組長 PR #24 round 8 review, P1): round 7's `syncRowVersionAfterOwnSkuMutation`
   * refreshed only the token after a SKU write, leaving stale form fields paired with an advanced
   * token — round 8 found this still lets a SKU write silently ride past a change another admin
   * made to the *product* fields (the SKU write only validates its own token, not the Product's).
   * The minimal safe fix: disable SKU operations entirely while the product form has unsaved
   * edits, so a SKU write can only ever happen when the form already matches the server.
   */
  it('disables SKU operations while the product form has unsaved edits', async () => {
    mockGetAdminProduct.mockResolvedValue({
      ...product,
      skus: [{
        publicId: 'sku-1', skuCode: 'SKU-1', nameZhTw: 'Existing SKU', listPrice: 100, unitCost: 60,
        weightKg: null, lengthCm: null, widthCm: null, heightCm: null, status: 'Draft',
        isDefault: true, requiresPrepayment: false, specifications: [], inventory: null,
        rowVersion: 'SKU-AAA=',
      }],
    })
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })

    const wrapper = await mountPage()
    await flushPromises()

    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    await nameInput.setValue('Product 1（管理員正在編輯中）')
    await flushPromises()

    const addSkuButton = wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!
    expect(addSkuButton.attributes('disabled')).toBeDefined()

    const editSkuButton = wrapper.findAll('button').find((button) => button.text() === '編輯')!
    expect(editSkuButton.attributes('disabled')).toBeDefined()

    // Clicking a disabled edit button never opens the row for edit, so the 儲存/取消 pair never
    // appears — confirms the guard actually blocks the action, not just the button's own state.
    await editSkuButton.trigger('click')
    expect(wrapper.findAll('button').some((button) => button.text() === '取消')).toBe(false)

    await wrapper.find('input[aria-label="新 SKU 代碼"]').setValue('NEW-1')
    await wrapper.find('input[aria-label="新 SKU 名稱"]').setValue('New SKU')
    await addSkuButton.trigger('click')
    await flushPromises()
    expect(mockCreateSku).not.toHaveBeenCalled()
  })

  /**
   * Regression test (組長 PR #24 round 8 review, P1): once the form has no unsaved edits, SKU
   * creation is allowed again — and because there's nothing unsaved to lose, the resulting resync
   * refreshes the *entire* snapshot (fields included, not just the token), so this admin also
   * sees whatever another admin changed on the product in the meantime instead of silently
   * carrying stale field values forward under a freshly-advanced token.
   */
  it('resyncs both the token and any externally-changed fields after creating a SKU with a clean form', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockCreateSku.mockResolvedValueOnce({ publicId: 'sku-new', skuCode: 'NEW-1', isDefault: false })
    mockUpdateProduct.mockResolvedValueOnce(product)

    const wrapper = await mountPage()
    await flushPromises()

    // Another admin renamed the product between this admin's last sync and this SKU creation —
    // the form was never touched here, so it's safe (and correct) to pick that up too.
    mockGetAdminProduct.mockResolvedValue({ ...product, nameZhTw: 'Renamed By Another Admin', rowVersion: 'BBB=' })
    await wrapper.find('input[aria-label="新 SKU 代碼"]').setValue('NEW-1')
    await wrapper.find('input[aria-label="新 SKU 名稱"]').setValue('New SKU')
    await wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!.trigger('click')
    await flushPromises()

    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    expect(nameInput.element.value).toBe('Renamed By Another Admin')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockUpdateProduct).toHaveBeenCalledWith('p1', expect.objectContaining({
      nameZhTw: 'Renamed By Another Admin',
      rowVersion: 'BBB=',
    }))
  })

  /**
   * Regression test (組長 PR #24 review round 9, P1, scenario 1): being clean when a SKU
   * mutation *starts* doesn't mean still clean when it *succeeds* — nothing disables the product
   * form's text inputs while a SKU write is in flight. If the admin edits the product name during
   * that window, the SKU write's resync must not silently apply the refetch snapshot over that
   * in-progress edit (which would both discard it and validly advance the token the admin is
   * about to submit stale fields against) — it must leave the form as-is and surface an explicit
   * conflict instead.
   */
  it('does not overwrite an edit made while a SKU mutation is in flight, and surfaces a conflict instead of silently advancing the token', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    let resolveCreateSku!: (value: { publicId: string, skuCode: string, isDefault: boolean }) => void
    mockCreateSku.mockReturnValueOnce(new Promise((resolve) => { resolveCreateSku = resolve }))

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('input[aria-label="新 SKU 代碼"]').setValue('NEW-1')
    await wrapper.find('input[aria-label="新 SKU 名稱"]').setValue('New SKU')
    await wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!.trigger('click')
    await flushPromises()

    // The SKU create is still in flight — the admin edits the product name while waiting.
    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    await nameInput.setValue('Edited While SKU Was Saving')

    mockGetAdminProduct.mockResolvedValueOnce({ ...product, rowVersion: 'BBB=' })
    resolveCreateSku({ publicId: 'sku-new', skuCode: 'NEW-1', isDefault: false })
    await flushPromises()

    expect(nameInput.element.value).toBe('Edited While SKU Was Saving')
    expect(wrapper.text()).toContain('無法確定要保留哪一份內容')

    // The token must still be the original — never silently advanced to BBB= behind this edit.
    mockUpdateProduct.mockResolvedValueOnce(product)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockUpdateProduct).toHaveBeenCalledWith('p1', expect.objectContaining({ rowVersion: 'AAA=' }))
  })

  /**
   * Regression test (組長 PR #24 review round 10, P2): `productSyncConflict` (round 9) was never
   * reset — the productId-change watcher reset the SKU draft and mutation errors but not this —
   * so product A's conflict banner stayed lit after navigating to product B, even though B's own
   * snapshot had already loaded cleanly and had nothing to do with A's conflict.
   */
  it('does not carry a conflict banner over to a different product navigated to afterward', async () => {
    mockGetAdminProduct.mockImplementation((publicId: string) =>
      Promise.resolve({ ...product, publicId, productCode: publicId.toUpperCase() }))
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    let resolveCreateSku!: (value: { publicId: string, skuCode: string, isDefault: boolean }) => void
    mockCreateSku.mockReturnValueOnce(new Promise((resolve) => { resolveCreateSku = resolve }))

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/products/:productId', name: 'product-edit', component: ProductEditPage, props: true }],
    })
    await router.push('/products/p1')
    await router.isReady()
    const wrapper = mount(RouterView, { global: { plugins: [[VueQueryPlugin, { queryClient }], router] } })
    await flushPromises()

    await wrapper.find('input[aria-label="新 SKU 代碼"]').setValue('NEW-1')
    await wrapper.find('input[aria-label="新 SKU 名稱"]').setValue('New SKU')
    await wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!.trigger('click')
    await flushPromises()

    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    await nameInput.setValue('Edited While SKU Was Saving')

    mockGetAdminProduct.mockResolvedValueOnce({ ...product, publicId: 'p1', rowVersion: 'BBB=' })
    resolveCreateSku({ publicId: 'sku-new', skuCode: 'NEW-1', isDefault: false })
    await flushPromises()
    expect(wrapper.text()).toContain('無法確定要保留哪一份內容')

    await router.push('/products/p2')
    await flushPromises()

    expect(wrapper.text()).not.toContain('無法確定要保留哪一份內容')
  })

  /**
   * Regression test (組長 PR #24 review round 10, P2): the explicit "重新載入" action must clear
   * the conflict banner, not just refresh the form — round 9's version left it set even after the
   * admin resolved it exactly the way the banner asked them to.
   */
  it('clears the conflict banner once the admin explicitly reloads', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    let resolveCreateSku!: (value: { publicId: string, skuCode: string, isDefault: boolean }) => void
    mockCreateSku.mockReturnValueOnce(new Promise((resolve) => { resolveCreateSku = resolve }))

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('input[aria-label="新 SKU 代碼"]').setValue('NEW-1')
    await wrapper.find('input[aria-label="新 SKU 名稱"]').setValue('New SKU')
    await wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!.trigger('click')
    await flushPromises()

    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    await nameInput.setValue('Edited While SKU Was Saving')

    mockGetAdminProduct.mockResolvedValueOnce({ ...product, rowVersion: 'BBB=' })
    resolveCreateSku({ publicId: 'sku-new', skuCode: 'NEW-1', isDefault: false })
    await flushPromises()
    expect(wrapper.text()).toContain('無法確定要保留哪一份內容')

    mockGetAdminProduct.mockResolvedValueOnce({ ...product, nameZhTw: 'Reloaded From Server', rowVersion: 'CCC=' })
    await wrapper.findAll('button').find((button) => button.text() === '重新載入')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).not.toContain('無法確定要保留哪一份內容')
    expect(nameInput.element.value).toBe('Reloaded From Server')
  })

  /**
   * Regression test (組長 PR #24 review round 9, P1, scenario 2): the success handler used to
   * copy the *live* form into `savedForm`, not what was actually submitted. If the admin edits
   * again (X -> Y) before the save response for X arrives, the server only ever persisted X — the
   * new baseline must be X, so the admin's further edit (now showing Y) correctly stays dirty
   * against what the server actually has, rather than the two silently agreeing on content that
   * was never sent.
   */
  it('uses the submitted value as the new baseline, not whatever the admin typed next before the response arrived', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    let resolveUpdateProduct!: (value: typeof product) => void
    mockUpdateProduct.mockReturnValueOnce(new Promise((resolve) => { resolveUpdateProduct = resolve }))

    const wrapper = await mountPage()
    await flushPromises()

    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    await nameInput.setValue('X')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    // The save for X is still in flight — the admin keeps editing before it resolves.
    await nameInput.setValue('Y')

    // The server only ever received and persisted X.
    resolveUpdateProduct({ ...product, nameZhTw: 'X', rowVersion: 'BBB=' })
    await flushPromises()

    expect(nameInput.element.value).toBe('Y')

    mockUpdateProduct.mockResolvedValueOnce(product)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    // Submitting Y must use the token from the X save (BBB=) — proving the baseline is the
    // server's actual X, and this submit is a genuinely new, still-dirty edit on top of it.
    expect(mockUpdateProduct).toHaveBeenLastCalledWith('p1', expect.objectContaining({
      nameZhTw: 'Y',
      rowVersion: 'BBB=',
    }))
  })

  /**
   * Regression test (組長 PR #24 review round 9, P1): the product save and a SKU write could
   * previously both start independently and overlap, which is exactly the pair of concurrent
   * writes the round 9 P2 backend fix exists to handle — but preventing the overlap from
   * starting at all is cheaper than relying on the server to reject one side.
   */
  it('disables the product save button while a SKU mutation is pending', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    let resolveCreateSku!: (value: { publicId: string, skuCode: string, isDefault: boolean }) => void
    mockCreateSku.mockReturnValueOnce(new Promise((resolve) => { resolveCreateSku = resolve }))

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('input[aria-label="新 SKU 代碼"]').setValue('NEW-1')
    await wrapper.find('input[aria-label="新 SKU 名稱"]').setValue('New SKU')
    await wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!.trigger('click')
    await flushPromises()

    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存')!
    expect(saveButton.attributes('disabled')).toBeDefined()

    resolveCreateSku({ publicId: 'sku-new', skuCode: 'NEW-1', isDefault: false })
    await flushPromises()

    expect(saveButton.attributes('disabled')).toBeUndefined()
  })

  /** Regression test (組長 PR #24 review round 9, P1): the reverse direction of the above. */
  it('disables SKU operations while the product save itself is pending', async () => {
    mockGetAdminProduct.mockResolvedValue(product)
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    let resolveUpdateProduct!: (value: typeof product) => void
    mockUpdateProduct.mockReturnValueOnce(new Promise((resolve) => { resolveUpdateProduct = resolve }))

    const wrapper = await mountPage()
    await flushPromises()

    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    await nameInput.setValue('X')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const addSkuButton = wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!
    expect(addSkuButton.attributes('disabled')).toBeDefined()

    resolveUpdateProduct({ ...product, nameZhTw: 'X', rowVersion: 'BBB=' })
    await flushPromises()

    expect(addSkuButton.attributes('disabled')).toBeUndefined()
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
   * Regression test (組長 PR #24 round 8 review, P2): pinning productPublicId into the mutation's
   * own variables (useSkus.ts) fixes the *write* target, but the page-level onSuccess handler
   * still needs its own guard — if this admin submits "新增 SKU" for product A and then navigates
   * to product B *before* A's response arrives, the response must not touch B's page state
   * (resetting B's own in-progress newSku draft, or resyncing B's token from a refetch that was
   * never actually about B) just because it happens to resolve while B is showing.
   */
  it('ignores a SKU creation response that resolves after the admin has navigated to a different product', async () => {
    mockGetAdminProduct.mockImplementation((publicId: string) =>
      Promise.resolve({ ...product, publicId, productCode: publicId.toUpperCase(), rowVersion: `${publicId}-AAA=` }))
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockUpdateProduct.mockResolvedValueOnce(product)
    let resolveCreateSku!: (value: { publicId: string, skuCode: string, isDefault: boolean }) => void
    mockCreateSku.mockReturnValueOnce(new Promise((resolve) => { resolveCreateSku = resolve }))

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/products/:productId', name: 'product-edit', component: ProductEditPage, props: true }],
    })
    await router.push('/products/p1')
    await router.isReady()
    const wrapper = mount(RouterView, {
      global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
    })
    await flushPromises()

    // Submit "新增 SKU" for p1 — the request is now in flight and will not resolve until told to.
    await wrapper.find('input[aria-label="新 SKU 代碼"]').setValue('NEW-1')
    await wrapper.find('input[aria-label="新 SKU 名稱"]').setValue('New SKU')
    await wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!.trigger('click')
    await flushPromises()

    // Navigate away to p2 before p1's request settles.
    await router.push('/products/p2')
    await flushPromises()

    // p1's request finally resolves while the admin is looking at p2.
    resolveCreateSku({ publicId: 'sku-new', skuCode: 'NEW-1', isDefault: false })
    await flushPromises()

    // p2's own token (from its own load) must still be what gets submitted — not corrupted by
    // p1's stale success handler running a refetch/resync against p2's page state.
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockUpdateProduct).toHaveBeenCalledWith('p2', expect.objectContaining({ rowVersion: 'p2-AAA=' }))
  })

  /**
   * Regression test (組長 PR #24 round 8 review, P2): the same interleaving as above, but for the
   * product form's own save — a slow update response for A must not stamp its result onto B's
   * `editRowVersion`/`savedForm` after the admin has moved on to editing B.
   */
  it('ignores a product update response that resolves after the admin has navigated to a different product', async () => {
    mockGetAdminProduct.mockImplementation((publicId: string) =>
      Promise.resolve({ ...product, publicId, productCode: publicId.toUpperCase(), rowVersion: `${publicId}-AAA=` }))
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    let resolveUpdateProduct!: (value: typeof product) => void
    mockUpdateProduct.mockReturnValueOnce(new Promise((resolve) => { resolveUpdateProduct = resolve }))
    mockUpdateProduct.mockResolvedValueOnce(product)

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/products/:productId', name: 'product-edit', component: ProductEditPage, props: true }],
    })
    await router.push('/products/p1')
    await router.isReady()
    const wrapper = mount(RouterView, {
      global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
    })
    await flushPromises()

    const nameInput = wrapper.findAll('label').find((label) => label.text().includes('名稱'))!.find('input')
    await nameInput.setValue('Edited on p1')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    // Navigate away to p2 before p1's save response arrives.
    await router.push('/products/p2')
    await flushPromises()

    // p1's slow response finally resolves with p1's own new RowVersion.
    resolveUpdateProduct({ ...product, publicId: 'p1', nameZhTw: 'Edited on p1', rowVersion: 'p1-BBB=' })
    await flushPromises()

    // p2's token must still be p2's own — not overwritten by p1's stale response.
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockUpdateProduct).toHaveBeenLastCalledWith('p2', expect.objectContaining({ rowVersion: 'p2-AAA=' }))
  })

  /**
   * Regression test (組長 PR #24 round 8 review, P2): the productId-change watcher reset most of
   * the draft SKU row but missed `status` — a status picked while looking at product A's page
   * (e.g. Published) would still be selected for the draft after navigating to B, even though
   * every other field had been cleared.
   */
  it('resets the draft SKU status back to Draft after navigating to a different product', async () => {
    mockGetAdminProduct.mockImplementation((publicId: string) =>
      Promise.resolve({ ...product, publicId, productCode: publicId.toUpperCase() }))
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
    const wrapper = mount(RouterView, {
      global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
    })
    await flushPromises()

    await wrapper.find('select[aria-label="新 SKU 狀態"]').setValue('Published')

    await router.push('/products/p2')
    await flushPromises()

    const statusSelect = wrapper.find('select[aria-label="新 SKU 狀態"]')
    expect((statusSelect.element as HTMLSelectElement).value).toBe('Draft')
  })

  /**
   * Regression test (組長 PR #24 round 8 review, P2): a create/update error left over from the
   * previous product stayed visible (and misleading) after navigating to a different one, since
   * the mutation objects themselves aren't recreated by a param-only navigation.
   */
  it('clears the previous product\'s SKU-creation error after navigating to a different product', async () => {
    mockGetAdminProduct.mockImplementation((publicId: string) =>
      Promise.resolve({ ...product, publicId, productCode: publicId.toUpperCase() }))
    mockListBrands.mockResolvedValue({ items: [{ publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListCategories.mockResolvedValue({ items: [{ publicId: 'cat-1', code: 'CAT-A', nameZhTw: 'Category A' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockListTags.mockResolvedValue({ items: [{ publicId: 'tag-legacy', code: 'LEGACY-TAG', nameZhTw: 'Legacy Tag' }], pageNumber: 1, pageSize: 100, totalCount: 1 })
    mockCreateSku.mockRejectedValueOnce(new ApiError('Conflict', { status: 409, code: 'sku_code_duplicate' }))

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/products/:productId', name: 'product-edit', component: ProductEditPage, props: true }],
    })
    await router.push('/products/p1')
    await router.isReady()
    const wrapper = mount(RouterView, {
      global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
    })
    await flushPromises()

    await wrapper.find('input[aria-label="新 SKU 代碼"]').setValue('DUP-1')
    await wrapper.find('input[aria-label="新 SKU 名稱"]').setValue('Dup SKU')
    await wrapper.findAll('button').find((button) => button.text() === '新增 SKU')!.trigger('click')
    await flushPromises()
    expect(wrapper.find('.product-form__error').exists()).toBe(true)

    await router.push('/products/p2')
    await flushPromises()

    expect(wrapper.find('.product-form__error').exists()).toBe(false)
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

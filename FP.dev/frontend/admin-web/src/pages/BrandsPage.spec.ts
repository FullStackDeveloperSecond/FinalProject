import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { ApiError } from '@doselect/web-shared/api'
import { describe, expect, it, vi } from 'vitest'

const mockListBrands = vi.fn()
const mockCreateBrand = vi.fn()
const mockUpdateBrand = vi.fn()

vi.mock('../features/brands/api', () => ({
  listBrands: mockListBrands,
  createBrand: mockCreateBrand,
  updateBrand: mockUpdateBrand,
}))

const { default: BrandsPage } = await import('./BrandsPage.vue')

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(BrandsPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
}

describe('BrandsPage', () => {
  it('renders the loaded brand list', async () => {
    mockListBrands.mockResolvedValueOnce({
      items: [{ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme', description: null, websiteUrl: null, isActive: true, sortOrder: 0, rowVersion: 'AAA=' }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
    })

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('ACME')
    expect(wrapper.text()).toContain('Acme')
  })

  it('submits an edit with the RowVersion carried from the loaded row', async () => {
    mockListBrands.mockResolvedValue({
      items: [{ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme', description: null, websiteUrl: null, isActive: true, sortOrder: 0, rowVersion: 'AAA=' }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
    })
    mockUpdateBrand.mockResolvedValueOnce({ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme Updated', description: null, websiteUrl: null, isActive: true, sortOrder: 0, rowVersion: 'BBB=' })

    const wrapper = mountPage()
    await flushPromises()

    const editButton = wrapper.findAll('button').find((button) => button.text() === '編輯')
    await editButton!.trigger('click')
    const nameInput = wrapper.find('input[aria-label="名稱"]')
    await nameInput.setValue('Acme Updated')
    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存')
    await saveButton!.trigger('click')
    await flushPromises()

    expect(mockUpdateBrand).toHaveBeenCalledWith('b1', expect.objectContaining({
      nameZhTw: 'Acme Updated',
      rowVersion: 'AAA=',
    }))
  })

  /**
   * Regression test (組長 PR #24 review round 7, P1): submitEdit() used to send item.rowVersion
   * read live from the list query's current data, not the value captured when editing started.
   * A background refetch while a row is mid-edit (another admin's change, window-refocus) would
   * silently swap in a newer token, defeating the optimistic-concurrency check the same way as
   * ProductEditPage and SkuEditorRow.
   */
  it('submits with the RowVersion captured when editing began, not one that arrives via a background refetch', async () => {
    mockListBrands.mockResolvedValueOnce({
      items: [{ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme', description: null, websiteUrl: null, isActive: true, sortOrder: 0, rowVersion: 'AAA=' }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
    })
    mockUpdateBrand.mockResolvedValueOnce({ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme Updated', description: null, websiteUrl: null, isActive: true, sortOrder: 0, rowVersion: 'CCC=' })

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const wrapper = mount(BrandsPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
    await flushPromises()

    const editButton = wrapper.findAll('button').find((button) => button.text() === '編輯')
    await editButton!.trigger('click')
    const nameInput = wrapper.find('input[aria-label="名稱"]')
    await nameInput.setValue('Acme Updated')

    // Simulates another admin renaming this brand elsewhere, landing here via a background
    // refetch (e.g. window refocus) while this row is still open for edit.
    mockListBrands.mockResolvedValueOnce({
      items: [{ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme', description: null, websiteUrl: null, isActive: true, sortOrder: 0, rowVersion: 'BBB=' }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
    })
    await queryClient.invalidateQueries({ queryKey: ['brands'] })
    await flushPromises()

    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存')
    await saveButton!.trigger('click')
    await flushPromises()

    expect(mockUpdateBrand).toHaveBeenCalledWith('b1', expect.objectContaining({
      nameZhTw: 'Acme Updated',
      rowVersion: 'AAA=',
    }))
  })

  it('shows the mapped error message when create fails with a duplicate code', async () => {
    mockListBrands.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })
    mockCreateBrand.mockRejectedValueOnce(new ApiError('Conflict', { status: 409, code: 'brand_code_duplicate' }))

    const wrapper = mountPage()
    await flushPromises()

    const addButton = wrapper.findAll('button').find((button) => button.text() === '新增')
    await addButton!.trigger('click')
    const codeInput = wrapper.find('input[aria-label="代碼"]')
    await codeInput.setValue('ACME')
    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存')
    await saveButton!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('代碼已存在')
  })

  /** PR #24 review: brand management was capped at the backend's default page size (20) with no way to reach later rows. */
  it('requests the next page and re-fetches when 下一頁 is clicked', async () => {
    mockListBrands.mockResolvedValue({
      items: [{ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme', description: null, websiteUrl: null, isActive: true, sortOrder: 0, rowVersion: 'AAA=' }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 40,
      totalPages: 2,
    })

    const wrapper = mountPage()
    await flushPromises()

    const nextButton = wrapper.findAll('button').find((button) => button.text() === '下一頁')
    expect(nextButton).toBeDefined()
    await nextButton!.trigger('click')
    await flushPromises()

    expect(mockListBrands).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2, pageSize: 20 }))
  })
})

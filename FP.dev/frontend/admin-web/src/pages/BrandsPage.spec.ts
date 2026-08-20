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
})

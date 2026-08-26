import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { describe, expect, it, vi } from 'vitest'

const mockListCategories = vi.fn()
const mockCreateCategory = vi.fn()
const mockUpdateCategory = vi.fn()

vi.mock('../features/categories/api', () => ({
  listCategories: mockListCategories,
  createCategory: mockCreateCategory,
  updateCategory: mockUpdateCategory,
}))

const { default: CategoriesPage } = await import('./CategoriesPage.vue')

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(CategoriesPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
}

function category(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'c1',
    code: 'CAT-1',
    nameZhTw: 'Category 1',
    slug: 'category-1',
    description: null,
    parentCategoryPublicId: null,
    isActive: true,
    sortOrder: 0,
    rowVersion: 'AAA=',
    ...overrides,
  }
}

describe('CategoriesPage', () => {
  it('blocks create and edit operations when the full parent-category lookup fails', async () => {
    mockListCategories.mockImplementation((params: { pageSize?: number }) =>
      params.pageSize === 100
        ? Promise.reject(new Error('full lookup failed'))
        : Promise.resolve({
            items: [category()],
            pageNumber: 1,
            pageSize: 20,
            totalCount: 1,
            totalPages: 1,
          }))

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('上層分類資料載入失敗')
    expect(wrapper.findAll('button').find((button) => button.text() === '新增')!.attributes('disabled')).toBeDefined()
    expect(wrapper.findAll('button').find((button) => button.text() === '編輯')!.attributes('disabled')).toBeDefined()
  })

  /**
   * PR #24 review: the parent-category dropdown used to only offer options from the
   * currently-viewed (filtered/paginated) page — a parent on a later page was unreachable.
   * The page-1, pageSize:20 main list and the "all categories" full-list lookup (paged through
   * in <=100-sized requests — PR #24 review round 2, a flat pageSize:500 gets rejected
   * server-side) are now separate queries, so a category absent from the paginated page must
   * still appear as a selectable parent option.
   */
  it('offers a category as a parent option even when it is not on the current paginated page', async () => {
    mockListCategories.mockImplementation((params: { pageSize?: number }) => {
      if (params.pageSize === 100) {
        return Promise.resolve({
          items: [category({ publicId: 'c1', code: 'CAT-1' }), category({ publicId: 'c2', code: 'CAT-2', nameZhTw: 'Category 2' })],
          pageNumber: 1,
          pageSize: 100,
          totalCount: 2,
        })
      }
      // The main, paginated list only shows CAT-1 on this page.
      return Promise.resolve({
        items: [category({ publicId: 'c1', code: 'CAT-1' })],
        pageNumber: 1,
        pageSize: 20,
        totalCount: 2,
        totalPages: 2,
      })
    })

    const wrapper = mountPage()
    await flushPromises()

    const addButton = wrapper.findAll('button').find((button) => button.text() === '新增')
    await addButton!.trigger('click')

    const parentSelect = wrapper.find('select[aria-label="上層分類"]')
    const optionTexts = parentSelect.findAll('option').map((option) => option.text())
    expect(optionTexts).toContain('Category 2')
  })
})

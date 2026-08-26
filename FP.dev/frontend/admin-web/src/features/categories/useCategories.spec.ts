import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { defineComponent, h } from 'vue'
import { describe, expect, it, vi } from 'vitest'

const mockListCategories = vi.fn()
const mockCreateCategory = vi.fn()

vi.mock('./api', () => ({
  listCategories: mockListCategories,
  createCategory: mockCreateCategory,
  updateCategory: vi.fn(),
}))

const { useFullCategoryList, useCreateCategory } = await import('./useCategories')

function mountWithQuery(setup: () => void, queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
})) {
  const Host = defineComponent({ setup: () => { setup(); return () => h('div') } })
  mount(Host, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
  return queryClient
}

describe('useCreateCategory', () => {
  it('invalidates the whole categories prefix on success', async () => {
    const created = { publicId: 'c1', code: 'CPU', nameZhTw: 'CPU' }
    mockCreateCategory.mockResolvedValueOnce(created)

    let result: ReturnType<typeof useCreateCategory> | undefined
    const queryClient = mountWithQuery(() => { result = useCreateCategory() })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    result!.mutate({ code: 'CPU', nameZhTw: 'CPU', slug: 'cpu', description: null, parentCategoryPublicId: null, sortOrder: 0, isActive: true })
    await flushPromises()

    expect(mockCreateCategory).toHaveBeenCalled()
    // 組長 PR #24 review round 3: must invalidate the whole 'categories' prefix, not just
    // ['categories','list'] — otherwise ['categories','full-list'] (the parent-category picker)
    // keeps serving a stale set after a create/update.
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['categories'] })
  })

  /**
   * Regression test (組長 PR #24 review round 3, item 5): the parent-category picker
   * (useFullCategoryList) used to keep showing the pre-create set until something unrelated
   * invalidated it, because useCreateCategory only invalidated ['categories','list'].
   */
  it('makes the parent-category picker refetch with the newly created category', async () => {
    mockListCategories.mockResolvedValueOnce({
      items: [{ publicId: 'c1', code: 'CPU', nameZhTw: 'CPU' }],
      pageNumber: 1,
      pageSize: 100,
      totalCount: 1,
    })
    const created = { publicId: 'c2', code: 'GPU', nameZhTw: 'GPU' }
    mockCreateCategory.mockResolvedValueOnce(created)

    let fullList: ReturnType<typeof useFullCategoryList> | undefined
    let createResult: ReturnType<typeof useCreateCategory> | undefined
    mountWithQuery(() => {
      fullList = useFullCategoryList()
      createResult = useCreateCategory()
    })
    await flushPromises()

    expect(fullList?.data.value?.items).toEqual([{ publicId: 'c1', code: 'CPU', nameZhTw: 'CPU' }])

    mockListCategories.mockResolvedValueOnce({
      items: [{ publicId: 'c1', code: 'CPU', nameZhTw: 'CPU' }, created],
      pageNumber: 1,
      pageSize: 100,
      totalCount: 2,
    })
    createResult!.mutate({ code: 'GPU', nameZhTw: 'GPU', slug: 'gpu', description: null, parentCategoryPublicId: null, sortOrder: 0, isActive: true })
    await flushPromises()

    expect(fullList?.data.value?.items).toEqual([{ publicId: 'c1', code: 'CPU', nameZhTw: 'CPU' }, created])
  })
})

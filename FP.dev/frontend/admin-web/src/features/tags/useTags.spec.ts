import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { defineComponent, h } from 'vue'
import { describe, expect, it, vi } from 'vitest'

const mockListTags = vi.fn()
const mockCreateTag = vi.fn()

vi.mock('./api', () => ({
  listTags: mockListTags,
  createTag: mockCreateTag,
  updateTag: vi.fn(),
}))

const { useFullTagList, useCreateTag } = await import('./useTags')

function mountWithQuery(setup: () => void, queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
})) {
  const Host = defineComponent({ setup: () => { setup(); return () => h('div') } })
  mount(Host, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
  return queryClient
}

describe('useCreateTag', () => {
  it('invalidates the whole tags prefix on success', async () => {
    const created = { publicId: 't1', code: 'RGB', nameZhTw: 'RGB' }
    mockCreateTag.mockResolvedValueOnce(created)

    let result: ReturnType<typeof useCreateTag> | undefined
    const queryClient = mountWithQuery(() => { result = useCreateTag() })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    result!.mutate({ code: 'RGB', nameZhTw: 'RGB', sortOrder: 0, isActive: true })
    await flushPromises()

    expect(mockCreateTag).toHaveBeenCalled()
    // 組長 PR #24 review round 3: must invalidate the whole 'tags' prefix, not just
    // ['tags','list'] — otherwise ['tags','full-list'] (the checkbox list on ProductEditPage)
    // keeps serving a stale set after a create/update.
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['tags'] })
  })

  /**
   * Regression test (組長 PR #24 review round 3, item 5): the tag checkbox list
   * (useFullTagList) used to keep showing the pre-create set until something unrelated
   * invalidated it, because useCreateTag only invalidated ['tags','list'].
   */
  it('makes the tag picker refetch with the newly created tag', async () => {
    mockListTags.mockResolvedValueOnce({
      items: [{ publicId: 't1', code: 'RGB', nameZhTw: 'RGB' }],
      pageNumber: 1,
      pageSize: 100,
      totalCount: 1,
    })
    const created = { publicId: 't2', code: 'WATERCOOLED', nameZhTw: '水冷' }
    mockCreateTag.mockResolvedValueOnce(created)

    let fullList: ReturnType<typeof useFullTagList> | undefined
    let createResult: ReturnType<typeof useCreateTag> | undefined
    mountWithQuery(() => {
      fullList = useFullTagList()
      createResult = useCreateTag()
    })
    await flushPromises()

    expect(fullList?.data.value?.items).toEqual([{ publicId: 't1', code: 'RGB', nameZhTw: 'RGB' }])

    mockListTags.mockResolvedValueOnce({
      items: [{ publicId: 't1', code: 'RGB', nameZhTw: 'RGB' }, created],
      pageNumber: 1,
      pageSize: 100,
      totalCount: 2,
    })
    createResult!.mutate({ code: 'WATERCOOLED', nameZhTw: '水冷', sortOrder: 0, isActive: true })
    await flushPromises()

    expect(fullList?.data.value?.items).toEqual([{ publicId: 't1', code: 'RGB', nameZhTw: 'RGB' }, created])
  })
})

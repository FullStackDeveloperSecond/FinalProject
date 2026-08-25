import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { defineComponent, h } from 'vue'
import { describe, expect, it, vi } from 'vitest'

const mockListBrands = vi.fn()
const mockCreateBrand = vi.fn()

vi.mock('./api', () => ({
  listBrands: mockListBrands,
  createBrand: mockCreateBrand,
  updateBrand: vi.fn(),
}))

const { useBrandList, useFullBrandList, useCreateBrand } = await import('./useBrands')

function mountWithQuery(setup: () => void, queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
})) {
  const Host = defineComponent({ setup: () => { setup(); return () => h('div') } })
  mount(Host, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
  return queryClient
}

describe('useBrandList', () => {
  it('resolves with the mocked list response', async () => {
    const items = [{ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme' }]
    mockListBrands.mockResolvedValueOnce({ items, pageNumber: 1, pageSize: 20, totalCount: 1 })

    let result: ReturnType<typeof useBrandList> | undefined
    mountWithQuery(() => { result = useBrandList({ pageNumber: 1, pageSize: 20 }) })
    await flushPromises()

    expect(result?.data.value?.items).toEqual(items)
    expect(result?.isError.value).toBe(false)
  })
})

describe('useCreateBrand', () => {
  it('invalidates the brands list query on success', async () => {
    const created = { publicId: 'b1', code: 'ACME', nameZhTw: 'Acme' }
    mockCreateBrand.mockResolvedValueOnce(created)

    let result: ReturnType<typeof useCreateBrand> | undefined
    const queryClient = mountWithQuery(() => { result = useCreateBrand() })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    result!.mutate({ code: 'ACME', nameZhTw: 'Acme', description: null, websiteUrl: null, sortOrder: 0, isActive: true })
    await flushPromises()

    expect(mockCreateBrand).toHaveBeenCalled()
    // 組長 PR #24 review round 3: must invalidate the whole 'brands' prefix, not just
    // ['brands','list'] — otherwise ['brands','full-list'] (the picker other pages use to
    // resolve a brand code to a publicId) keeps serving a stale set after a create/update.
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['brands'] })
  })

  it('surfaces a mutation error without throwing', async () => {
    const error = new Error('brand_code_duplicate')
    mockCreateBrand.mockRejectedValueOnce(error)

    let result: ReturnType<typeof useCreateBrand> | undefined
    mountWithQuery(() => { result = useCreateBrand() })

    result!.mutate({ code: 'ACME', nameZhTw: 'Acme', description: null, websiteUrl: null, sortOrder: 0, isActive: true })
    await flushPromises()

    expect(result?.isError.value).toBe(true)
    expect(result?.error.value).toBe(error)
  })

  /**
   * Regression test (組長 PR #24 review round 3, item 5): a picker using useFullBrandList used
   * to keep showing the pre-create set until something unrelated invalidated it, because
   * useCreateBrand only invalidated ['brands','list'].
   */
  it('makes a full-list picker refetch with the newly created brand', async () => {
    mockListBrands.mockResolvedValueOnce({
      items: [{ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme' }],
      pageNumber: 1,
      pageSize: 100,
      totalCount: 1,
    })
    const created = { publicId: 'b2', code: 'NEWCO', nameZhTw: 'NewCo' }
    mockCreateBrand.mockResolvedValueOnce(created)

    let fullList: ReturnType<typeof useFullBrandList> | undefined
    let createResult: ReturnType<typeof useCreateBrand> | undefined
    mountWithQuery(() => {
      fullList = useFullBrandList()
      createResult = useCreateBrand()
    })
    await flushPromises()

    expect(fullList?.data.value?.items).toEqual([{ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme' }])

    mockListBrands.mockResolvedValueOnce({
      items: [{ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme' }, created],
      pageNumber: 1,
      pageSize: 100,
      totalCount: 2,
    })
    createResult!.mutate({ code: 'NEWCO', nameZhTw: 'NewCo', description: null, websiteUrl: null, sortOrder: 0, isActive: true })
    await flushPromises()

    expect(fullList?.data.value?.items).toEqual([{ publicId: 'b1', code: 'ACME', nameZhTw: 'Acme' }, created])
  })
})

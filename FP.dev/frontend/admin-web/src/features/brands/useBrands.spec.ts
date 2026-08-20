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

const { useBrandList, useCreateBrand } = await import('./useBrands')

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
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['brands', 'list'] })
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
})

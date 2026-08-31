import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'

describe('customer review queries', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('refetches public reviews when the reactive product id changes', async () => {
    const fetchStub = vi.fn<typeof fetch>().mockResolvedValue(Response.json([]))
    vi.stubGlobal('fetch', fetchStub)
    const { usePublicProductReviewsQuery } = await import('./queries')
    const productId = ref('018f2e6a-0000-7000-8000-000000000001')
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const Harness = defineComponent({
      setup() {
        usePublicProductReviewsQuery(productId)
        return () => null
      },
    })
    const wrapper = mount(Harness, {
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })

    await vi.waitFor(() => expect(fetchStub).toHaveBeenCalledTimes(1))
    productId.value = '018f2e6a-0000-7000-8000-000000000002'
    await vi.waitFor(() => expect(fetchStub).toHaveBeenCalledTimes(2))

    const firstInput = fetchStub.mock.calls[0]?.[0]
    const secondInput = fetchStub.mock.calls[1]?.[0]
    expect(new URL(firstInput instanceof Request ? firstInput.url : String(firstInput)).pathname)
      .toContain('018f2e6a-0000-7000-8000-000000000001')
    expect(new URL(secondInput instanceof Request ? secondInput.url : String(secondInput)).pathname)
      .toContain('018f2e6a-0000-7000-8000-000000000002')
    wrapper.unmount()
  })
})

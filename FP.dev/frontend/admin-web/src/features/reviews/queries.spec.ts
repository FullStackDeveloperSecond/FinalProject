import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'

describe('admin review queries', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('refetches when the reactive moderation status changes', async () => {
    const fetchStub = vi.fn<typeof fetch>().mockResolvedValue(Response.json([]))
    vi.stubGlobal('fetch', fetchStub)
    const { useAdminReviewsQuery } = await import('./queries')
    const status = ref('pendingReview')
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const Harness = defineComponent({
      setup() {
        useAdminReviewsQuery(status)
        return () => null
      },
    })
    const wrapper = mount(Harness, {
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })

    await vi.waitFor(() => expect(fetchStub).toHaveBeenCalledTimes(1))
    status.value = 'approved'
    await vi.waitFor(() => expect(fetchStub).toHaveBeenCalledTimes(2))

    const firstInput = fetchStub.mock.calls[0]?.[0]
    const secondInput = fetchStub.mock.calls[1]?.[0]
    expect(new URL(firstInput instanceof Request ? firstInput.url : String(firstInput)).searchParams.get('status'))
      .toBe('pendingReview')
    expect(new URL(secondInput instanceof Request ? secondInput.url : String(secondInput)).searchParams.get('status'))
      .toBe('approved')
    wrapper.unmount()
  })
})

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { defineComponent, type PropType } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'

function requestUrl(input: RequestInfo | URL): URL {
  return new URL(input instanceof Request ? input.url : String(input))
}

// A single generic harness (rather than one `defineComponent` per test) so this file only
// declares one Vue component, per the project's `vue/one-component-per-file` lint rule.
const Harness = defineComponent({
  props: { run: { type: Function as PropType<() => void>, required: true } },
  setup(props) {
    props.run()
    return () => null
  },
})

describe('customer favorites queries', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('refetches the list when the reactive page number changes', async () => {
    const fetchStub = vi.fn<typeof fetch>().mockResolvedValue(
      Response.json({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
    )
    vi.stubGlobal('fetch', fetchStub)
    const { useMyFavoritesQuery } = await import('./queries')
    const { ref } = await import('vue')
    const pageNumber = ref(1)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const wrapper = mount(Harness, {
      props: { run: () => { useMyFavoritesQuery(pageNumber, 20) } },
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })

    await vi.waitFor(() => expect(fetchStub).toHaveBeenCalledTimes(1))
    pageNumber.value = 2
    await vi.waitFor(() => expect(fetchStub).toHaveBeenCalledTimes(2))

    expect(requestUrl(fetchStub.mock.calls[0]![0]).searchParams.get('PageNumber')).toBe('1')
    expect(requestUrl(fetchStub.mock.calls[1]![0]).searchParams.get('PageNumber')).toBe('2')
    wrapper.unmount()
  })

  it('does not call the API when disabled (e.g. a guest viewing a product)', async () => {
    const fetchStub = vi.fn<typeof fetch>().mockResolvedValue(
      Response.json({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0, totalPages: 0 }),
    )
    vi.stubGlobal('fetch', fetchStub)
    const { useMyFavoritesQuery } = await import('./queries')
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const wrapper = mount(Harness, {
      props: { run: () => { useMyFavoritesQuery(1, 100, () => false) } },
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })
    await Promise.resolve()

    expect(fetchStub).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})

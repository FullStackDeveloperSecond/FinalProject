import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'

let runHarness: () => void

const Harness = defineComponent({
  setup() {
    runHarness()
    return () => null
  },
})

describe('case workbench queries', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.resetModules()
  })

  it('loads the workbench with array filters, the opaque cursor, and credentials', async () => {
    const fetchStub = vi.fn<typeof fetch>().mockResolvedValue(Response.json({
      items: [],
      nextCursor: null,
      hasMore: false,
      totalCount: 0,
    }))
    vi.stubGlobal('fetch', fetchStub)
    const { useCaseWorkbenchQuery } = await import('./queries')
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    runHarness = () => useCaseWorkbenchQuery(() => ({
      caseTypes: ['support', 'return'],
      statuses: ['inProgress'],
      priorities: ['high'],
      assignee: 'mine',
      createdFrom: '2026-08-01',
      createdTo: '2026-08-31',
      lastActivityFrom: '2026-09-01',
      lastActivityTo: '2026-09-08',
      sort: 'oldest',
      keyword: 'CS-2026',
      overdueOnly: true,
      cursor: 'opaque+cursor/==',
      pageSize: 20,
    }))
    const wrapper = mount(Harness, {
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })

    await vi.waitFor(() => expect(fetchStub).toHaveBeenCalledOnce())
    const [input, init] = fetchStub.mock.calls[0] ?? []
    const request = input instanceof Request ? input : new Request(String(input), init)
    const url = new URL(request.url)
    expect(url.pathname).toBe('/api/v1/admin/case-workbench')
    expect(url.searchParams.getAll('CaseTypes')).toEqual(['support', 'return'])
    expect(url.searchParams.getAll('Statuses')).toEqual(['inProgress'])
    expect(url.searchParams.getAll('Priorities')).toEqual(['high'])
    expect(url.searchParams.get('Assignee')).toBe('mine')
    expect(url.searchParams.get('CreatedFrom')).toBe('2026-08-01')
    expect(url.searchParams.get('CreatedTo')).toBe('2026-08-31')
    expect(url.searchParams.get('LastActivityFrom')).toBe('2026-09-01')
    expect(url.searchParams.get('LastActivityTo')).toBe('2026-09-08')
    expect(url.searchParams.get('Sort')).toBe('oldest')
    expect(url.searchParams.get('Keyword')).toBe('CS-2026')
    expect(url.searchParams.get('OverdueOnly')).toBe('true')
    expect(url.searchParams.get('Cursor')).toBe('opaque+cursor/==')
    expect(url.searchParams.get('PageSize')).toBe('20')
    expect(request.credentials).toBe('include')

    wrapper.unmount()
  })

  it('shares its query key root with the support actions so their invalidation refreshes this page', async () => {
    const { caseWorkbenchQueryKey } = await import('./queries')
    const { caseWorkbenchRootKey } = await import('../support/queries')

    expect(caseWorkbenchQueryKey()[0]).toBe(caseWorkbenchRootKey)
  })
})

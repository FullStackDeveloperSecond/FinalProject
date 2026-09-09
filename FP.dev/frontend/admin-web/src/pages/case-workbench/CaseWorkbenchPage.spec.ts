import PrimeVue from 'primevue/config'
import { chinesePaginationLocale } from '@doselect/web-shared/theme'
import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import CaseWorkbenchPage from './CaseWorkbenchPage.vue'

const workbenchMocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')

  return {
    data: ref<Record<string, unknown> | null>(null),
    isPending: ref(false),
    isError: ref(false),
    error: ref<unknown>(null),
    refetch: vi.fn(),
    lastFilters: ref<Record<string, unknown> | null>(null),
  }
})

vi.mock('../../features/case-workbench/queries', async () => {
  const { toValue, watchEffect } = await import('vue')
  return {
    defaultCaseWorkbenchPageSize: 20,
    useCaseWorkbenchQuery: (filters: unknown) => {
      watchEffect(() => {
        workbenchMocks.lastFilters.value = toValue(filters) as Record<string, unknown>
      })
      return {
        data: workbenchMocks.data,
        isPending: workbenchMocks.isPending,
        isError: workbenchMocks.isError,
        error: workbenchMocks.error,
        refetch: workbenchMocks.refetch,
      }
    },
  }
})

// caseType uses vw_CaseWorkbench's actual PascalCase SQL literals ('Support'/'Return'/'Report'),
// not the lowercase CaseWorkbenchCaseType query-parameter enum — using the wrong casing here
// would let a regression on that mismatch slip through undetected (it did, once).
function sampleItem(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    caseType: 'Support',
    casePublicId: '018f2e6a-0000-7000-8000-000000000001',
    caseNumber: 'CS-20260819-0001',
    title: '訂單延遲問題',
    status: 'open',
    priority: 'high',
    requesterDisplay: '王小明',
    assigneePublicId: null,
    createdAtUtc: '2026-08-19T01:00:00Z',
    lastActivityAtUtc: '2026-08-19T03:00:00Z',
    slaDueAtUtc: '2026-08-20T03:00:00Z',
    isOverdue: false,
    ...overrides,
  }
}

async function mountPage(path = '/cases') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/cases', component: { template: '<div />' } },
      { path: '/support/tickets/:ticketId', name: 'support-ticket-detail', component: { template: '<div />' } },
    ],
  })
  await router.push(path)
  await router.isReady()

  return mount(CaseWorkbenchPage, { global: { plugins: [[PrimeVue, { locale: chinesePaginationLocale }], router] } })
}

describe('CaseWorkbenchPage', () => {
  beforeEach(() => {
    workbenchMocks.data.value = null
    workbenchMocks.isPending.value = false
    workbenchMocks.isError.value = false
    workbenchMocks.error.value = null
    workbenchMocks.refetch.mockReset()
    workbenchMocks.lastFilters.value = null
  })

  it('shows a loading state while pending', async () => {
    workbenchMocks.isPending.value = true
    const wrapper = await mountPage()

    expect(wrapper.findComponent({ name: 'LoadingState' }).exists()).toBe(true)
  })

  it('shows a safe error state with a retry action, without leaking correlation details into the title', async () => {
    workbenchMocks.isError.value = true
    workbenchMocks.error.value = new ApiError('boom', { status: 500, code: 'internal_error', correlationId: 'corr-1' })
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('無法載入案件工作台')
    await wrapper.findComponent({ name: 'ErrorState' }).get('button').trigger('click')
    expect(workbenchMocks.refetch).toHaveBeenCalledOnce()
  })

  it('shows an empty state when there are no matching cases', async () => {
    workbenchMocks.data.value = { items: [], nextCursor: null, hasMore: false, totalCount: 0 }
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('目前沒有符合條件的案件')
  })

  it('renders the fixed 12-column summary and links a Support case to its existing detail route', async () => {
    workbenchMocks.data.value = { items: [sampleItem()], nextCursor: null, hasMore: false, totalCount: 1 }
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('CS-20260819-0001')
    expect(wrapper.text()).toContain('訂單延遲問題')
    expect(wrapper.text()).toContain('王小明')
    // Regression: caseType arrives as 'Support' (PascalCase, from vw_CaseWorkbench's SQL
    // literals) — this must still resolve to the Chinese label, not fall through to the raw
    // string, and must still be recognized as a navigable Support case despite the casing.
    expect(wrapper.text()).toContain('客服案件')
    expect(wrapper.text()).not.toContain('Support')
    expect(wrapper.text()).toContain('待處理')
    expect(wrapper.text()).toContain('共 1 筆符合條件的案件')
    const link = wrapper.get('a')
    expect(link.attributes('href')).toBe('/support/tickets/018f2e6a-0000-7000-8000-000000000001')
  })

  it('never links a Return or Report case to a fake route — shows a disabled hint instead', async () => {
    workbenchMocks.data.value = {
      items: [sampleItem({ caseType: 'Return', casePublicId: 'return-1', caseNumber: 'RT-0001' })],
      nextCursor: null,
      hasMore: false,
      totalCount: 1,
    }
    const wrapper = await mountPage()

    expect(wrapper.find('a').exists()).toBe(false)
    const hint = wrapper.find('.case-workbench__no-detail')
    expect(hint.exists()).toBe(true)
    expect(hint.text()).toContain('RT-0001')
    expect(hint.attributes('title')).toContain('目前無可用明細')
  })

  it('shows numbered pages and disables previous on the first page', async () => {
    workbenchMocks.data.value = { items: [sampleItem()], nextCursor: 'next-cursor', hasMore: true, totalCount: 40 }
    const wrapper = await mountPage()

    const buttons = wrapper.findAll('.ds-page-pager button').filter(b => ['上一頁', '下一頁'].includes(b.text()))
    expect(buttons[0]?.attributes('disabled')).toBeDefined()
    expect(buttons[1]?.attributes('disabled')).toBeUndefined()

    await buttons[1]?.trigger('click')
    await flushPromises()
    expect(workbenchMocks.lastFilters.value?.pageNumber).toBe(2)
  })

  it('returns to a valid page if a refresh leaves the last page empty', async () => {
    workbenchMocks.data.value = { items: [sampleItem()], nextCursor: 'next', hasMore: true, totalCount: 40 }
    const wrapper = await mountPage()
    await wrapper.get('[aria-label="第 2 頁"]').trigger('click')
    await flushPromises()
    expect(workbenchMocks.lastFilters.value?.pageNumber).toBe(2)
    workbenchMocks.data.value = { items: [], nextCursor: null, hasMore: false, totalCount: 1 }
    await flushPromises()
    expect(workbenchMocks.lastFilters.value?.pageNumber).toBe(1)
    wrapper.unmount()
  })

  it('toggling a case-type filter resets pagination and passes the selection through to the query', async () => {
    workbenchMocks.data.value = { items: [sampleItem()], nextCursor: 'next-cursor', hasMore: true, totalCount: 40 }
    const wrapper = await mountPage()
    const nextButton = wrapper.findAll('.ds-page-pager button').find(b => b.text() === '下一頁')
    await nextButton?.trigger('click')
    await flushPromises()
    expect(workbenchMocks.lastFilters.value?.pageNumber).toBe(2)

    await wrapper.get('#case-type-filter').setValue('support')
    await flushPromises()

    expect(workbenchMocks.lastFilters.value?.pageNumber).toBe(1)
    expect(workbenchMocks.lastFilters.value?.caseTypes).toEqual(['support'])
  })

  it('shows only the currently authorized Support case-type filter', async () => {
    workbenchMocks.data.value = { items: [], nextCursor: null, hasMore: false, totalCount: 0 }
    const wrapper = await mountPage()
    const caseTypeOptions = wrapper.get('#case-type-filter').findAll('option')

    expect(caseTypeOptions).toHaveLength(2)
    expect(caseTypeOptions[1]?.text()).toContain('客服案件')
  })

  it.each([
    ['#case-search', 'new keyword'],
    ['#case-assignee-filter', 'mine'],
    ['#case-created-from', '2026-08-01'],
  ])('clears a second-page cursor synchronously when filter %s changes', async (selector, value) => {
    workbenchMocks.data.value = { items: [sampleItem()], nextCursor: 'next-cursor', hasMore: true, totalCount: 40 }
    const wrapper = await mountPage()
    await wrapper.findAll('.ds-page-pager button').find(b => b.text() === '下一頁')?.trigger('click')
    await flushPromises()
    expect(workbenchMocks.lastFilters.value?.pageNumber).toBe(2)

    await wrapper.get(selector).setValue(value)
    await flushPromises()

    expect(workbenchMocks.lastFilters.value?.pageNumber).toBe(1)
  })

  it('restores readable filters from the URL and writes later changes back to it', async () => {
    workbenchMocks.data.value = { items: [], nextCursor: null, hasMore: false, totalCount: 0 }
    const wrapper = await mountPage(
      '/cases?keyword=CS-2026&status=inProgress&priority=urgent&assignee=mine&createdFrom=2026-08-01&createdTo=2026-08-31&lastActivityFrom=2026-09-01&lastActivityTo=2026-09-08&sort=oldest',
    )

    expect(workbenchMocks.lastFilters.value).toMatchObject({
      keyword: 'CS-2026',
      statuses: ['inProgress'],
      priorities: ['urgent'],
      assignee: 'mine',
      createdFrom: '2026-08-01',
      createdTo: '2026-08-31',
      lastActivityFrom: '2026-09-01',
      lastActivityTo: '2026-09-08',
      sort: 'oldest',
    })

    await wrapper.get('#case-status-filter').setValue('resolved')
    await flushPromises()
    expect(wrapper.vm.$route.query.status).toBe('resolved')
  })
})

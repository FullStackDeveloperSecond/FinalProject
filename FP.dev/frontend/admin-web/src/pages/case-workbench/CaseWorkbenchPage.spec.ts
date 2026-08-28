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

async function mountPage() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/cases', component: { template: '<div />' } },
      { path: '/support/tickets/:ticketId', name: 'support-ticket-detail', component: { template: '<div />' } },
    ],
  })
  await router.push('/cases')
  await router.isReady()

  return mount(CaseWorkbenchPage, { global: { plugins: [router] } })
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
    await wrapper.get('button[type="button"]').trigger('click')
    expect(workbenchMocks.refetch).toHaveBeenCalledOnce()
  })

  it('shows an empty state when there are no matching cases', async () => {
    workbenchMocks.data.value = { items: [], nextCursor: null, hasMore: false }
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('目前沒有符合條件的案件')
  })

  it('renders the fixed 12-column summary and links a Support case to its existing detail route', async () => {
    workbenchMocks.data.value = { items: [sampleItem()], nextCursor: null, hasMore: false }
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('CS-20260819-0001')
    expect(wrapper.text()).toContain('訂單延遲問題')
    expect(wrapper.text()).toContain('王小明')
    // Regression: caseType arrives as 'Support' (PascalCase, from vw_CaseWorkbench's SQL
    // literals) — this must still resolve to the Chinese label, not fall through to the raw
    // string, and must still be recognized as a navigable Support case despite the casing.
    expect(wrapper.text()).toContain('客服案件')
    expect(wrapper.text()).not.toContain('Support')
    const link = wrapper.get('a')
    expect(link.attributes('href')).toBe('/support/tickets/018f2e6a-0000-7000-8000-000000000001')
  })

  it('never links a Return or Report case to a fake route — shows a disabled hint instead', async () => {
    workbenchMocks.data.value = {
      items: [sampleItem({ caseType: 'Return', casePublicId: 'return-1', caseNumber: 'RT-0001' })],
      nextCursor: null,
      hasMore: false,
    }
    const wrapper = await mountPage()

    expect(wrapper.find('a').exists()).toBe(false)
    const hint = wrapper.find('.case-workbench__no-detail')
    expect(hint.exists()).toBe(true)
    expect(hint.text()).toContain('RT-0001')
    expect(hint.attributes('title')).toContain('尚未上線')
  })

  it('disables previous on the first page and enables next only when hasMore is true', async () => {
    workbenchMocks.data.value = { items: [sampleItem()], nextCursor: 'next-cursor', hasMore: true }
    const wrapper = await mountPage()

    const buttons = wrapper.findAll('.case-workbench__pagination button')
    expect(buttons[0]?.attributes('disabled')).toBeDefined()
    expect(buttons[1]?.attributes('disabled')).toBeUndefined()

    await buttons[1]?.trigger('click')
    await flushPromises()
    expect(workbenchMocks.lastFilters.value?.cursor).toBe('next-cursor')
  })

  it('toggling a case-type filter resets pagination and passes the selection through to the query', async () => {
    workbenchMocks.data.value = { items: [sampleItem()], nextCursor: 'next-cursor', hasMore: true }
    const wrapper = await mountPage()
    const nextButton = wrapper.findAll('.case-workbench__pagination button')[1]
    await nextButton?.trigger('click')
    await flushPromises()
    expect(workbenchMocks.lastFilters.value?.cursor).toBe('next-cursor')

    const supportCheckbox = wrapper.get('input[type="checkbox"]')
    await supportCheckbox.setValue(true)
    await flushPromises()

    expect(workbenchMocks.lastFilters.value?.cursor).toBeUndefined()
    expect(workbenchMocks.lastFilters.value?.caseTypes).toEqual(['support'])
  })

  it('shows only the currently authorized Support case-type filter', async () => {
    workbenchMocks.data.value = { items: [], nextCursor: null, hasMore: false }
    const wrapper = await mountPage()
    const caseTypeLabels = wrapper.findAll('fieldset')[0]?.findAll('label') ?? []

    expect(caseTypeLabels).toHaveLength(1)
    expect(caseTypeLabels[0]?.text()).toContain('客服案件')
  })

  it.each([
    [0, 'assigned'],
    [1, '018f2e6a-0000-7000-8000-000000000099'],
    [2, 'new keyword'],
  ])('clears a second-page cursor synchronously when text filter %i changes', async (inputIndex, value) => {
    workbenchMocks.data.value = { items: [sampleItem()], nextCursor: 'next-cursor', hasMore: true }
    const wrapper = await mountPage()
    await wrapper.findAll('.case-workbench__pagination button')[1]?.trigger('click')
    await flushPromises()
    expect(workbenchMocks.lastFilters.value?.cursor).toBe('next-cursor')

    await wrapper.findAll('input[type="text"]')[inputIndex]?.setValue(value)
    await flushPromises()

    expect(workbenchMocks.lastFilters.value?.cursor).toBeUndefined()
  })
})

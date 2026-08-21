import { ApiError } from '@doselect/web-shared/api'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import SupportSlaQueuePage from './SupportSlaQueuePage.vue'

const queueMocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')

  return {
    data: ref<Record<string, unknown> | null>(null),
    isPending: ref(false),
    isError: ref(false),
    error: ref<unknown>(null),
    refetch: vi.fn(),
    filters: null as null | (() => { pageSize?: number, cursor?: string }),
  }
})

vi.mock('../../features/support/queries', () => ({
  defaultSlaPageSize: 20,
  useSupportSlaQueueQuery: (filters: () => { pageSize?: number, cursor?: string }) => {
    queueMocks.filters = filters
    return {
      data: queueMocks.data,
      isPending: queueMocks.isPending,
      isError: queueMocks.isError,
      error: queueMocks.error,
      refetch: queueMocks.refetch,
    }
  },
}))

const ticketId = '018f2e6a-0000-7000-8000-000000000001'

function sampleItem(overrides: Record<string, unknown> = {}) {
  return {
    ticketPublicId: ticketId,
    ticketNumber: 'CS-20260819-0001',
    priority: 'urgent',
    assignee: {
      publicId: '018f2e6a-0000-7000-8000-000000000010',
      displayName: '客服小安',
      email: 'admin-secret@example.test',
      internalIdentityId: 'identity-secret',
    },
    status: 'assigned',
    firstResponseDueAtUtc: '2026-08-19T02:00:00Z',
    resolutionDueAtUtc: '2026-08-20T02:00:00Z',
    effectiveDueAtUtc: '2026-08-19T02:00:00Z',
    usageRatio: 1.25,
    isOverdue: true,
    lastActivityAtUtc: '2026-08-19T03:00:00Z',
    rowVersion: 'AAAAAAAAAAE=',
    storageKey: 'private/support/secret-object',
    physicalPath: 'D:\\private\\support\\secret.txt',
    sha256: 'secret-digest',
    ...overrides,
  }
}

async function mountPage() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/support', component: { template: '<div />' } },
      { path: '/support/tickets/:ticketId', component: { template: '<div />' } },
    ],
  })
  await router.push('/support')
  await router.isReady()

  return mount(SupportSlaQueuePage, { global: { plugins: [router] } })
}

describe('SupportSlaQueuePage', () => {
  beforeEach(() => {
    queueMocks.data.value = null
    queueMocks.isPending.value = false
    queueMocks.isError.value = false
    queueMocks.error.value = null
    queueMocks.refetch.mockReset()
    queueMocks.filters = null
  })

  it('renders loading and empty states', async () => {
    queueMocks.isPending.value = true
    const loading = await mountPage()
    expect(loading.get('[role="status"]').text()).toContain('資料載入中')

    loading.unmount()
    queueMocks.isPending.value = false
    queueMocks.data.value = { items: [], nextCursor: null, hasMore: false }
    const empty = await mountPage()
    expect(empty.get('[role="status"]').text()).toContain('目前沒有待處理案件')
  })

  it.each([
    [401, '需要登入'],
    [403, '沒有權限查看 SLA 佇列'],
    [500, '無法載入 SLA 佇列'],
  ])('renders a safe %i error and retries', async (status, title) => {
    queueMocks.isError.value = true
    queueMocks.error.value = new ApiError('安全的錯誤訊息', {
      status,
      code: 'support_queue_failed',
      correlationId: 'request-safe-id',
    })
    const wrapper = await mountPage()

    expect(wrapper.get('[role="alert"] h2').text()).toBe(title)
    expect(wrapper.text()).toContain('安全的錯誤訊息')
    expect(wrapper.text()).toContain('request-safe-id')
    expect(wrapper.text()).not.toContain('Stack Trace')

    await wrapper.get('[role="alert"] button').trigger('click')
    expect(queueMocks.refetch).toHaveBeenCalledOnce()
  })

  it('renders backend-computed SLA urgency and only public-safe row fields', async () => {
    queueMocks.data.value = { items: [sampleItem()], nextCursor: null, hasMore: false }
    const wrapper = await mountPage()
    const row = wrapper.get('tbody tr')

    expect(row.classes()).toContain('sla-queue__row--overdue')
    expect(row.text()).toContain('CS-20260819-0001')
    expect(row.text()).toContain('緊急')
    expect(row.text()).toContain('已受理')
    expect(row.text()).toContain('客服小安')
    expect(row.text()).toContain('已逾時')
    expect(row.text()).toContain('125%')
    expect(row.get('a').attributes('href')).toBe(`/support/tickets/${ticketId}`)
    expect(wrapper.text()).not.toContain('admin-secret@example.test')
    expect(wrapper.text()).not.toContain('identity-secret')
    expect(wrapper.text()).not.toContain('private/support/secret-object')
    expect(wrapper.text()).not.toContain('D:\\private\\support\\secret.txt')
    expect(wrapper.text()).not.toContain('secret-digest')
  })

  it('moves forward with the opaque next cursor and returns to the previous cursor', async () => {
    queueMocks.data.value = {
      items: [sampleItem({ isOverdue: false, usageRatio: 0.5 })],
      nextCursor: 'opaque+cursor/==',
      hasMore: true,
    }
    const wrapper = await mountPage()
    const buttons = wrapper.findAll('.sla-queue__pagination button')

    expect(queueMocks.filters?.()).toEqual({ pageSize: 20, cursor: undefined })
    expect(buttons[0]?.attributes()).toHaveProperty('disabled')

    await buttons[1]?.trigger('click')
    await nextTick()
    expect(queueMocks.filters?.()).toEqual({ pageSize: 20, cursor: 'opaque+cursor/==' })
    expect(buttons[0]?.attributes()).not.toHaveProperty('disabled')

    await buttons[0]?.trigger('click')
    await nextTick()
    expect(queueMocks.filters?.()).toEqual({ pageSize: 20, cursor: undefined })
  })
})

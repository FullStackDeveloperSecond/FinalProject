import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockListRefunds = vi.fn()
const mockGetRefund = vi.fn()
const mockExecuteRefund = vi.fn()

vi.mock('../../features/refunds/api', () => ({
  listRefunds: mockListRefunds,
  getRefund: mockGetRefund,
  executeRefund: mockExecuteRefund,
}))

const { default: AdminRefundsPage } = await import('./AdminRefundsPage.vue')

function refund(overrides: Record<string, unknown> = {}) {
  return {
    publicId: '018f2e6a-0000-7000-8000-000000000050',
    refundNumber: 'RF-202609-000001',
    orderPublicId: '018f2e6a-0000-7000-8000-000000000030',
    returnPublicId: null,
    status: 'approved',
    requestedAmount: 500,
    approvedAmount: 480,
    succeededAmount: null,
    allocations: [],
    requestedBy: null,
    approvedBy: null,
    executedBy: null,
    createdAtUtc: '2026-09-01T00:00:00Z',
    succeededAtUtc: null,
    rowVersion: 'AAAAAAAAAAE=',
    ...overrides,
  }
}

async function mountPage() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/refunds', component: AdminRefundsPage },
      { path: '/refunds/:refundId', component: { template: '<div />' } },
    ],
  })
  await router.push('/refunds')
  await router.isReady()
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return mount(AdminRefundsPage, {
    global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
  })
}

describe('AdminRefundsPage', () => {
  beforeEach(() => vi.resetAllMocks())

  it('renders the refund queue with status and approved limit', async () => {
    mockListRefunds.mockResolvedValue({
      items: [refund()], pageNumber: 1, pageSize: 20, totalCount: 1, totalPages: 1,
    })

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('RF-202609-000001')
    expect(wrapper.text()).toContain('已核准')
    expect(wrapper.text()).toContain('NT$480')
  })

  it('applies a status filter and resets to page one', async () => {
    mockListRefunds.mockResolvedValue({
      items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0,
    })

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('[aria-label="退款狀態"]').setValue('succeeded')
    await flushPromises()

    expect(mockListRefunds).toHaveBeenLastCalledWith(expect.objectContaining({
      statuses: ['succeeded'],
      pageNumber: 1,
    }))
  })
})

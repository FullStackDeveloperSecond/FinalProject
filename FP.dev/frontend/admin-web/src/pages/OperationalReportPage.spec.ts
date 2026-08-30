import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')
  return {
    data: ref<Record<string, unknown> | null>(null),
    error: ref<unknown>(null),
    actionError: ref<unknown>(null),
    isLoading: ref(false),
    isLoadingMore: ref(false),
    isExporting: ref(false),
    load: vi.fn(),
    loadMore: vi.fn(),
    download: vi.fn(),
  }
})

vi.mock('../features/operationalReports/useOperationalReport', () => ({
  useOperationalReport: () => mocks,
}))
vi.mock('../features/auth/stores/useAdminAuthStore', () => ({
  useAdminAuthStore: () => ({ currentUser: { roles: ['MarketingAnalyst'] } }),
}))

const { default: OperationalReportPage } = await import('./OperationalReportPage.vue')

async function mountPage() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/reports/:reportKey', component: OperationalReportPage }],
  })
  await router.push('/reports/sales-overview')
  await router.isReady()
  return mount(OperationalReportPage, { global: { plugins: [router] } })
}

function reportResult(rows: unknown[] = [{
  rowType: 'sales-overview',
  bucket: '2026-08-01',
  netRevenue: 1250,
  orderCount: 2,
  averageOrderValue: 625,
  refundAmount: 0,
  refundAmountRate: 0,
  cancelledOrderCount: 0,
  cancellationRate: 0,
}]) {
  return {
    reportKey: 'sales-overview',
    title: '銷售總覽',
    timeBasis: 'Payment.PaidAtUtc / Refund.SucceededAtUtc',
    timeZone: 'Asia/Taipei',
    from: '2026-08-01',
    to: '2026-09-01',
    generatedAtUtc: '2026-09-01T00:00:00Z',
    asOfUtc: '2026-09-01T00:00:00Z',
    summary: [{ metricKey: 'net_revenue', value: 1250, unit: 'currency' }],
    series: [{ bucket: '2026-08-01', metrics: [{ metricKey: 'net_revenue', value: 1250, unit: 'currency' }] }],
    rows: { items: rows, nextCursor: null, hasMore: false },
  }
}

describe('OperationalReportPage', () => {
  beforeEach(() => {
    mocks.data.value = reportResult()
    mocks.error.value = null
    mocks.actionError.value = null
    mocks.isLoading.value = false
    mocks.isLoadingMore.value = false
    mocks.isExporting.value = false
    mocks.load.mockReset()
    mocks.loadMore.mockReset()
    mocks.download.mockReset()
  })

  it('renders metadata, summary, chart and typed detail rows', async () => {
    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('Payment.PaidAtUtc / Refund.SucceededAtUtc')
    expect(wrapper.text()).toContain('NT$1,250')
    expect(wrapper.text()).toContain('2026-08-01')
    expect(wrapper.text()).not.toContain('毛利分析')
  })

  it('downloads XLSX with the currently applied filters', async () => {
    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.get('button:nth-of-type(2)').trigger('click')

    expect(mocks.download).toHaveBeenCalledWith(
      'sales-overview',
      expect.objectContaining({ timeZone: 'Asia/Taipei' }),
      'xlsx',
    )
  })

  it('derives the default date boundary in Asia/Taipei instead of UTC', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-28T16:30:00Z'))
    try {
      await mountPage()
      await flushPromises()

      expect(mocks.load.mock.calls[0]?.[1]).toEqual(expect.objectContaining({
        fromDate: '2026-07-30',
        toDate: '2026-08-29',
      }))
    } finally {
      vi.useRealTimers()
    }
  })

  it('renders an explicit empty state without inventing rows', async () => {
    mocks.data.value = reportResult([])

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('目前沒有符合條件的資料')
  })

  it('does not query draft filters until submit and then normalizes status values', async () => {
    const wrapper = await mountPage()
    await flushPromises()
    const initialCalls = mocks.load.mock.calls.length

    await wrapper.find('input[placeholder="全部分類"]').setValue(' CPU ')
    await wrapper.find('input[placeholder="Completed,Cancelled"]').setValue('Completed, Cancelled')
    expect(mocks.load).toHaveBeenCalledTimes(initialCalls)

    await wrapper.find('form').trigger('submit')

    expect(mocks.load).toHaveBeenCalledTimes(initialCalls + 1)
    expect(mocks.load).toHaveBeenLastCalledWith('sales-overview', expect.objectContaining({
      categoryCode: 'CPU',
      orderStatuses: ['Completed', 'Cancelled'],
    }))
  })

  it('rejects an invalid local date range before calling the API', async () => {
    const wrapper = await mountPage()
    await flushPromises()
    const initialCalls = mocks.load.mock.calls.length
    const dateInputs = wrapper.findAll('input[type="date"]')
    await dateInputs[0]!.setValue('2026-09-01')
    await dateInputs[1]!.setValue('2026-09-01')

    await wrapper.find('form').trigger('submit')

    expect(wrapper.text()).toContain('必須晚於開始日期')
    expect(mocks.load).toHaveBeenCalledTimes(initialCalls)
  })
})

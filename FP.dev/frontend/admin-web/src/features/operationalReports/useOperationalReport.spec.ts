import { describe, expect, it, vi } from 'vitest'

const fetchOperationalReport = vi.fn()
const exportOperationalReport = vi.fn()
vi.mock('./api', () => ({ fetchOperationalReport, exportOperationalReport }))

const { useOperationalReport } = await import('./useOperationalReport')

const filters = {
  fromDate: '2026-08-01',
  toDate: '2026-09-01',
  timeZone: 'Asia/Taipei' as const,
  categoryCode: '',
  brandCode: '',
  orderStatuses: [],
  granularity: 'day' as const,
  pageSize: 20,
}

function result(reportKey: string, items: unknown[], nextCursor: string | null = null) {
  return {
    reportKey,
    title: reportKey,
    timeBasis: 'test',
    timeZone: 'Asia/Taipei',
    from: filters.fromDate,
    to: filters.toDate,
    generatedAtUtc: '2026-09-01T00:00:00Z',
    asOfUtc: '2026-09-01T00:00:00Z',
    summary: [],
    series: [],
    rows: { items, nextCursor, hasMore: nextCursor !== null },
  }
}

describe('useOperationalReport', () => {
  it('ignores an obsolete response after the report changes', async () => {
    let resolveFirst!: (value: ReturnType<typeof result>) => void
    const first = new Promise<ReturnType<typeof result>>((resolve) => { resolveFirst = resolve })
    fetchOperationalReport
      .mockReturnValueOnce(first)
      .mockResolvedValueOnce(result('product-abc', []))
    const report = useOperationalReport()

    const pendingFirst = report.load('sales-overview', filters)
    await report.load('product-abc', filters)
    resolveFirst(result('sales-overview', []))
    await pendingFirst

    expect(report.data.value?.reportKey).toBe('product-abc')
    expect(report.isLoading.value).toBe(false)
  })

  it('appends the next cursor page without losing the first page', async () => {
    fetchOperationalReport
      .mockResolvedValueOnce(result('sales-overview', [{ bucket: 'day-1' }], 'next'))
      .mockResolvedValueOnce(result('sales-overview', [{ bucket: 'day-2' }]))
    const report = useOperationalReport()

    await report.load('sales-overview', filters)
    await report.loadMore('sales-overview', filters)

    expect(fetchOperationalReport).toHaveBeenLastCalledWith('sales-overview', filters, 'next')
    expect(report.data.value?.rows.items).toEqual([{ bucket: 'day-1' }, { bucket: 'day-2' }])
  })
})

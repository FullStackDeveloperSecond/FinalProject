import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { OperationalReportFilters } from './types'

const mockGet = vi.fn()
vi.mock('../../api/client', () => ({ apiClient: { GET: mockGet } }))

const { exportOperationalReport, fetchOperationalReport } = await import('./api')

const filters: OperationalReportFilters = {
  fromDate: '2026-08-01',
  toDate: '2026-09-01',
  timeZone: 'Asia/Taipei',
  categoryCode: 'CPU',
  brandCode: '',
  orderStatuses: ['Completed', 'Cancelled'],
  granularity: 'day',
  pageSize: 20,
}

describe('operational reports api', () => {
  beforeEach(() => mockGet.mockReset())

  it('sends the typed report query and opaque cursor', async () => {
    const result = { reportKey: 'sales-overview' }
    mockGet.mockResolvedValueOnce({ data: result })

    await expect(fetchOperationalReport('sales-overview', filters, 'opaque-next')).resolves.toBe(result)

    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/reports/{reportKey}', {
      params: {
        path: { reportKey: 'sales-overview' },
        query: {
          FromDate: '2026-08-01',
          ToDate: '2026-09-01',
          TimeZone: 'Asia/Taipei',
          CategoryCode: 'CPU',
          BrandCode: undefined,
          OrderStatuses: ['Completed', 'Cancelled'],
          Granularity: 'day',
          Cursor: 'opaque-next',
          PageSize: 20,
        },
      },
    })
  })

  it('requests CSV exports as a blob with the same applied filters', async () => {
    const blob = new Blob(['DEMO DATA'], { type: 'text/csv' })
    mockGet.mockResolvedValueOnce({ data: blob })

    await expect(exportOperationalReport('product-abc', filters)).resolves.toBe(blob)

    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/reports/{reportKey}/export', expect.objectContaining({
      params: expect.objectContaining({ path: { reportKey: 'product-abc' } }),
      parseAs: 'blob',
    }))
  })

  it('requests XLSX exports as a blob with the same applied filters', async () => {
    const blob = new Blob(['PK'], {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    })
    mockGet.mockResolvedValueOnce({ data: blob })

    await expect(exportOperationalReport('product-abc', filters, 'xlsx')).resolves.toBe(blob)

    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/reports/{reportKey}/export/xlsx', expect.objectContaining({
      params: expect.objectContaining({ path: { reportKey: 'product-abc' } }),
      parseAs: 'blob',
    }))
  })
})

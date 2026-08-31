import { apiClient } from '../../api/client'
import type {
  OperationalReportFilters,
  OperationalReportKey,
  OperationalReportResult,
} from './types'

export type OperationalReportExportFormat = 'csv' | 'xlsx'

function query(filters: OperationalReportFilters, cursor?: string) {
  return {
    FromDate: filters.fromDate,
    ToDate: filters.toDate,
    TimeZone: filters.timeZone,
    CategoryCode: filters.categoryCode || undefined,
    BrandCode: filters.brandCode || undefined,
    OrderStatuses: filters.orderStatuses.length > 0 ? filters.orderStatuses : undefined,
    Granularity: filters.granularity,
    Cursor: cursor,
    PageSize: filters.pageSize,
  }
}

export async function fetchOperationalReport(
  reportKey: OperationalReportKey,
  filters: OperationalReportFilters,
  cursor?: string,
): Promise<OperationalReportResult> {
  const { data, error } = await apiClient.GET('/api/v1/admin/reports/{reportKey}', {
    params: {
      path: { reportKey },
      query: query(filters, cursor),
    },
  })
  if (error) throw error
  return data
}

export async function exportOperationalReport(
    reportKey: OperationalReportKey,
    filters: OperationalReportFilters,
    format: OperationalReportExportFormat = 'csv',
): Promise<Blob> {
  const request = format === 'xlsx'
    ? apiClient.GET('/api/v1/admin/reports/{reportKey}/export/xlsx', {
        params: {
          path: { reportKey },
          query: query(filters),
        },
        parseAs: 'blob',
      })
    : apiClient.GET('/api/v1/admin/reports/{reportKey}/export', {
        params: {
          path: { reportKey },
          query: query(filters),
        },
        parseAs: 'blob',
      })
  const { data, error } = await request
  if (error) throw error
  if (!(data instanceof Blob)) {
    throw new Error('The report export response was not a file.')
  }
  return data
}

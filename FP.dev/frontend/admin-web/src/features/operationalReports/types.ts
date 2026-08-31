import type { components } from '@doselect/web-shared/api'

export const operationalReportKeys = [
  'sales-overview',
  'product-abc',
  'period-comparison',
  'inventory-turnover',
  'gross-margin',
  'product-associations',
  'forecast-anomalies',
] as const

export type OperationalReportKey = typeof operationalReportKeys[number]
export type OperationalReportResult = components['schemas']['ReportResultDto']
export type OperationalReportRow = components['schemas']['ReportRowDto']

export interface OperationalReportFilters {
  fromDate: string
  toDate: string
  timeZone: 'Asia/Taipei'
  categoryCode: string
  brandCode: string
  orderStatuses: string[]
  granularity: 'day' | 'week' | 'month'
  pageSize: number
}

export interface OperationalReportDefinition {
  key: OperationalReportKey
  title: string
  financial: boolean
}

export const operationalReportDefinitions: readonly OperationalReportDefinition[] = [
  { key: 'sales-overview', title: '銷售總覽', financial: false },
  { key: 'product-abc', title: '商品排行與 ABC 分級', financial: false },
  { key: 'period-comparison', title: '同期比較', financial: false },
  { key: 'inventory-turnover', title: '庫存周轉分析', financial: true },
  { key: 'gross-margin', title: '毛利分析', financial: true },
  { key: 'product-associations', title: '關聯組合分析', financial: false },
  { key: 'forecast-anomalies', title: '預測與異常偵測', financial: false },
]

export function isOperationalReportKey(value: unknown): value is OperationalReportKey {
  return typeof value === 'string' && operationalReportKeys.includes(value as OperationalReportKey)
}

export function reportDefinition(key: OperationalReportKey): OperationalReportDefinition {
  const definition = operationalReportDefinitions.find((candidate) => candidate.key === key)
  if (!definition) {
    throw new Error(`Missing report definition for ${key}`)
  }
  return definition
}

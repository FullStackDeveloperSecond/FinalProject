import type { components } from '@doselect/web-shared/api'
import type { OperationalReportKey, OperationalReportRow } from './types'

type SalesRow = components['schemas']['ReportRowDtoSalesOverviewReportRowDto']
type ProductAbcRow = components['schemas']['ReportRowDtoProductAbcReportRowDto']
type PeriodComparisonRow = components['schemas']['ReportRowDtoPeriodComparisonReportRowDto']
type InventoryTurnoverRow = components['schemas']['ReportRowDtoInventoryTurnoverReportRowDto']
type GrossMarginRow = components['schemas']['ReportRowDtoGrossMarginReportRowDto']
type ProductAssociationRow = components['schemas']['ReportRowDtoProductAssociationReportRowDto']
type ForecastAnomalyRow = components['schemas']['ReportRowDtoForecastAnomalyReportRowDto']

const metricLabels: Readonly<Record<string, string>> = {
  net_revenue: '淨營收',
  order_count: '訂單數',
  average_order_value: '平均客單價',
  refund_amount: '退款金額',
  refund_amount_rate: '退款率',
  cancellation_rate: '取消率',
  sku_count: 'SKU 數',
  gross_profit: '毛利',
  gross_margin_rate: '毛利率',
  pair_count: '有效組合數',
  anomaly_count: '異常日數',
}

const tableHeaders: Readonly<Record<OperationalReportKey, readonly string[]>> = {
  'sales-overview': ['期間', '淨營收', '訂單數', '平均客單價', '退款金額', '退款率', '取消訂單', '取消率'],
  'product-abc': ['排名', 'SKU', '商品', '數量', '淨營收', '營收占比', '累計占比', '分級'],
  'period-comparison': ['指標', '本期', '比較期', '變化率', '狀態'],
  'inventory-turnover': ['SKU', '商品', '銷貨成本', '期初庫存成本', '期末庫存成本', '週轉率', '週轉天數', '可用庫存', '狀態'],
  'gross-margin': ['SKU', '商品', '淨營收', '銷貨成本', '毛利', '毛利率', '銷售數量', '退款數量'],
  'product-associations': ['來源 SKU', '來源商品', '關聯 SKU', '關聯商品', '共同訂單', 'Support', 'Confidence', 'Lift'],
  'forecast-anomalies': ['日期', '實際值', '預測值', '殘差 Z-Score', '判定'],
}

export function metricLabel(metricKey: string): string {
  return metricLabels[metricKey] ?? metricKey.replaceAll('_', ' ')
}

export function formatMetric(value: number | string | null, unit: string): string {
  if (value === null || value === '') return '—'
  const number = Number(value)
  if (!Number.isFinite(number)) return String(value)
  if (unit === 'currency') return `NT$${number.toLocaleString('zh-TW', { maximumFractionDigits: 0 })}`
  if (unit === 'percent' || unit === 'ratio') return `${(number * 100).toFixed(2)}%`
  if (unit === 'days') return `${number.toFixed(2)} 天`
  return number.toLocaleString('zh-TW', { maximumFractionDigits: 2 })
}

function number(value: number | string | null, digits = 2): string {
  if (value === null || value === '') return '—'
  const parsed = Number(value)
  return Number.isFinite(parsed)
    ? parsed.toLocaleString('zh-TW', { maximumFractionDigits: digits })
    : String(value)
}

function currency(value: number | string | null): string {
  return value === null ? '—' : formatMetric(value, 'currency')
}

function percent(value: number | string | null): string {
  return value === null ? '—' : formatMetric(value, 'percent')
}

export function headersFor(key: OperationalReportKey): readonly string[] {
  return tableHeaders[key]
}

export function cellsFor(key: OperationalReportKey, value: OperationalReportRow): string[] {
  switch (key) {
    case 'sales-overview': {
      const row = value as SalesRow
      return [row.bucket, currency(row.netRevenue), number(row.orderCount, 0), currency(row.averageOrderValue), currency(row.refundAmount), percent(row.refundAmountRate), number(row.cancelledOrderCount, 0), percent(row.cancellationRate)]
    }
    case 'product-abc': {
      const row = value as ProductAbcRow
      return [number(row.rank, 0), row.skuCode, row.skuName, number(row.quantity, 0), currency(row.netRevenue), percent(row.revenueShare), percent(row.cumulativeRevenueShare), row.abcClass]
    }
    case 'period-comparison': {
      const row = value as PeriodComparisonRow
      return [metricLabel(row.metricKey), number(row.currentValue), number(row.previousValue), percent(row.changeRate), row.isNew ? '新增' : '既有']
    }
    case 'inventory-turnover': {
      const row = value as InventoryTurnoverRow
      const states = [row.isOutOfStock && '缺貨', row.isLowStock && '低庫存', row.isLongTermUnsold && '長期未售', row.isInsufficientData && '資料不足'].filter(Boolean)
      return [row.skuCode, row.skuName, currency(row.costOfGoodsSold), currency(row.beginningInventoryCost), currency(row.endingInventoryCost), number(row.turnoverRate), number(row.turnoverDays), number(row.availableQuantity, 0), states.join('、') || '正常']
    }
    case 'gross-margin': {
      const row = value as GrossMarginRow
      return [row.skuCode, row.skuName, currency(row.netRevenue), currency(row.costOfGoodsSold), currency(row.grossProfit), percent(row.grossMarginRate), number(row.quantitySold, 0), number(row.refundedQuantity, 0)]
    }
    case 'product-associations': {
      const row = value as ProductAssociationRow
      return [row.leftSkuCode, row.leftSkuName, row.rightSkuCode, row.rightSkuName, number(row.coOccurrenceOrderCount, 0), percent(row.support), percent(row.confidence), number(row.lift)]
    }
    case 'forecast-anomalies': {
      const row = value as ForecastAnomalyRow
      return [row.date, number(row.actualValue), number(row.forecastValue), number(row.zScore), row.isInsufficientData ? '資料不足' : row.isAnomaly ? '異常' : '正常']
    }
  }
}

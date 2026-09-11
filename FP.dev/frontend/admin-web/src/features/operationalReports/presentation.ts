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
  paid_amount: '已付款金額',
  net_revenue: '淨營收',
  order_count: '訂單數',
  average_order_value: '平均客單價',
  refund_amount: '退款金額',
  refund_amount_rate: '退款率',
  cancellation_rate: '取消率',
  quantity: '銷售數量',
  sku_count: 'SKU 數',
  cost_of_goods_sold: '銷貨成本',
  gross_profit: '毛利',
  gross_margin_rate: '毛利率',
  quantity_sold: '銷售數量',
  refunded_quantity: '退款數量',
  low_stock_count: '低庫存 SKU 數',
  out_of_stock_count: '缺貨 SKU 數',
  long_term_unsold_count: '長期未售 SKU 數',
  insufficient_data_count: '資料不足 SKU 數',
  pair_count: '有效組合數',
  completed_order_count: '已完成訂單數',
  directional_rule_count: '關聯規則數',
  observed_days: '觀測天數',
  forecast_days: '預測天數',
  anomaly_count: '異常日數',
  slope: '每日變化趨勢',
  turnover_rate: '庫存週轉率',
  turnover_days: '庫存週轉天數',
  available_quantity: '可用庫存',
  actual_quantity: '實際數量',
  forecast_quantity: '預測數量',
  residual_z_score: '殘差 Z 分數',
}

const paymentMethodLabels: Readonly<Record<string, string>> = {
  credit_card: '信用卡付款',
  atm: '銀行轉帳',
  convenience_code: '超商代碼',
  cash_on_delivery: '貨到付款',
  line_pay: 'LINE Pay',
  apple_pay: 'Apple Pay',
  google_pay: 'Google Pay',
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
  const knownLabel = metricLabels[metricKey]
  if (knownLabel) return knownLabel

  const paymentMethodMatch = /^payment_method_(.+)_share$/.exec(metricKey)
  if (paymentMethodMatch) {
    const paymentMethod = paymentMethodMatch[1] ?? ''
    return `${paymentMethodLabels[paymentMethod] ?? '其他付款方式'}占比`
  }

  return '其他指標'
}

export function formatMetric(value: number | string | null, unit: string): string {
  if (value === null || value === '') return '—'
  const number = Number(value)
  if (!Number.isFinite(number)) return String(value)
  if (unit === 'currency' || unit === 'TWD') return `NT$${number.toLocaleString('zh-TW', { maximumFractionDigits: 0 })}`
  if (unit === 'percent' || unit === 'ratio') return `${(number * 100).toFixed(2)}%`
  if (unit === 'days') return `${number.toFixed(2)} 天`
  return number.toLocaleString('zh-TW', { maximumFractionDigits: 2 })
}

export function unitLabel(unit: string): string {
  const labels: Readonly<Record<string, string>> = {
    currency: '新臺幣',
    TWD: '新臺幣',
    percent: '百分比',
    ratio: '比率',
    days: '天',
    day: '天',
    count: '筆數',
    quantity: '數量',
    quantity_per_day: '每日數量',
    z_score: '標準分數',
  }
  return labels[unit] ?? '數值'
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

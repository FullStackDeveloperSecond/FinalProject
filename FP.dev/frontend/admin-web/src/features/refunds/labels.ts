import type { RefundAllocationType, RefundStatus } from './types'

export const refundStatusLabels: Record<RefundStatus, string> = {
  pendingReview: '待審核',
  approved: '已核准',
  rejected: '已拒絕',
  processing: '處理中',
  succeeded: '退款成功',
  failed: '退款失敗',
  cancelled: '已取消',
}

export const refundAllocationLabels: Record<RefundAllocationType, string> = {
  itemRefund: '商品退款',
  originalShipping: '原訂單運費退還',
  returnShipping: '退貨運費補償',
  assemblyFee: '組裝費退還',
  discountClawback: '優惠追回',
  shippingClawback: '免運優惠追回',
  otherAdjustment: '歷史其他調整',
}

const debitTypes = new Set<RefundAllocationType>([
  'discountClawback',
  'shippingClawback',
])

export function allocationSign(type: RefundAllocationType): '+' | '-' {
  return debitTypes.has(type) ? '-' : '+'
}

export function formatRefundMoney(value: number | string | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }
  return `NT$${Number(value).toLocaleString('zh-TW', { maximumFractionDigits: 2 })}`
}

export function formatRefundDate(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString('zh-TW') : '—'
}

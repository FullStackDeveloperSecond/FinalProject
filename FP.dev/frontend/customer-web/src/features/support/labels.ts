// The API only ever sends stable English tokens (see 資料字典/API DTO契約) — the frontend
// owns turning them into Traditional Chinese labels. Keep these in sync with the enum values
// in the generated schema; nothing here should be treated as authoritative business meaning.
import type { CasePriority, SupportTicketCategory, SupportTicketStatus } from './types'

export const categoryLabels: Record<SupportTicketCategory, string> = {
  order: '訂單',
  payment: '付款',
  logistics: '物流',
  productWarranty: '商品與保固',
  returnHelp: '退貨協助',
  account: '帳號',
  other: '其他',
}

export const statusLabels: Record<SupportTicketStatus, string> = {
  open: '待處理',
  assigned: '已受理',
  inProgress: '處理中',
  waitingForCustomer: '等待您的回覆',
  waitingForInternal: '客服處理中',
  resolved: '已解決',
  closed: '已結案',
  cancelled: '已取消',
}

export const priorityLabels: Record<CasePriority, string> = {
  low: '低',
  normal: '一般',
  high: '高',
  urgent: '緊急',
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—'
  }

  return new Intl.DateTimeFormat('zh-TW', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Asia/Taipei',
  }).format(new Date(value))
}

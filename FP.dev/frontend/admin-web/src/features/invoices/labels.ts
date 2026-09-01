import type { SimulatedInvoiceStatus } from './types'

export const invoiceStatusLabels: Record<SimulatedInvoiceStatus, string> = {
  pending: '待開立',
  issued: '已開立',
  voided: '已作廢',
  partiallyAllowed: '部分折讓',
  fullyAllowed: '全額折讓',
}

export function formatInvoiceMoney(value: number | string): string {
  return new Intl.NumberFormat('zh-TW', {
    style: 'currency',
    currency: 'TWD',
    maximumFractionDigits: 2,
  }).format(Number(value))
}

export function formatInvoiceDate(value?: string | null): string {
  return value ? new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'short' })
    .format(new Date(value)) : '—'
}

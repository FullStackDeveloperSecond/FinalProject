// See customer-web/src/features/returns/labels.ts for why these are numeric-keyed: origin/dev
// has no global JsonStringEnumConverter yet, so enums serialize as raw ordinal ints.
export const statusLabels: Record<number, string> = {
  0: '已申請', // Requested
  1: '審核中', // UnderReview
  2: '已核准', // Approved
  3: '等待寄回', // AwaitingShipment
  4: '寄回運送中', // InTransit
  5: '商家已收件', // Received
  6: '商品檢查中', // Inspecting
  7: '等待退款', // AwaitingRefund
  8: '已完成', // Completed
  9: '已拒絕', // Rejected
  10: '已取消', // Cancelled
}

export const priorityLabels: Record<number, string> = {
  0: '低', // Low
  1: '一般', // Normal
  2: '高', // High
  3: '急件', // Urgent
}

export const restockDispositionLabels: Record<number, string> = {
  0: '可轉售 Resellable',
  1: '隔離 Quarantine',
  2: '報廢 Scrap',
}

export const conditionCodeOptions = [
  'Unopened',
  'OpenedForInspection',
  'Installed',
  'Used',
  'Damaged',
  'MissingAccessories',
  'Activated',
] as const

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

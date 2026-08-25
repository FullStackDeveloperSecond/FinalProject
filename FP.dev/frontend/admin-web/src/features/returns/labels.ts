// The shared ApiFoundationExtensions now registers a global JsonStringEnumConverter
// (JsonNamingPolicy.CamelCase, allowIntegerValues: false) — added by the merged Support PR —
// so every enum on the wire is a camelCase string, not a raw ordinal int.
export const statusLabels: Record<string, string> = {
  requested: '已申請',
  underReview: '審核中',
  approved: '已核准',
  awaitingShipment: '等待寄回',
  inTransit: '寄回運送中',
  received: '商家已收件',
  inspecting: '商品檢查中',
  awaitingRefund: '等待退款',
  completed: '已完成',
  rejected: '已拒絕',
  cancelled: '已取消',
}

export const priorityLabels: Record<string, string> = {
  low: '低',
  normal: '一般',
  high: '高',
  urgent: '急件',
}

export const restockDispositionLabels: Record<string, string> = {
  resellable: '可轉售 Resellable',
  quarantine: '隔離 Quarantine',
  scrap: '報廢 Scrap',
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

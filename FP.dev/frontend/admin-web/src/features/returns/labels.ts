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

// 文字直接沿用後端 AssemblyFeeDisposition 列舉的 XML 文件註解（RefundCalculation.cs），
// 避免前後端各自維護一套用語、之後對不上（alex 2026-09-05 #109 裁定）。
export const assemblyFeeDispositionOptions: Array<{ value: string, label: string }> = [
  { value: 'notApplicable', label: '訂單沒有組裝電腦' },
  { value: 'notStarted', label: '尚未開始組裝' },
  { value: 'merchantCancelled', label: '商家取消或無法組裝' },
  { value: 'assemblyFault', label: '組裝錯誤或服務瑕疵' },
  { value: 'merchantFaultWholeUnit', label: '整台因商家責任退回' },
  { value: 'completedPartialReturn', label: '組裝正常完成後只退其中一個零件' },
]

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

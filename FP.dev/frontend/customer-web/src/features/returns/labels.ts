// The shared ApiFoundationExtensions now registers a global JsonStringEnumConverter
// (JsonNamingPolicy.CamelCase, allowIntegerValues: false) — added by the merged Support PR —
// so every enum on the wire is a camelCase string, not a raw ordinal int. Keys below are
// DoSelect.Domain.Returns.ReturnRequestStatus's names camelCased to match.
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

export const reasonLabels: Record<string, string> = {
  CoolingOff: '一般退貨（猶豫期）',
  Defective: '商品瑕疵',
  WrongItem: '寄錯商品',
  ShippingDamage: '運送損壞',
  Warranty: '保固處理',
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

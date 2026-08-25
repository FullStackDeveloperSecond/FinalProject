// origin/dev's shared ApiFoundationExtensions does not register a global
// JsonStringEnumConverter (unlike the Support module's own branch) — changing that is shared
// infrastructure out of this PR's scope, so every enum on the wire is presently a raw ordinal
// int, not a camelCase string. Keys below are DoSelect.Domain.Returns.ReturnRequestStatus's
// declared enum order; if the team later adds a global string converter, switch these to
// string keys.
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

import { formatTaipeiDateTime } from '@doselect/web-shared/datetime'

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

export const inspectionStatusLabels: Record<string, string> = {
  NotInspected: '尚未檢查',
  Resellable: '可重新販售',
  Quarantine: '隔離保管',
  Scrap: '報廢',
}

export const shipmentMethodLabels: Record<string, string> = {
  homePickup: '到府取件',
  convenienceStore: '超商寄回',
  selfShip: '自行寄回',
}

export const shipmentStatusLabels: Record<string, string> = {
  pending: '待安排',
  scheduled: '已安排',
  pickedUp: '已取件',
  inTransit: '運送中',
  delivered: '已送達商家',
  failed: '寄回失敗',
  cancelled: '已取消',
}

export function formatDateTime(value: string | null | undefined): string {
  return formatTaipeiDateTime(value)
}

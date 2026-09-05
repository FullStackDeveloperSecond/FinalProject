import type { components } from '@doselect/web-shared/api'
import { createApiClient } from '../../api/client'

export type OrderStatus = 'PendingPayment' | 'Confirmed' | 'Processing' | 'Completed' | 'Cancelled'
export type PaymentStatus =
  | 'Pending'
  | 'AwaitingPayment'
  | 'Processing'
  | 'Paid'
  | 'Failed'
  | 'Cancelled'
  | 'Expired'
export type FulfillmentStatus =
  | 'Pending'
  | 'Preparing'
  | 'Shipped'
  | 'InTransit'
  | 'PickupReady'
  | 'PickedUp'
  | 'Delivered'
  | 'DeliveryFailed'
  | 'Returned'
export type AssemblyStatus =
  | 'NotRequired'
  | 'Pending'
  | 'Started'
  | 'Testing'
  | 'ReadyToShip'
  | 'Failed'
  | 'Cancelled'
export type OrderRefundStatus = 'None' | 'Pending' | 'PartiallyRefunded' | 'Refunded'

export type SummaryStatus = 'awaitingShipment' | 'shipped' | 'completed' | 'cancelled'
export type OrderBadge = 'partiallyRefunded' | 'refunded' | 'paymentOverdue'

export const SUMMARY_STATUS_OPTIONS: ReadonlyArray<{ value: SummaryStatus; label: string }> = [
  { value: 'awaitingShipment', label: '待出貨' },
  { value: 'shipped', label: '已出貨' },
  { value: 'completed', label: '已完成' },
  { value: 'cancelled', label: '已取消' },
]

export const BADGE_OPTIONS: ReadonlyArray<{ value: OrderBadge; label: string }> = [
  { value: 'partiallyRefunded', label: '部分退款' },
  { value: 'refunded', label: '已退款' },
  { value: 'paymentOverdue', label: '付款逾期' },
]

const summaryStatusLabels: Record<string, string> = Object.fromEntries(
  SUMMARY_STATUS_OPTIONS.map(option => [option.value, option.label]),
)
const badgeLabels: Record<string, string> = Object.fromEntries(
  BADGE_OPTIONS.map(option => [option.value, option.label]),
)

export function summaryStatusLabel(value: string): string {
  return summaryStatusLabels[value] ?? value
}

export function badgeLabel(value: string): string {
  return badgeLabels[value] ?? value
}

export const orderStatusLabel: Record<string, string> = {
  PendingPayment: '等待付款',
  Confirmed: '已確認',
  Processing: '處理中',
  Completed: '已完成',
  Cancelled: '已取消',
}

/**
 * API Endpoint目錄.md 沒有列出 POST .../actions/{action} 的白名單，只有非窮舉例子。
 * 這裡只開放後端 AdminOrderActions 目前實作的兩個動作（見 AdminOrderContracts.cs 註解），
 * 待 alex 確認正式清單後再擴充（例如是否要納入建立出貨）。
 */
export const ORDER_ACTION_OPTIONS: ReadonlyArray<{ value: string; label: string; requiresReason: boolean }> = [
  { value: 'startProcessing', label: '開始備貨／組裝', requiresReason: false },
  { value: 'cancel', label: '人工取消訂單', requiresReason: true },
]

/**
 * M-11 物流狀態命令（組長 2026-09-04 裁定 A1）：六個 action 對應後端 ShipmentStatusActions；
 * delivery-failed／returned 必填 reasonCode。哪些可按由後端的 `shipment.availableActions` 決定，這裡只有文案。
 */
export const SHIPMENT_ACTION_OPTIONS: ReadonlyArray<{ value: string; label: string; requiresReason: boolean }> = [
  { value: 'in-transit', label: '配送中', requiresReason: false },
  { value: 'delivered', label: '宅配送達', requiresReason: false },
  { value: 'pickup-ready', label: '超商到店', requiresReason: false },
  { value: 'picked-up', label: '顧客取貨', requiresReason: false },
  { value: 'delivery-failed', label: '配送失敗', requiresReason: true },
  { value: 'returned', label: '退回商家', requiresReason: true },
]

/** 對應後端 ShipmentStatusReasonCodes 白名單，兩邊需同步維護。 */
export const SHIPMENT_REASON_OPTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: 'recipient_absent', label: '無人簽收' },
  { value: 'address_invalid', label: '地址錯誤' },
  { value: 'recipient_refused', label: '收件人拒收' },
  { value: 'pickup_expired', label: '逾期未取' },
  { value: 'package_damaged', label: '包裹損毀' },
  { value: 'carrier_issue', label: '物流業者問題' },
  { value: 'redelivery', label: '再次配送' },
  { value: 'other', label: '其他' },
]

const fulfillmentStatusLabels: Record<string, string> = {
  Pending: '待處理',
  Preparing: '備貨中',
  Shipped: '已出貨',
  InTransit: '配送中',
  PickupReady: '超商到店',
  PickedUp: '已取貨',
  Delivered: '已送達',
  DeliveryFailed: '配送失敗',
  Returned: '已退回',
}

export function fulfillmentStatusLabel(value: string): string {
  return fulfillmentStatusLabels[value] ?? value
}

export interface CursorPage<T> {
  items: T[]
  nextCursor?: string | null
  hasMore: boolean
}

export interface AdminOrderSummaryDto {
  publicId: string
  orderNumber: string
  buyerType: string
  maskedBuyerEmail: string
  orderStatus: OrderStatus
  paymentStatus: PaymentStatus
  fulfillmentStatus: FulfillmentStatus
  assemblyStatus: AssemblyStatus
  orderRefundStatus: OrderRefundStatus
  summaryStatus: SummaryStatus
  badges: OrderBadge[]
  grandTotal: number
  currency: string
  shippingMethodCode: string
  createdAtUtc: string
  paidAtUtc?: string | null
  shippedAtUtc?: string | null
  deliveredAtUtc?: string | null
  completedAtUtc?: string | null
  rowVersion: string
}

export interface AdminOrderItemDto {
  publicId: string
  skuCodeSnapshot: string
  productNameSnapshot: string
  skuNameSnapshot: string
  quantity: number
  listUnitPrice: number
  saleUnitPrice: number
  finalUnitPrice: number
  lineSubtotal: number
  discountAllocation: number
  lineTotal: number
  returnableQuantity: number
  returnedQuantity: number
}

/**
 * alex PR #47 review round 2, P3 item: this file hand-writes response DTOs instead of importing
 * `components['schemas'][...]` from the generated OpenAPI schema directly, which is how
 * `unitCostSnapshot` (removed from the backend DTO) and `actorUserId` (renamed to
 * `actorPublicId`) went stale here without anyone noticing. Keeping the hand-written types for
 * now (they're friendlier to read than the generated `number | string` unions), but this check
 * fails to compile if a hand-written key stops existing in the generated schema — the same class
 * of drift that caused this review comment.
 */
type RequireKeysSubsetOfSchema<T, SchemaKeys extends string> = keyof T extends SchemaKeys
  ? true
  : { staleFieldsNotInGeneratedSchema: Exclude<keyof T, SchemaKeys> }
type ExpectTrue<T extends true> = T
/** Compiles only while every AdminOrderItemDto field still exists in the generated schema. */
export type AdminOrderItemDtoStaysInSyncWithSchema = ExpectTrue<
  RequireKeysSubsetOfSchema<AdminOrderItemDto, keyof components['schemas']['AdminOrderItemDto']>
>
/** Compiles only while every OrderStatusHistoryDto field still exists in the generated schema. */
export type OrderStatusHistoryDtoStaysInSyncWithSchema = ExpectTrue<
  RequireKeysSubsetOfSchema<OrderStatusHistoryDto, keyof components['schemas']['OrderStatusHistoryDto']>
>

export interface AdminOrderAmountsDto {
  merchandiseSubtotal: number
  itemDiscountTotal: number
  shippingFee: number
  assemblyFee: number
  grandTotal: number
  paidAmount: number
  refundedAmount: number
  currency: string
}

export interface OrderStatusHistoryDto {
  stateDimension: string
  fromStatus?: string | null
  toStatus: string
  reasonCode?: string | null
  actorPublicId?: string | null
  occurredAtUtc: string
}

export interface AdminShipmentHistoryDto {
  fromStatus?: string | null
  toStatus: string
  actorPublicId?: string | null
  occurredAtUtc: string
}

/** C1（組長 2026-09-04）：訂單明細上的物流摘要；availableActions 由後端算，前端只照單顯示。 */
export interface AdminShipmentDto {
  publicId: string
  shipmentNumber: string
  trackingNumber?: string | null
  status: FulfillmentStatus
  shippingMethodCode: string
  shippedAtUtc?: string | null
  deliveredAtUtc?: string | null
  history: AdminShipmentHistoryDto[]
  availableActions: string[]
  rowVersion: string
}

export interface AdminOrderDto {
  publicId: string
  orderNumber: string
  buyerType: string
  maskedBuyerEmail: string
  orderStatus: OrderStatus
  paymentStatus: PaymentStatus
  fulfillmentStatus: FulfillmentStatus
  assemblyStatus: AssemblyStatus
  orderRefundStatus: OrderRefundStatus
  summaryStatus: SummaryStatus
  badges: OrderBadge[]
  items: AdminOrderItemDto[]
  amounts: AdminOrderAmountsDto
  shippingMethodCode: string
  storeName?: string | null
  statusHistory: OrderStatusHistoryDto[]
  availableActions: string[]
  paymentDueAtUtc?: string | null
  confirmedAtUtc?: string | null
  paidAtUtc?: string | null
  shippedAtUtc?: string | null
  deliveredAtUtc?: string | null
  completedAtUtc?: string | null
  cancelledAtUtc?: string | null
  createdAtUtc: string
  rowVersion: string
  shipment?: AdminShipmentDto | null
}

export interface OrderRecipientDto {
  orderPublicId: string
  recipientName: string
  recipientPhone: string
  recipientEmail: string
  postalCode?: string | null
  recipientCity?: string | null
  recipientDistrict?: string | null
  addressLine1?: string | null
  addressLine2?: string | null
  shippingMethodCode: string
  storeCode?: string | null
  storeName?: string | null
  storeAddress?: string | null
  accessPurpose: string
}

export interface AdminOrderListFilters {
  summaryStatus: SummaryStatus[]
  badge: OrderBadge[]
  cursor?: string
  pageSize: number
}

export interface AdminOrderActionRequestBody {
  reasonCode?: string
  note?: string
  rowVersion: string
}

/** A1：`shipmentRowVersion`、`reasonCode?`、`note?`；不送 occurredAtUtc（伺服器 UTC）。 */
export interface ShipmentStatusActionRequestBody {
  shipmentRowVersion: string
  reasonCode?: string
  note?: string
}

interface AdminOrdersPaths {
  '/api/v1/admin/orders': {
    get: {
      parameters: {
        query: {
          summaryStatus?: string[]
          badge?: string[]
          cursor?: string
          pageSize?: number
        }
      }
      responses: {
        200: { content: { 'application/json': CursorPage<AdminOrderSummaryDto> } }
      }
    }
  }
  '/api/v1/admin/orders/{id}': {
    get: {
      parameters: { path: { id: string } }
      responses: {
        200: { content: { 'application/json': AdminOrderDto } }
      }
    }
  }
  '/api/v1/admin/orders/{id}/recipient': {
    get: {
      parameters: { path: { id: string } }
      responses: {
        200: { content: { 'application/json': OrderRecipientDto } }
      }
    }
  }
  '/api/v1/admin/orders/{id}/actions/{actionName}': {
    post: {
      parameters: { path: { id: string; actionName: string } }
      requestBody: { content: { 'application/json': AdminOrderActionRequestBody } }
      responses: {
        200: { content: { 'application/json': AdminOrderDto } }
      }
    }
  }
  '/api/v1/admin/shipments/{shipmentPublicId}/actions/{shipmentAction}': {
    post: {
      parameters: {
        path: { shipmentPublicId: string; shipmentAction: string }
        header: { 'Idempotency-Key': string }
      }
      requestBody: { content: { 'application/json': ShipmentStatusActionRequestBody } }
      responses: {
        200: { content: { 'application/json': AdminOrderDto } }
      }
    }
  }
}

const client = createApiClient<AdminOrdersPaths>()

export async function fetchAdminOrders(
  filters: AdminOrderListFilters,
): Promise<CursorPage<AdminOrderSummaryDto>> {
  const { data } = await client.GET('/api/v1/admin/orders', {
    params: {
      query: {
        summaryStatus: filters.summaryStatus.length > 0 ? filters.summaryStatus : undefined,
        badge: filters.badge.length > 0 ? filters.badge : undefined,
        cursor: filters.cursor,
        pageSize: filters.pageSize,
      },
    },
  })
  return data as CursorPage<AdminOrderSummaryDto>
}

export async function fetchAdminOrder(publicId: string): Promise<AdminOrderDto> {
  const { data } = await client.GET('/api/v1/admin/orders/{id}', {
    params: { path: { id: publicId } },
  })
  return data as AdminOrderDto
}

export async function fetchAdminOrderRecipient(publicId: string): Promise<OrderRecipientDto> {
  const { data } = await client.GET('/api/v1/admin/orders/{id}/recipient', {
    params: { path: { id: publicId } },
  })
  return data as OrderRecipientDto
}

export async function executeAdminOrderAction(
  publicId: string,
  actionName: string,
  body: AdminOrderActionRequestBody,
): Promise<AdminOrderDto> {
  const { data } = await client.POST('/api/v1/admin/orders/{id}/actions/{actionName}', {
    params: { path: { id: publicId, actionName } },
    body,
  })
  return data as AdminOrderDto
}

/**
 * M-11 物流狀態命令。Idempotency-Key 由呼叫端在開啟表單時產生、失敗重試沿用同一把，成功後才換新
 * （同 A-16 批次出貨的做法）；成功回傳更新後的 AdminOrderDto（C1）。
 */
export async function executeShipmentStatusAction(
  shipmentPublicId: string,
  shipmentAction: string,
  body: ShipmentStatusActionRequestBody,
  idempotencyKey: string,
): Promise<AdminOrderDto> {
  const { data } = await client.POST('/api/v1/admin/shipments/{shipmentPublicId}/actions/{shipmentAction}', {
    params: {
      path: { shipmentPublicId, shipmentAction },
      header: { 'Idempotency-Key': idempotencyKey },
    },
    body,
  })
  return data as AdminOrderDto
}

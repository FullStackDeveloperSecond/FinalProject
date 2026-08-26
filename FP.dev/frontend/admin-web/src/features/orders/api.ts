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
  unitCostSnapshot: number
  lineSubtotal: number
  discountAllocation: number
  lineTotal: number
  returnableQuantity: number
  returnedQuantity: number
}

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
  actorUserId?: string | null
  occurredAtUtc: string
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

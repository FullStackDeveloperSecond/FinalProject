import { createApiClient } from '../../api/client'

export type OrderStatus = 'pendingPayment' | 'confirmed' | 'processing' | 'completed' | 'cancelled'
export type PaymentStatus =
  | 'pending'
  | 'awaitingPayment'
  | 'processing'
  | 'paid'
  | 'failed'
  | 'cancelled'
  | 'expired'
export type FulfillmentStatus =
  | 'pending'
  | 'preparing'
  | 'shipped'
  | 'inTransit'
  | 'pickupReady'
  | 'pickedUp'
  | 'delivered'
  | 'deliveryFailed'
  | 'returned'
export type AssemblyStatus =
  | 'notRequired'
  | 'pending'
  | 'started'
  | 'testing'
  | 'readyToShip'
  | 'failed'
  | 'cancelled'
export type OrderRefundStatus = 'none' | 'pending' | 'partiallyRefunded' | 'refunded'

export interface OrderItemDto {
  publicId: string
  skuCodeSnapshot: string
  productNameSnapshot: string
  skuNameSnapshot: string
  quantity: number
  finalUnitPrice: number
  lineTotal: number
  returnableQuantity: number
  returnedQuantity: number
}

export interface OrderAmountsDto {
  merchandiseSubtotal: number
  itemDiscountTotal: number
  shippingFee: number
  assemblyFee: number
  grandTotal: number
  paidAmount: number
  refundedAmount: number
  currency: string
}

export interface OrderRecipientSummaryDto {
  recipientName: string
  shippingMethodCode: string
  storeName?: string | null
}

export interface OrderDto {
  publicId: string
  orderNumber: string
  orderStatus: OrderStatus
  paymentStatus: PaymentStatus
  fulfillmentStatus: FulfillmentStatus
  assemblyStatus: AssemblyStatus
  orderRefundStatus: OrderRefundStatus
  items: OrderItemDto[]
  recipient: OrderRecipientSummaryDto
  amounts: OrderAmountsDto
  paymentDueAtUtc?: string | null
  confirmedAtUtc?: string | null
  paidAtUtc?: string | null
  shippedAtUtc?: string | null
  deliveredAtUtc?: string | null
  completedAtUtc?: string | null
  cancelledAtUtc?: string | null
  returnRequestDeadlineUtc?: string | null
  availableActions: string[]
  rowVersion: string
}

export interface CancelOrderRequestBody {
  reasonCode: string
  note?: string
  orderRowVersion: string
}

/**
 * 退貨與退款政策.md 只定義「顧客可選理由」這個限制，沒有列出正式代碼表 —
 * 對應後端 OrderContracts.cs 的 OrderCancellationReasonCodes，兩邊需同步維護。
 */
export const CANCELLATION_REASON_OPTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: 'changed_mind', label: '改變心意，不想要了' },
  { value: 'ordered_by_mistake', label: '下單時選錯商品或數量' },
  { value: 'found_better_price', label: '找到更便宜的選擇' },
  { value: 'shipping_too_slow', label: '出貨或到貨時間太久' },
  { value: 'other', label: '其他原因' },
]

interface OrdersPaths {
  '/api/v1/orders/{id}': {
    get: {
      parameters: { path: { id: string } }
      responses: {
        200: { content: { 'application/json': OrderDto } }
      }
    }
  }
  '/api/v1/orders/{id}/actions/cancel': {
    post: {
      parameters: { path: { id: string } }
      requestBody: { content: { 'application/json': CancelOrderRequestBody } }
      responses: {
        200: { content: { 'application/json': OrderDto } }
      }
    }
  }
}

const client = createApiClient<OrdersPaths>()

export async function fetchOrder(orderPublicId: string): Promise<OrderDto> {
  const { data } = await client.GET('/api/v1/orders/{id}', {
    params: { path: { id: orderPublicId } },
  })
  return data as OrderDto
}

export async function cancelOrder(
  orderPublicId: string,
  body: CancelOrderRequestBody,
): Promise<OrderDto> {
  const { data } = await client.POST('/api/v1/orders/{id}/actions/cancel', {
    params: { path: { id: orderPublicId } },
    body,
  })
  return data as OrderDto
}

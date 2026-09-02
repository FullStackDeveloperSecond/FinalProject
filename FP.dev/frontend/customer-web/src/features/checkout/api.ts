import type { components } from '@doselect/web-shared/api'
import { apiClient } from '../../api/client'

export type AcceptedPolicyVersions = components['schemas']['AcceptedPolicyVersions']
export type CreateOrderRequest = components['schemas']['CreateOrderRequest']
export type OrderDto = components['schemas']['OrderDto']
export type PaymentMethod = components['schemas']['PaymentMethod']

function guestHeaders(guestCartKey?: string): HeadersInit | undefined {
  return guestCartKey ? { 'X-DoSelect-Guest-Cart-Key': guestCartKey } : undefined
}

export async function getCheckoutPolicyVersions(): Promise<AcceptedPolicyVersions> {
  const { data } = await apiClient.GET('/api/v1/checkout/policy-versions')
  return data!
}

/**
 * Checkout sends only identifiers, RowVersion and shopper input. Amounts, shipping fees,
 * inventory, coupon effects and snapshots are always recomputed by the atomic backend command.
 */
export async function createOrder(
  body: CreateOrderRequest,
  idempotencyKey: string,
  guestCartKey?: string,
): Promise<OrderDto> {
  const { data } = await apiClient.POST('/api/v1/orders', {
    body,
    params: { header: { 'Idempotency-Key': idempotencyKey } },
    headers: guestHeaders(guestCartKey),
  })
  return data!
}

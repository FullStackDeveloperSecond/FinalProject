import type { components } from '@doselect/web-shared/api'
import { apiClient } from '../../api/client'
import { getOrCreateGuestCartKey } from '../cart/guestCartKey'

export type ShippingMethodOptionDto = components['schemas']['ShippingMethodOptionDto']
export type PaymentMethod = components['schemas']['PaymentMethod']
export type CheckoutInvoiceBuyerType = components['schemas']['CheckoutInvoiceBuyerType']
export type CreateOrderRequest = components['schemas']['CreateOrderRequest']
export type OrderDto = components['schemas']['OrderDto']

// Mirrors DoSelect.Application.Checkout.CheckoutPolicyOptions' configured defaults until a
// versioned policy-registry read endpoint exists (same non-goal as auth/api.ts's own
// CURRENT_TERMS_VERSION) — appsettings.json currently pins all three to 1.
export const CURRENT_CHECKOUT_POLICY_VERSIONS = { terms: 1, return: 1, privacy: 1 } as const

function guestHeaders(guestCartKey?: string): HeadersInit | undefined {
  return guestCartKey ? { 'X-DoSelect-Guest-Cart-Key': guestCartKey } : undefined
}

export async function fetchShippingOptions(): Promise<ShippingMethodOptionDto[]> {
  const { data } = await apiClient.GET('/api/v1/cart/shipping-options')
  return data!.methods
}

export async function createOrder(
  body: CreateOrderRequest,
  idempotencyKey: string,
  isMember: boolean,
): Promise<OrderDto> {
  const { data } = await apiClient.POST('/api/v1/orders', {
    params: { header: { 'Idempotency-Key': idempotencyKey } },
    body,
    headers: isMember ? undefined : guestHeaders(getOrCreateGuestCartKey()),
  })
  return data!
}

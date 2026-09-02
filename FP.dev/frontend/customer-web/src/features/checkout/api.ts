import type { components } from '@doselect/web-shared/api'
import { apiClient } from '../../api/client'

export type CheckoutPolicyVersions = components['schemas']['AcceptedPolicyVersions']
export type CreateOrderRequest = components['schemas']['CreateOrderRequest']
export type CheckoutBuyerInput = components['schemas']['CheckoutBuyerInput']
export type CheckoutAddressInput = components['schemas']['CheckoutAddressInput']
export type CheckoutShippingInput = components['schemas']['CheckoutShippingInput']
export type CheckoutInvoiceInput = components['schemas']['CheckoutInvoiceInput']
export type CheckoutInvoiceBuyerType = components['schemas']['CheckoutInvoiceBuyerType']
export type PaymentMethod = components['schemas']['PaymentMethod']

function guestHeaders(guestCartKey?: string): HeadersInit | undefined {
  return guestCartKey ? { 'X-DoSelect-Guest-Cart-Key': guestCartKey } : undefined
}

export async function fetchCheckoutPolicyVersions(): Promise<CheckoutPolicyVersions> {
  const { data } = await apiClient.GET('/api/v1/checkout/policy-versions')
  return data!
}

export async function submitCheckout(
  body: CreateOrderRequest,
  idempotencyKey: string,
  guestCartKey?: string,
) {
  const { data } = await apiClient.POST('/api/v1/orders', {
    params: { header: { 'Idempotency-Key': idempotencyKey } },
    headers: guestHeaders(guestCartKey),
    body,
  })
  return data!
}

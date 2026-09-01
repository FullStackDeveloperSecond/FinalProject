import type { components } from '@doselect/web-shared/api'
import { apiClient } from '../../api/client'

export type PaymentMethod = components['schemas']['PaymentMethod']
export type PaymentAttemptStatus = components['schemas']['PaymentAttemptStatus']
export type PaymentAttemptDto = components['schemas']['PaymentAttemptDto']
export type SimulatedPaymentOutcome = components['schemas']['SimulatedPaymentOutcome']
export type SimulatedInvoiceDto = components['schemas']['SimulatedInvoiceDto']

export interface CreatePaymentAttemptBody {
  method: PaymentMethod
  orderRowVersion: string
}

export interface CompleteSimulatedPaymentBody {
  outcome: SimulatedPaymentOutcome
  simulationKey: string
}

export async function createPaymentAttempt(
  orderPublicId: string,
  body: CreatePaymentAttemptBody,
  idempotencyKey: string,
): Promise<PaymentAttemptDto> {
  const { data } = await apiClient.POST('/api/v1/orders/{id}/payment-attempts', {
    params: {
      path: { id: orderPublicId },
      header: { 'Idempotency-Key': idempotencyKey },
    },
    body,
  })
  return data!
}

export async function completeSimulatedPayment(
  attemptPublicId: string,
  body: CompleteSimulatedPaymentBody,
): Promise<PaymentAttemptDto> {
  const { data } = await apiClient.POST('/api/v1/simulated-payments/{attemptId}/actions/complete', {
    params: { path: { attemptId: attemptPublicId } },
    body,
  })
  return data!
}

export async function fetchOrderInvoice(orderPublicId: string): Promise<SimulatedInvoiceDto> {
  const { data } = await apiClient.GET('/api/v1/orders/{orderId}/invoice', {
    params: { path: { orderId: orderPublicId } },
  })
  return data!
}

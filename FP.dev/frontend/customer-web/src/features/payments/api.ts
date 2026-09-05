import type { components } from '@doselect/web-shared/api'
import { isApiError } from '@doselect/web-shared/api'
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

/**
 * 這張訂單最新的一筆付款嘗試，供重新整理後恢復畫面。
 *
 * 回傳所有終態（失敗、逾期、取消、已付款）—— 付款失敗後重新整理仍要看得到
 * 剛才發生什麼事，而不是回到一張空的建立表單。
 *
 * 沒有付款嘗試時後端回 404，這裡轉成 undefined：那不是錯誤，是「還沒建立」，
 * 頁面照既有流程顯示建立表單。
 */
export async function fetchLatestPaymentAttempt(
  orderPublicId: string,
): Promise<PaymentAttemptDto | undefined> {
  try {
    const { data } = await apiClient.GET(
      '/api/v1/orders/{id}/payment-attempts/latest',
      { params: { path: { id: orderPublicId } } },
    )
    return data!
  }
  catch (error) {
    if (isApiError(error) && error.status === 404) {
      return undefined
    }
    throw error
  }
}

export async function fetchOrderInvoice(orderPublicId: string): Promise<SimulatedInvoiceDto> {
  const { data } = await apiClient.GET('/api/v1/orders/{orderId}/invoice', {
    params: { path: { orderId: orderPublicId } },
  })
  return data!
}

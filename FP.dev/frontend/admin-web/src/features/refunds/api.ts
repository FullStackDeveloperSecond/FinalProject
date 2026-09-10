import { apiClient } from '../../api/client'
import type { ApproveRefundRequest, ExecuteRefundRequest, RefundDto, RefundStatus } from './types'

export interface RefundListParams {
  q?: string
  statuses?: RefundStatus[]
  fromUtc?: string
  toUtc?: string
  sort?: RefundSortOption
  pageNumber?: number
  pageSize?: number
}

export type RefundSortOption =
  | 'refundNumberAsc'
  | 'refundNumberDesc'
  | 'statusAsc'
  | 'statusDesc'
  | 'createdAtAsc'
  | 'createdAtDesc'

export async function listRefunds(params: RefundListParams) {
  const { data } = await apiClient.GET('/api/v1/admin/refunds', {
    params: {
      query: {
        Q: params.q || undefined,
        Statuses: params.statuses?.length ? params.statuses : undefined,
        FromUtc: params.fromUtc,
        ToUtc: params.toUtc,
        Sort: params.sort,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function getRefund(refundPublicId: string): Promise<RefundDto> {
  const { data } = await apiClient.GET('/api/v1/admin/refunds/{refundPublicId}', {
    params: { path: { refundPublicId } },
  })
  return data!
}

export async function executeRefund(
  refundPublicId: string,
  request: ExecuteRefundRequest,
  idempotencyKey: string,
): Promise<RefundDto> {
  const { data } = await apiClient.POST(
    '/api/v1/admin/refunds/{refundPublicId}/actions/execute',
    {
      params: {
        path: { refundPublicId },
        header: { 'Idempotency-Key': idempotencyKey },
      },
      body: request,
    },
  )
  return data!
}

export async function approveRefund(
  refundPublicId: string,
  request: ApproveRefundRequest,
  idempotencyKey: string,
): Promise<RefundDto> {
  const { data } = await apiClient.POST(
    '/api/v1/admin/refunds/{refundPublicId}/actions/approve',
    {
      params: {
        path: { refundPublicId },
        header: { 'Idempotency-Key': idempotencyKey },
      },
      body: request,
    },
  )
  return data!
}

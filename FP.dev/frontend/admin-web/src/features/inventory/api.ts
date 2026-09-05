import { apiClient } from '../../api/client'
import type {
  CursorPageOfInventoryReservationDto,
  PageResultOfInventoryBalanceDto,
  PageResultOfInventoryMovementDto,
  PageResultOfInventoryReconciliationCaseDto,
  ReconciliationCaseResolutionRequest,
  ReleaseReservationRequest,
} from './types'

export interface InventoryBalanceListParams {
  q?: string
  stockState?: string
  categoryCode?: string
  pageNumber?: number
  pageSize?: number
}

export interface InventoryMovementListParams {
  skuPublicId?: string
  movementTypes?: string[]
  from?: string
  to?: string
  pageNumber?: number
  pageSize?: number
}

export interface InventoryReservationListParams {
  cursor?: string
  status?: string
  pageSize?: number
}

export interface InventoryReconciliationCaseListParams {
  status?: string
  pageNumber?: number
  pageSize?: number
}

/** 對帳案件的兩個結案動作，對應 `…/reconciliation-cases/{id}/actions/{action}`。 */
export type ReconciliationCaseCloseAction = 'dismiss' | 'resolve'

/**
 * The shared API client's middleware throws an `ApiError` for any non-OK response (see
 * `frontend/shared/src/api/client.ts`), so `data` is always populated on the success path
 * handled here — callers do not need to additionally check openapi-fetch's own `error` field.
 */
export async function listBalances(params: InventoryBalanceListParams): Promise<PageResultOfInventoryBalanceDto> {
  const { data } = await apiClient.GET('/api/v1/admin/inventory/balances', {
    params: {
      query: {
        Q: params.q || undefined,
        StockState: params.stockState || undefined,
        CategoryCode: params.categoryCode || undefined,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function listMovements(params: InventoryMovementListParams): Promise<PageResultOfInventoryMovementDto> {
  const { data } = await apiClient.GET('/api/v1/admin/inventory/movements', {
    params: {
      query: {
        SkuPublicId: params.skuPublicId || undefined,
        MovementTypes: params.movementTypes,
        From: params.from || undefined,
        To: params.to || undefined,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function listReservations(
  params: InventoryReservationListParams,
): Promise<CursorPageOfInventoryReservationDto> {
  const { data } = await apiClient.GET('/api/v1/admin/inventory/reservations', {
    params: {
      query: {
        Cursor: params.cursor || undefined,
        Status: params.status || undefined,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function releaseReservation(publicId: string, request: ReleaseReservationRequest): Promise<void> {
  await apiClient.POST('/api/v1/admin/inventory/reservations/{id}/actions/release', {
    params: { path: { id: publicId } },
    body: request,
  })
}

export async function listReconciliationCases(
  params: InventoryReconciliationCaseListParams,
): Promise<PageResultOfInventoryReconciliationCaseDto> {
  const { data } = await apiClient.GET('/api/v1/admin/inventory/reconciliation-cases', {
    params: {
      query: {
        Status: params.status || undefined,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function acknowledgeReconciliationCase(publicId: string, rowVersion: string): Promise<void> {
  await apiClient.POST('/api/v1/admin/inventory/reconciliation-cases/{id}/actions/acknowledge', {
    params: { path: { id: publicId } },
    body: { rowVersion },
  })
}

/**
 * dismiss／resolve 共用同一個 Request（組長對帳裁定 C1）；路由分開，所以 openapi-fetch 的路徑
 * 型別要各寫一次，不能用字串拼出來。
 */
export async function closeReconciliationCase(
  publicId: string,
  action: ReconciliationCaseCloseAction,
  request: ReconciliationCaseResolutionRequest,
): Promise<void> {
  if (action === 'dismiss') {
    await apiClient.POST('/api/v1/admin/inventory/reconciliation-cases/{id}/actions/dismiss', {
      params: { path: { id: publicId } },
      body: request,
    })
    return
  }
  await apiClient.POST('/api/v1/admin/inventory/reconciliation-cases/{id}/actions/resolve', {
    params: { path: { id: publicId } },
    body: request,
  })
}

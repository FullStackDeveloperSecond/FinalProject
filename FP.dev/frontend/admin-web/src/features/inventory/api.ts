import { apiClient } from '../../api/client'
import type {
  CursorPageOfInventoryReservationDto,
  PageResultOfInventoryBalanceDto,
  PageResultOfInventoryMovementDto,
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

/**
 * Hand-typed Inventory Admin contract, mirroring `InventoryContracts.cs` and
 * `AdminInventoryController.cs` on `feature/inventory-reservation-api` (not merged to `dev`
 * yet, so there is no live API to export `frontend/shared`'s generated OpenAPI schema from).
 * This is a stand-in for that generated schema — see `features/categories/types.ts` on
 * `feature/catalog-frontend` for what this should become once inventory-reservation-api merges
 * to `dev` and `api:generate` is run for real (mirrors the same pattern already used by
 * `features/compatibilityRules/types.ts` on `feature/build-compat-frontend`).
 */

export interface InventorySkuSummaryDto {
  publicId: string
  skuCode: string
  nameZhTw: string
}

export interface InventoryActorSummaryDto {
  publicId: string | null
  email: string | null
}

export interface InventoryOrderSummaryDto {
  publicId: string
  orderNumber: string
}

export interface InventoryBalanceDto {
  skuPublicId: string
  skuCode: string
  skuNameZhTw: string
  onHand: number
  reserved: number
  available: number
  lowStockThreshold: number
  rowVersion: string
}

export interface InventoryMovementDto {
  publicId: string
  sku: InventorySkuSummaryDto
  movementType: string
  onHandDelta: number
  reservedDelta: number
  beforeOnHand: number
  afterOnHand: number
  beforeReserved: number
  afterReserved: number
  reasonCode: string
  actor: InventoryActorSummaryDto | null
  referenceType: string
  referencePublicId: string | null
  occurredAtUtc: string
}

export interface InventoryReservationDto {
  publicId: string
  order: InventoryOrderSummaryDto
  sku: InventorySkuSummaryDto
  quantity: number
  status: string
  expiresAtUtc: string | null
  createdAtUtc: string
  availableActions: string[]
  rowVersion: string
}

export interface ReleaseReservationRequest {
  reasonCode: string
  note: string
  rowVersion: string
}

export interface PageResultOf<T> {
  items: T[]
  pageNumber: number
  pageSize: number
  totalCount: number
  totalPages?: number
}

export interface CursorPageOf<T> {
  items: T[]
  nextCursor: string | null
  hasMore: boolean
}

interface JsonResponse<T> {
  content: {
    'application/json': T
  }
}

interface ProblemResponse {
  content: {
    'application/problem+json': { code: string }
  }
}

export interface InventoryApiPaths {
  '/api/v1/admin/inventory/balances': {
    get: {
      parameters: {
        query: {
          Q?: string
          StockState?: string
          CategoryCode?: string
          PageNumber?: number
          PageSize?: number
        }
      }
      responses: {
        200: JsonResponse<PageResultOf<InventoryBalanceDto>>
      }
    }
  }
  '/api/v1/admin/inventory/movements': {
    get: {
      parameters: {
        query: {
          SkuPublicId?: string
          MovementTypes?: string[]
          From?: string
          To?: string
          PageNumber?: number
          PageSize?: number
        }
      }
      responses: {
        200: JsonResponse<PageResultOf<InventoryMovementDto>>
      }
    }
  }
  '/api/v1/admin/inventory/reservations': {
    get: {
      parameters: {
        query: {
          Cursor?: string
          Status?: string
          PageSize?: number
        }
      }
      responses: {
        200: JsonResponse<CursorPageOf<InventoryReservationDto>>
      }
    }
  }
  '/api/v1/admin/inventory/reservations/{id}/actions/release': {
    post: {
      parameters: { path: { id: string } }
      requestBody: { content: { 'application/json': ReleaseReservationRequest } }
      responses: {
        204: { content?: never }
        400: ProblemResponse
        404: ProblemResponse
        409: ProblemResponse
      }
    }
  }
}

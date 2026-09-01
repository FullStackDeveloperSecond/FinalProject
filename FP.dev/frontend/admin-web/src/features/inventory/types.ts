import type { components } from '@doselect/web-shared/api'

/**
 * 組長 PR #37 review item 5: the hand-written OpenAPI stand-in this file used to carry is gone —
 * #36 merged into dev, the official contract now includes the five admin inventory endpoints, and
 * every DTO below re-exports the generated schema so the page can never drift from the API again.
 */
export type InventorySkuSummaryDto = components['schemas']['InventorySkuSummaryDto']
export type InventoryActorSummaryDto = components['schemas']['InventoryActorSummaryDto']
export type InventoryOrderSummaryDto = components['schemas']['InventoryOrderSummaryDto']
export type InventoryBalanceDto = components['schemas']['InventoryBalanceDto']
export type InventoryMovementDto = components['schemas']['InventoryMovementDto']
export type InventoryReservationDto = components['schemas']['InventoryReservationDto']
export type PageResultOfInventoryBalanceDto = components['schemas']['PageResultOfInventoryBalanceDto']
export type PageResultOfInventoryMovementDto = components['schemas']['PageResultOfInventoryMovementDto']
export type CursorPageOfInventoryReservationDto = components['schemas']['CursorPageOfInventoryReservationDto']

/**
 * The one deliberate exception to "everything comes from the generated schema": the manual-release
 * HTTP endpoint is withdrawn on the backend until a follow-up PR wires it to the central Audit Log
 * in the same transaction (組長 PR #36 round-3 ruling), so the official contract has no
 * release path or request schema to import yet. The release UI stays dormant behind
 * `reservation.availableActions.includes('release')`, which the backend keeps empty for the same
 * reason — these two local types exist only so that dormant path still compiles, and they must be
 * replaced by the generated ones in the PR that re-adds the endpoint.
 */
export interface ReleaseReservationRequest {
  reasonCode: string
  note: string
  rowVersion: string
}

export interface InventoryReleaseApiPaths {
  '/api/v1/admin/inventory/reservations/{id}/actions/release': {
    post: {
      parameters: { path: { id: string } }
      requestBody: { content: { 'application/json': ReleaseReservationRequest } }
      responses: { 204: { content?: never } }
    }
  }
}

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
 * 人工釋放（UC-ADM-INV-01）。這個端點在 PR #36 round 3 被撤回，這裡曾經留一份手寫的 request／
 * path 型別讓休眠的釋放 UI 能編譯；端點連同中央稽核補回契約之後，那份例外就不該再存在——
 * 跟其他 DTO 一樣直接用產生的 schema。`rowVersion` 是後端 byte[] 的 base64 字串。
 */
export type ReleaseReservationRequest = components['schemas']['ReleaseReservationRequest']

/**
 * 對帳案件（UC-ADM-INV-01 對帳；PR #107 補回 dismiss／resolve 端點後的產生契約）。
 * `rowVersion` 同樣是 byte[] 的 base64 字串；int32 欄位在產生器裡是 `number | string`，頁面用 Number() 收斂。
 */
export type InventoryReconciliationCaseDto = components['schemas']['InventoryReconciliationCaseDto']
export type PageResultOfInventoryReconciliationCaseDto = components['schemas']['PageResultOfInventoryReconciliationCaseDto']
export type AcknowledgeReconciliationCaseRequest = components['schemas']['AcknowledgeReconciliationCaseRequest']
export type ReconciliationCaseResolutionRequest = components['schemas']['ReconciliationCaseResolutionRequest']

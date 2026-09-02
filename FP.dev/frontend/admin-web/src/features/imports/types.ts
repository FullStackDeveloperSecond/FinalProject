import type { components } from '@doselect/web-shared/api'

export type ProductImportBatchDto = components['schemas']['ProductImportBatchDto']
export type InventoryImportBatchDto = components['schemas']['InventoryImportBatchDto']
export type ImportRowDto = components['schemas']['ImportRowDto']
export type ImportRowsPage = components['schemas']['CursorPageOfImportRowDto']

/**
 * 組長 PR #89 item 2：庫存匯入的預覽列是明確型別，帶 Preview 當時算出的 Before／Delta／After 與
 * 原因碼、說明。商品匯入仍用通用的 ImportRowDto；兩者只在「rows 端點回什麼」不同。
 */
export type InventoryImportRowDto = components['schemas']['InventoryImportRowDto']
export type InventoryImportRowsPage = components['schemas']['CursorPageOfInventoryImportRowDto']

export type AnyImportRowsPage = ImportRowsPage | InventoryImportRowsPage
export type AnyImportRow = ImportRowDto | InventoryImportRowDto

export function isInventoryImportRow(row: AnyImportRow): row is InventoryImportRowDto {
  return 'skuCode' in row
}

export function isProductImportRow(row: AnyImportRow): row is ImportRowDto {
  return !('skuCode' in row)
}

/**
 * A-07 與 A-13 兩頁畫的是同一件事：一個批次、它的統計、它的預覽列。兩支 API 的 Batch DTO 欄位
 * 相同（型別上是兩個具名 Schema，因為可用的動作與 Policy 不同），所以共用元件綁這個聯集型別，
 * 不必為了共用而把兩邊的契約硬併成一個。
 */
export type ImportBatchDto = ProductImportBatchDto | InventoryImportBatchDto

/** 預覽列的動作，決定列要標成什麼。 */
export type ImportRowAction = 'Insert' | 'Update' | 'NoChange' | 'Error'

/** 批次狀態；只有 Ready 可以確認。 */
export type ImportBatchStatus =
  | 'Uploaded'
  | 'Validating'
  | 'Ready'
  | 'Invalid'
  | 'Committing'
  | 'Committed'
  | 'Failed'
  | 'Expired'

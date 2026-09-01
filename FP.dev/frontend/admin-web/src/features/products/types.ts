import type { components } from '@doselect/web-shared/api'

export type AdminProductSummaryDto = components['schemas']['AdminProductSummaryDto']
export type AdminProductDetailDto = components['schemas']['AdminProductDetailDto']
export type CreateProductRequest = components['schemas']['CreateProductRequest']
export type UpdateProductRequest = components['schemas']['UpdateProductRequest']
export type BulkProductActionRequest = components['schemas']['BulkProductActionRequest']
export type BulkProductActionResultDto = components['schemas']['BulkProductActionResultDto']
export type BulkPriceAdjustment = components['schemas']['BulkPriceAdjustment']

/** UC-ADM-PROD-02 白名單，與後端 BulkProductActions 一致。 */
export type BulkProductAction = 'publish' | 'unpublish' | 'adjust-price'

export type BulkPriceAdjustmentMode = 'percentage' | 'amount'

export type AdminProductExportFormat = 'csv' | 'xlsx'

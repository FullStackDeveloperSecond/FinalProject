import type { components } from '@doselect/web-shared/api'

export type AdminProductSummaryDto = components['schemas']['AdminProductSummaryDto']
export type AdminProductDetailDto = components['schemas']['AdminProductDetailDto']
export type CreateProductRequest = components['schemas']['CreateProductRequest']
export type UpdateProductRequest = components['schemas']['UpdateProductRequest']
export type BulkProductActionRequest = components['schemas']['BulkProductActionRequest']
export type BulkProductActionResultDto = components['schemas']['BulkProductActionResultDto']
export type BulkPriceAdjustment = components['schemas']['BulkPriceAdjustment']

// M-03 商品圖片後台（A-06 圖片區塊）。全部來自產生的契約。
export type AdminProductImageDto = components['schemas']['AdminProductImageDto']
export type AdminProductImageVariantDto = components['schemas']['AdminProductImageVariantDto']
export type UpdateProductImageRequest = components['schemas']['UpdateProductImageRequest']
export type ProductImageActionRequest = components['schemas']['ProductImageActionRequest']

/** 上傳是 multipart：檔案加四個可省略的文字欄位（檔案與圖片儲存設計.md）。 */
export interface ProductImageUploadInput {
  file: File
  altText?: string
  sourceUrl?: string
  licenseName?: string
  licenseUrl?: string
}

/** UC-ADM-PROD-02 白名單，與後端 BulkProductActions 一致。 */
export type BulkProductAction = 'publish' | 'unpublish' | 'adjust-price'

export type BulkPriceAdjustmentMode = 'percentage' | 'amount'

export type AdminProductExportFormat = 'csv' | 'xlsx'

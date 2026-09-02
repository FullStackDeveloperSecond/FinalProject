import { apiClient } from '../../api/client'
import type {
  ImportRowsPage,
  InventoryImportBatchDto,
  InventoryImportRowsPage,
  ProductImportBatchDto,
} from './types'

/**
 * 商品匯入的兩種上傳（匯入暫存與庫存調整設計.md：「上傳 XLSX，或三份 CSV」）。有 workbook 就走
 * 單一 XLSX，三個 CSV 欄位不送；後端對「兩邊都給」回 validation_failed。
 */
export interface ProductImportFiles {
  products?: File
  skus?: File
  specifications?: File
  workbook?: File
}

export interface ImportRowsParams {
  errorsOnly?: boolean
  cursor?: string
  pageSize?: number
}

/**
 * A-07 的第一個動作。模板是一個含三個 CSV 的 ZIP；後端的標題列直接取自上傳端的驗證常數，所以
 * 下載到的東西一定通得過自己的驗證。
 */
export async function downloadProductImportTemplate(): Promise<Blob> {
  const { data, error } = await apiClient.GET('/api/v1/admin/import-templates/products/current', {
    parseAs: 'blob',
  })
  if (error) throw error
  if (!(data instanceof Blob)) {
    throw new Error('The template response was not a file.')
  }
  return data
}

export async function previewProductImport(
  files: ProductImportFiles,
  templateVersion: number,
): Promise<ProductImportBatchDto> {
  const formData = new FormData()
  if (files.workbook) {
    formData.set('workbookFile', files.workbook)
  }
  else {
    formData.set('productsFile', files.products!)
    formData.set('skusFile', files.skus!)
    formData.set('specificationsFile', files.specifications!)
  }
  formData.set('templateVersion', String(templateVersion))

  const { data, error } = await apiClient.POST('/api/v1/admin/product-imports/preview', {
    // openapi-fetch 看到 FormData 就不做 JSON 序列化，讓瀏覽器自己帶 multipart boundary；
    // 產生出來的型別沒有描述這件事，所以這裡照它宣告的形狀轉型（與退貨附件上傳同一個做法）。
    body: formData as unknown as Record<string, never>,
  })
  if (error) throw error
  return data!
}

export async function getProductImportBatch(id: string): Promise<ProductImportBatchDto> {
  const { data, error } = await apiClient.GET('/api/v1/admin/product-imports/{id}', {
    params: { path: { id } },
  })
  if (error) throw error
  return data!
}

export async function getProductImportRows(
  id: string,
  params: ImportRowsParams,
): Promise<ImportRowsPage> {
  const { data, error } = await apiClient.GET('/api/v1/admin/product-imports/{id}/rows', {
    params: { path: { id }, query: normalizeRowsQuery(params) },
  })
  if (error) throw error
  return data!
}

export async function downloadProductImportErrors(id: string): Promise<Blob> {
  return await downloadErrors('/api/v1/admin/product-imports/{id}/errors', id)
}

export async function confirmProductImport(
  id: string,
  rowVersion: string,
): Promise<ProductImportBatchDto> {
  const { data, error } = await apiClient.POST('/api/v1/admin/product-imports/{id}/actions/confirm', {
    params: { path: { id } },
    body: { rowVersion },
  })
  if (error) throw error
  return data!
}

// ---------------------------------------------------------------------------
// A-13 庫存匯入。路由與形狀刻意與商品匯入對齊，但是不同的 Policy。
// ---------------------------------------------------------------------------

export async function previewInventoryImport(
  adjustmentsFile: File,
  templateVersion: number,
): Promise<InventoryImportBatchDto> {
  const formData = new FormData()
  formData.set('adjustmentsFile', adjustmentsFile)
  formData.set('templateVersion', String(templateVersion))

  const { data, error } = await apiClient.POST('/api/v1/admin/inventory-imports/preview', {
    body: formData as unknown as Record<string, never>,
  })
  if (error) throw error
  return data!
}

export async function getInventoryImportBatch(id: string): Promise<InventoryImportBatchDto> {
  const { data, error } = await apiClient.GET('/api/v1/admin/inventory-imports/{id}', {
    params: { path: { id } },
  })
  if (error) throw error
  return data!
}

export async function getInventoryImportRows(
  id: string,
  params: ImportRowsParams,
): Promise<InventoryImportRowsPage> {
  const { data, error } = await apiClient.GET('/api/v1/admin/inventory-imports/{id}/rows', {
    params: { path: { id }, query: normalizeRowsQuery(params) },
  })
  if (error) throw error
  return data!
}

export async function downloadInventoryImportErrors(id: string): Promise<Blob> {
  return await downloadErrors('/api/v1/admin/inventory-imports/{id}/errors', id)
}

export async function confirmInventoryImport(
  id: string,
  rowVersion: string,
): Promise<InventoryImportBatchDto> {
  const { data, error } = await apiClient.POST('/api/v1/admin/inventory-imports/{id}/actions/confirm', {
    params: { path: { id } },
    body: { rowVersion },
  })
  if (error) throw error
  return data!
}

function normalizeRowsQuery(params: ImportRowsParams) {
  return {
    errorsOnly: params.errorsOnly || undefined,
    cursor: params.cursor || undefined,
    pageSize: params.pageSize,
  }
}

async function downloadErrors(
  path: '/api/v1/admin/product-imports/{id}/errors' | '/api/v1/admin/inventory-imports/{id}/errors',
  id: string,
): Promise<Blob> {
  const { data, error } = await apiClient.GET(path, {
    params: { path: { id } },
    parseAs: 'blob',
  })
  if (error) throw error
  if (!(data instanceof Blob)) {
    throw new Error('The error report response was not a file.')
  }
  return data
}

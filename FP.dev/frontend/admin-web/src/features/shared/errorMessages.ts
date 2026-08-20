import { type ApiError } from '@doselect/web-shared/api'

const codeMessages: Record<string, string> = {
  brand_code_duplicate: '代碼已存在',
  category_code_duplicate: '代碼已存在',
  tag_code_duplicate: '代碼已存在',
  product_code_duplicate: '代碼已存在',
  sku_code_duplicate: '代碼已存在',
  sku_code_immutable: 'SKU 代碼建立後不可修改',
  concurrency_conflict: '此資料已被其他人修改，請重新整理後再試',
  sku_delete_referenced: '此 SKU 已被使用，無法刪除',
  category_parent_invalid: '上層分類不正確',
  reference_not_found: '關聯的資料不存在',
  specification_invalid: '規格值不正確',
  validation_failed: '資料驗證失敗',
}

export function describeApiError(error: ApiError): string {
  return codeMessages[error.code] ?? error.message
}

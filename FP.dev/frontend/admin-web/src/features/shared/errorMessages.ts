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
  // 組長 PR #24 round 5 review, item 5: registered in API錯誤碼目錄.md / API Endpoint目錄.md.
  sku_default_required: '無法直接取消或刪除目前的預設 SKU，請先將其他 SKU 設為預設',
  sku_missing_required_specification: '缺少分類規定的必要規格值，無法上架此 SKU',
  category_parent_invalid: '上層分類不正確',
  reference_not_found: '關聯的資料不存在',
  specification_invalid: '規格值不正確',
  validation_failed: '資料驗證失敗',
  compatibility_threshold_out_of_range: '門檻數值超出允許範圍',
  // 這張表是全站共用的（相容性規則、商品、SKU、庫存都經由 describeApiError 取字），所以
  // resource_not_found 不能寫成任一功能專屬的字——ProductEditPage 與庫存頁同樣會拿到它。
  resource_not_found: '找不到此資料',
  coupon_code_duplicate: '優惠碼已存在',
  // 管理員的 activate 只接受 Draft 或符合條件的 Paused：Scheduled 由排程喚醒、
  // Exhausted 由名額返還，兩者都是系統事件（狀態機設計「優惠券狀態」）。
  coupon_state_conflict: '優惠券目前狀態不允許這個操作',
  refund_state_conflict: '退款目前狀態已變更，請重新整理後再試',
  refund_amount_exceeded: '退款金額超過目前可退款餘額',
  refund_snapshot_unavailable: '缺少可信交易快照，不能執行退款',
  idempotency_payload_conflict: '同一重試識別已用於不同內容，請重新整理後再試',
  inventory_reservation_not_active: '此保留已非 Active 狀態，無法釋放',
  inventory_reservation_already_processed: '此保留已被消耗、釋放或逾時',
}

export function describeApiError(error: ApiError): string {
  return codeMessages[error.code] ?? error.message
}

/**
 * Catalog 分類代碼 → 中文顯示名。
 *
 * 為什麼前端要留一份對照表：
 * `/api/v1/catalog/filter-options` 的 `categories` 是「還可以再往下鑽的分類」——
 * 沒帶 Category 時回頂層清單，帶了 Category 時回**該分類的子分類**
 * （EfCatalogFilterOptionsService.GetCategoriesAsync）。所以深連結進
 * `/products?category=CPU` 時，回應裡不會有 CPU 自己，也就拿不到它的名稱。
 *
 * 代碼本身來自 `CompatibilityCatalogContract.Categories`（後端 Domain 層），
 * 是穩定的契約值；名稱只是顯示用，對不上時一律退回代碼，不會擋住任何功能。
 *
 * 維護方式：這是**顯示層的補充**，不是分類清單的來源。後端新增分類時，
 * 商品頁的下拉仍然會從 API 拿到它（只是顯示代碼），補進這裡即可顯示中文名。
 * `categoryLabels.spec.ts` 會檢查這裡的鍵沒有拼錯、也沒有超出契約的代碼。
 */

/** `CompatibilityCatalogContract.Categories.All`（後端 Domain 契約）。 */
export const CATALOG_CATEGORY_CODES = [
  'CPU',
  'MOTHERBOARD',
  'MEMORY',
  'GPU',
  'STORAGE',
  'PSU',
  'CASE',
  'CPU_COOLER',
] as const

export type CatalogCategoryCode = typeof CATALOG_CATEGORY_CODES[number]

const CATEGORY_LABELS: Record<CatalogCategoryCode, string> = {
  CPU: '處理器',
  MOTHERBOARD: '主機板',
  MEMORY: '記憶體',
  GPU: '顯示卡',
  STORAGE: '儲存裝置',
  PSU: '電源供應器',
  CASE: '機殼',
  CPU_COOLER: '散熱器',
}

/**
 * 取得分類的中文顯示名；未知代碼退回代碼本身。
 *
 * 呼叫端優先用 API 回傳的 `CategoryFilterOption.name`（那是後端的權威名稱，
 * 也已經套用語系），只有在回應裡沒有該分類時才會走到這裡。
 */
export function categoryLabel(code: string): string {
  return CATEGORY_LABELS[code as CatalogCategoryCode] ?? code
}

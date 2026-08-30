import { apiClient } from '../../api/client'
import type { CategoryOption, ProductOption } from './types'

/**
 * 優惠券適用範圍挑選器的目錄參考資料。
 *
 * ## 這一版換掉了什麼
 *
 * 先前打的是店面的公開端點（`/api/v1/catalog/filter-options` 與 `/api/v1/products`），
 * 理由只有一個：既有的 `features/categories`／`features/products` 走 `CatalogManager`
 * 政策，與 `Coupon.Manage` 的交集只有 SuperAdmin。
 *
 * 但那個選擇沒有算過代價：分類樹每個節點各打一次（上限 100 次，而那個端點每次還會
 * 順便算品牌、價格區間與規格篩選），已選商品又逐筆查明細，兩個 picker 各最多 50 次 ——
 * 一次普通編輯就是上百次 HTTP 與 SQL（alex 2026-08-29 PR #64 P2#3）。
 *
 * 現在改用受 `Coupon.Manage` 保護、用途限定的批次端點：分類一次取回、商品關鍵字
 * 分頁搜尋、一組 PublicId 批次解析。
 */

/** 商品搜尋一次最多回幾筆；與後端的上限一致。 */
export const maximumSearchPageSize = 50

/** 一次可以批次解析的商品數；與優惠券規則的 200 筆上限一致。 */
export const maximumBatchSize = 200

/**
 * 一次取回整棵分類樹。
 *
 * 停用的分類也會回並帶 `isActive`：舊券可能已經綁在上面，查不到會讓那筆設定
 * 在介面上靜默消失。
 */
export async function loadCategoryOptions(): Promise<CategoryOption[]> {
  const { data } = await apiClient.GET('/api/v1/admin/coupons/catalog-options/categories')
  return data!
}

export interface ProductSearchParams {
  q?: string
  pageNumber?: number
  pageSize?: number
}

export interface ProductSearchResult {
  items: ProductOption[]
  hasMore: boolean
}

/**
 * 關鍵字搜尋可新增的商品。
 *
 * 只回可新增的狀態（草稿／已上架／已下架）—— 搜尋結果是「可以加進來的東西」，
 * 已停售的商品不會出現在這裡，但仍然解析得到（見 `resolveProductOptions`）。
 */
export async function searchProductOptions(
  params: ProductSearchParams,
): Promise<ProductSearchResult> {
  const { data } = await apiClient.GET('/api/v1/admin/coupons/catalog-options/products', {
    params: {
      query: {
        Q: params.q || undefined,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })

  return { items: data!.items, hasMore: data!.hasMore }
}

/**
 * 一次解析一組商品 `publicId`，用於載入既有的適用／排除範圍。
 *
 * <b>一次往返</b>，不論幾筆 —— 先前是逐筆查商品明細。已停售的商品也會回來，
 * 帶 `isSelectable: false`：已經寫在券上的參考不能因為挑選器查不到就消失。
 */
export async function resolveProductOptions(
  publicIds: readonly string[],
): Promise<Record<string, ProductOption>> {
  const wanted = [...new Set(publicIds)].slice(0, maximumBatchSize)
  if (wanted.length === 0) {
    return {}
  }

  const { data } = await apiClient.POST(
    '/api/v1/admin/coupons/catalog-options/products/resolve',
    { body: { publicIds: wanted } },
  )

  return Object.fromEntries((data ?? []).map(option => [option.publicId, option]))
}

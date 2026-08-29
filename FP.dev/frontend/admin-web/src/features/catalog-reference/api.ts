import { apiClient } from '../../api/client'
import type { CategoryOption, ProductOption } from './types'

/**
 * 挑選器需要的分類與商品「參考資料」。
 *
 * ## 為什麼不用既有的 `features/categories`／`features/products`
 *
 * 那兩個模組打的是 `/api/v1/admin/categories` 與 `/api/v1/admin/products`，
 * 掛的政策是 `CatalogManager`（CatalogManager、SuperAdmin）。優惠券頁面掛的是
 * `Coupon.Manage`（FinanceManager、MarketingAnalyst、SuperAdmin）——
 * **兩者的交集只有 SuperAdmin**。
 *
 * 三分之二的合法優惠券管理員一打開挑選器就會拿到 403，而 admin-web 的
 * `handleGlobalApiError` 對 403 是 `router.push('/forbidden')`：使用者不是看到
 * 「挑選器載入失敗」，而是整個人被踢出優惠券頁面，連填到一半的表單都沒了。
 *
 * 所以這裡改用兩個沒有 `[Authorize]` 的公開端點，三個角色都讀得到。代價寫在
 * 各個函式的註解裡（分類要逐層取、商品只看得到已上架的）。
 *
 * 把 `CatalogManager` 政策放寬、或替目錄參考資料另立一個唯讀政策，都是安全設定
 * 的變更，不由這支 PR 決定。
 */

/**
 * 走訪分類樹的請求數上限。
 *
 * `/api/v1/catalog/filter-options` 一次只回一層（無參數回頂層、帶 `Category`
 * 回該分類的子分類），所以要攤平整棵樹得對每個節點各問一次。
 */
const maximumCategoryRequests = 100

/**
 * 一次替多少個已存的商品 `publicId` 解析名稱。
 *
 * 公開 API 沒有「用一組 id 批次查商品」的端點，只能逐筆 `GET /products/{id}`。
 * 範圍清單最多可到 200 筆，全部展開會是 200 個請求。
 */
const maximumResolvedProductLabels = 50

/**
 * 分類樹沒走完就撞到請求上限。
 *
 * 跟 `fetchAllPages` 同樣的理由：**不完整的集合不能當成完整的用**。
 * 少掉的分類在挑選器裡看起來就等於「不存在」，管理員會以為那個分類沒建過；
 * 已存的選取項也會解析不到名稱而只剩一串 GUID。寧可讓載入失敗。
 */
export class CategoryTreeTruncatedError extends Error {
  constructor() {
    super(`loadCategoryOptions: category tree exceeds the ${maximumCategoryRequests}-request safety cap`)
    this.name = 'CategoryTreeTruncatedError'
  }
}

interface CategoryFrontier {
  code: string | undefined
  path: string
}

/**
 * 攤平整棵分類樹。
 *
 * 只會拿到 `IsActive` 的分類 —— 端點是給店面篩選用的。已停用的分類若仍被某張
 * 舊券引用，這裡解析不到，介面會退回顯示原始 `publicId`。
 */
export async function loadCategoryOptions(): Promise<CategoryOption[]> {
  const collected: CategoryOption[] = []
  // 以 code 去重。資料若出現環狀 parent，沒有這道檢查會一直展開到撞上限。
  const visited = new Set<string>()
  let frontier: CategoryFrontier[] = [{ code: undefined, path: '' }]
  let requests = 0

  while (frontier.length > 0) {
    if (requests + frontier.length > maximumCategoryRequests) {
      throw new CategoryTreeTruncatedError()
    }
    requests += frontier.length

    const levels = await Promise.all(frontier.map(node => loadChildren(node)))
    const next: CategoryFrontier[] = []

    for (const level of levels) {
      for (const option of level) {
        if (visited.has(option.code)) {
          continue
        }
        visited.add(option.code)
        collected.push(option)
        next.push({ code: option.code, path: option.path })
      }
    }

    frontier = next
  }

  return collected
}

async function loadChildren(node: CategoryFrontier): Promise<CategoryOption[]> {
  const { data } = await apiClient.GET('/api/v1/catalog/filter-options', {
    params: { query: { Category: node.code } },
  })

  return data!.categories.map(category => ({
    publicId: category.publicId,
    code: category.code,
    name: category.name,
    path: node.path === '' ? category.name : `${node.path} / ${category.name}`,
  }))
}

export interface ProductSearchParams {
  q?: string
  pageNumber?: number
  pageSize?: number
}

export interface ProductSearchResult {
  items: ProductOption[]
  totalPages: number
}

/**
 * 關鍵字搜尋商品。
 *
 * 走店面搜尋，所以**只看得到 `Published` 的商品**。還在草稿、準備上架的商品
 * 沒辦法先設好優惠券範圍；需要的話要等商品上架後再回來加。
 */
export async function searchProductOptions(params: ProductSearchParams): Promise<ProductSearchResult> {
  const { data } = await apiClient.GET('/api/v1/products', {
    params: {
      query: {
        Q: params.q || undefined,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })

  return {
    items: data!.items.map(item => ({
      publicId: item.productPublicId,
      code: item.productCode,
      name: item.name,
    })),
    totalPages: Number(data!.totalPages ?? 0),
  }
}

/**
 * 替已存的商品 `publicId` 解析出名稱，供編輯既有優惠券時顯示。
 *
 * 解析不到的（已下架、已刪除，或超過 `maximumResolvedProductLabels` 的部分）
 * 不會出現在回傳的對照表裡，呼叫端要退回顯示原始 `publicId`。
 * 這裡刻意不讓單一筆失敗炸掉整張表 —— 一件商品下架不該讓整張券打不開。
 */
export async function resolveProductOptions(
  publicIds: readonly string[],
): Promise<Record<string, ProductOption>> {
  const resolved: Record<string, ProductOption> = {}

  await Promise.all(publicIds.slice(0, maximumResolvedProductLabels).map(async (publicId) => {
    try {
      const { data } = await apiClient.GET('/api/v1/products/{id}', {
        params: { path: { id: publicId } },
      })
      resolved[publicId] = {
        publicId,
        code: data!.productCode,
        name: data!.name,
      }
    }
    catch {
      // 解析不到就留白，由呼叫端顯示原始 publicId。
    }
  }))

  return resolved
}

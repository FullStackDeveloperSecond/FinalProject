/**
 * 挑選器用的分類節點，已把 `/api/v1/catalog/filter-options` 的逐層結果攤平。
 *
 * `path` 是從根到自己的名稱串接。分類名稱在不同上層之下可能重複
 * （例如「配件」），只顯示 `name` 會讓管理員分不出選到哪一個。
 */
export interface CategoryOption {
  publicId: string
  code: string
  name: string
  path: string
}

/** 挑選器用的商品項目。 */
export interface ProductOption {
  publicId: string
  code: string
  name: string
}

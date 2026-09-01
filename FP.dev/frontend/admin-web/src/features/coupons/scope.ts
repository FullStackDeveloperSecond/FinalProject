import type { CouponScopeType } from './types'

/**
 * 與後端 `AdminCouponRules.MaximumScopeEntries` 同值。
 *
 * 這個常數在兩邊各寫一次是刻意的：前端沒有辦法從產生的 OpenAPI 型別讀到它，
 * 而讓使用者選到第 201 筆才在送出時收到 400，比在挑選當下就說明白差得多。
 */
export const maximumScopeEntries = 200

export interface CouponScopeSelection {
  scopeType: CouponScopeType
  categoryPublicIds: readonly string[]
  productPublicIds: readonly string[]
  excludedProductPublicIds: readonly string[]
}

/**
 * 回傳第一個違反範圍規則的中文訊息；全部通過時回 `null`。
 *
 * 逐條對應後端 `AdminCouponRules.RequireValidRule`：
 *
 * - `Restricted` 至少需要一個分類或商品 —— 沒有的話是一張永遠算不出折扣的券，
 *   而**排除清單不算**，後端的判斷只看包含集合。
 * - 三份清單各自最多 `MaximumScopeEntries` 筆。
 * - 同一件商品不得同時出現在適用與排除清單。規則另定「排除優先」，所以不會壞掉，
 *   但那是兩個相反的意圖，靜默讓排除勝出等於幫管理員選了一邊。
 *
 * 重複值不在這裡檢查：挑選器以 `publicId` 切換選取，本來就不可能選到兩次。
 *
 * 前端擋下來**不是安全邊界** —— 後端仍會以 400 `validation_failed` 拒絕。
 * 這裡只是不要讓管理員填完整張表單才收到一句英文錯誤。
 */
export function describeScopeProblem(selection: CouponScopeSelection): string | null {
  if (selection.scopeType === 'restricted'
    && selection.categoryPublicIds.length === 0
    && selection.productPublicIds.length === 0) {
    return '指定範圍至少要選擇一個分類或商品。'
  }

  const overflowing = ([
    ['適用分類', selection.categoryPublicIds],
    ['適用商品', selection.productPublicIds],
    ['排除商品', selection.excludedProductPublicIds],
  ] as const).find(([, publicIds]) => publicIds.length > maximumScopeEntries)

  if (overflowing) {
    return `${overflowing[0]}最多 ${maximumScopeEntries} 筆。`
  }

  if (selection.productPublicIds.some(publicId =>
    selection.excludedProductPublicIds.includes(publicId))) {
    return '同一件商品不能同時列為適用與排除。'
  }

  return null
}

/**
 * 組出要送給 API 的四個範圍欄位。
 *
 * `scopeType=all` 時**必須真的把已選的分類與商品丟掉**，不能只是把 UI 收起來：
 * 後端對「All 卻帶了包含範圍」直接回 400，而「先挑了商品、再改回全站」
 * 是很自然的操作順序。
 *
 * 排除清單不在此列 —— 後端只禁止 All 帶**包含**範圍，全站券搭配排除商品
 * （例如「全站九折，特定機種除外」）是允許且常見的設定。
 *
 * 空清單送 `null` 而不是 `[]`：兩者後端都接受（判斷式看的是 `Count > 0`），
 * 但 `null` 才是「沒有這一段設定」的意思。
 */
export function toScopeRequestFields(selection: CouponScopeSelection): {
  scopeType: CouponScopeType
  categoryPublicIds: string[] | null
  productPublicIds: string[] | null
  excludedProductPublicIds: string[] | null
} {
  const restricted = selection.scopeType === 'restricted'

  return {
    scopeType: selection.scopeType,
    categoryPublicIds: restricted ? nonEmpty(selection.categoryPublicIds) : null,
    productPublicIds: restricted ? nonEmpty(selection.productPublicIds) : null,
    excludedProductPublicIds: nonEmpty(selection.excludedProductPublicIds),
  }
}

function nonEmpty(publicIds: readonly string[]): string[] | null {
  return publicIds.length > 0 ? [...publicIds] : null
}

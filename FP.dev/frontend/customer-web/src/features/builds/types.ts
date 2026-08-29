import type { components } from '@doselect/web-shared/api'

export type BuildItemDto = components['schemas']['BuildItemDto']
export type BuildCompatibilitySummaryDto = components['schemas']['BuildCompatibilitySummaryDto']
export type CompatibilityFindingDto = components['schemas']['CompatibilityFindingDto']
export type BuildTotalsDto = components['schemas']['BuildTotalsDto']
export type BuildListDto = components['schemas']['BuildListDto']
export type BuildListSummaryDto = components['schemas']['BuildListSummaryDto']
export type BuildShareDto = components['schemas']['BuildShareDto']
export type SharedBuildDto = components['schemas']['SharedBuildDto']
export type BuildItemInput = components['schemas']['BuildItemInput']
export type CreateBuildListRequest = components['schemas']['CreateBuildListRequest']
export type UpdateBuildListRequest = components['schemas']['UpdateBuildListRequest']
export type AddBuildToCartRequest = components['schemas']['AddBuildToCartRequest']
export type CompatibilityCheckRequest = components['schemas']['CompatibilityCheckRequest']
export type CompatibilityCheckDto = components['schemas']['CompatibilityCheckDto']

/**
 * `severity`/`overall` are plain `string` in the generated schema (the backend serializes them
 * via HasConversion<string>() with no OpenAPI enum annotation) — these narrower unions exist for
 * exhaustive comparisons in this feature's own code, not because the wire type guarantees them.
 */
export type CompatibilitySeverity =
  | 'compatible' | 'warning' | 'blocked' | 'insufficientData' | 'ruleDisabled'
export type CompatibilityOverall = 'compatible' | 'warning' | 'blocked' | 'insufficientData'

export type BuildListPageResultDto = components['schemas']['PageResultOfBuildListSummaryDto']

/**
 * 組長 PR #35 review, item 1: mirrors the backend's own
 * `DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories`/`CompatibilityEvaluator.
 * SingletonCategories` — the 8 build-component categories a compatibility check actually
 * evaluates, and which of them accept only one SKU (CPU／主機板／顯卡／PSU／機殼／散熱器) versus
 * multiple (記憶體／儲存裝置). Catalog's own `Category.Code` for these 8 categories is the same
 * string as the compatibility-catalog code (confirmed against `MinimalDevelopmentDataSeeder`'s
 * seed data), so this doubles as the `Category` search-query value.
 */
export interface BuildCategorySlot {
  code: string
  label: string
  singleton: boolean
}

export const BUILD_CATEGORY_SLOTS: readonly BuildCategorySlot[] = [
  { code: 'CPU', label: 'CPU', singleton: true },
  { code: 'MOTHERBOARD', label: '主機板', singleton: true },
  { code: 'MEMORY', label: '記憶體', singleton: false },
  { code: 'GPU', label: '顯示卡', singleton: true },
  { code: 'STORAGE', label: '儲存裝置', singleton: false },
  { code: 'PSU', label: '電源供應器', singleton: true },
  { code: 'CASE', label: '機殼', singleton: true },
  { code: 'CPU_COOLER', label: '散熱器', singleton: true },
]

/**
 * 組長 PR #35 round-3 review, P1-2: mirrors
 * `EfCompatibilityCheckService.MergeAndValidateItems`'s own bounds exactly (1–20 raw items,
 * 1–8 per merged SKU) — the editor already keeps `items` deduplicated per SKU (see
 * BuildItemsEditor.vue's `selectForSlot`), so what's here *is* the merged form the backend would
 * compute, and this can validate it directly without re-implementing the grouping itself. Used to
 * gate every "儲存"／"加入購物車" action on both NewBuildPage.vue and BuildDetailPage.vue so an
 * invalid request is never even attempted, not just eventually rejected by the backend.
 */
export const MAX_BUILD_ITEM_COUNT = 20
export const MAX_BUILD_ITEM_QUANTITY = 8

export interface BuildItemsValidation {
  isValid: boolean
  errors: string[]
}

export function validateBuildItems(items: { skuPublicId: string, quantity: number }[]): BuildItemsValidation {
  const errors: string[] = []
  if (items.length === 0) {
    errors.push('請至少選擇一項元件。')
  } else if (items.length > MAX_BUILD_ITEM_COUNT) {
    errors.push(`組裝項目最多 ${MAX_BUILD_ITEM_COUNT} 項，目前有 ${items.length} 項。`)
  }
  if (items.some((item) => !Number.isInteger(item.quantity) || item.quantity < 1 || item.quantity > MAX_BUILD_ITEM_QUANTITY)) {
    errors.push(`每項數量須為 1–${MAX_BUILD_ITEM_QUANTITY} 之間的整數。`)
  }
  return { isValid: errors.length === 0, errors }
}

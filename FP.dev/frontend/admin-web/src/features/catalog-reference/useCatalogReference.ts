import { useQuery } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  loadCategoryOptions,
  resolveProductOptions,
  searchProductOptions,
  type ProductSearchParams,
} from './api'

/**
 * 整棵分類樹，一次取回。
 *
 * `staleTime` 放長：分類是參考資料，不會在管理員填一張表單的期間改變。
 */
export function useCategoryOptions(enabled: MaybeRefOrGetter<boolean> = true) {
  return useQuery({
    queryKey: ['catalog-reference', 'categories'] as const,
    queryFn: loadCategoryOptions,
    staleTime: 5 * 60 * 1000,
    enabled: computed(() => toValue(enabled)),
  })
}

export function useProductOptionSearch(
  params: MaybeRefOrGetter<ProductSearchParams>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  return useQuery({
    queryKey: computed(() => ['catalog-reference', 'products', 'search', toValue(params)] as const),
    queryFn: () => searchProductOptions(toValue(params)),
    placeholderData: previous => previous,
    enabled: computed(() => toValue(enabled)),
  })
}

/**
 * 已選商品的名稱對照表。
 *
 * queryKey 用排序後的 id：勾選順序不同不該算成另一份查詢，否則每次調整選取
 * 都會重打一輪。
 */
export function useProductOptionLabels(publicIds: MaybeRefOrGetter<readonly string[]>) {
  return useQuery({
    queryKey: computed(() =>
      ['catalog-reference', 'products', 'labels', [...toValue(publicIds)].sort()] as const),
    queryFn: () => resolveProductOptions(toValue(publicIds)),
    enabled: computed(() => toValue(publicIds).length > 0),
    staleTime: 5 * 60 * 1000,
  })
}

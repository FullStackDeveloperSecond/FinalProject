import { useQuery } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { getCatalogFilterOptions, getProductDetail, searchProducts, type ProductSearchParams } from './api'

export function useProductSearch(params: MaybeRefOrGetter<ProductSearchParams>) {
  return useQuery({
    queryKey: computed(() => ['products', 'search', toValue(params)] as const),
    queryFn: () => searchProducts(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

export function useProductDetail(productPublicId: MaybeRefOrGetter<string | undefined>) {
  return useQuery({
    queryKey: computed(() => ['products', 'detail', toValue(productPublicId)] as const),
    queryFn: () => getProductDetail(toValue(productPublicId) as string),
    enabled: computed(() => Boolean(toValue(productPublicId))),
  })
}

export function useCatalogFilterOptions(category: MaybeRefOrGetter<string | undefined>) {
  return useQuery({
    queryKey: computed(() => ['catalog', 'filter-options', toValue(category)] as const),
    queryFn: () => getCatalogFilterOptions(toValue(category)),
    staleTime: 60_000,
  })
}

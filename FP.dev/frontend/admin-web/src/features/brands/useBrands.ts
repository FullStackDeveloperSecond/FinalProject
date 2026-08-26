import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { createBrand, listBrands, updateBrand, type BrandListParams } from './api'
import type { CreateBrandRequest, UpdateBrandRequest } from './types'
import { fetchAllPages } from '../shared/fetchAllPages'

export function useBrandList(params: MaybeRefOrGetter<BrandListParams>) {
  return useQuery({
    queryKey: computed(() => ['brands', 'list', toValue(params)] as const),
    queryFn: () => listBrands(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

/**
 * PR #24 review round 2: for resolving an existing association's code to a publicId — needs
 * every brand, not a single over-sized page (rejected server-side above pageSize 100).
 * See `fetchAllPages`.
 */
export function useFullBrandList(params: MaybeRefOrGetter<Omit<BrandListParams, 'pageNumber' | 'pageSize'>> = {}) {
  return useQuery({
    queryKey: computed(() => ['brands', 'full-list', toValue(params)] as const),
    queryFn: async () => ({
      items: await fetchAllPages((pageNumber, pageSize) =>
        listBrands({ ...toValue(params), pageNumber, pageSize })),
    }),
  })
}

// PR #24 review round 3: invalidating only ['brands','list'] left ['brands','full-list'] (the
// parent/association picker used by ProductEditPage etc.) holding a stale set — a brand created
// or renamed here wouldn't show up (or would still show the old name) in those pickers until
// something else happened to invalidate it. Invalidate the whole 'brands' prefix so both match.
export function useCreateBrand() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateBrandRequest) => createBrand(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['brands'] }),
  })
}

export function useUpdateBrand() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateBrandRequest }) =>
      updateBrand(publicId, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['brands'] }),
  })
}

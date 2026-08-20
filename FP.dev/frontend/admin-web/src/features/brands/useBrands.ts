import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { createBrand, listBrands, updateBrand, type BrandListParams } from './api'
import type { CreateBrandRequest, UpdateBrandRequest } from './types'

export function useBrandList(params: MaybeRefOrGetter<BrandListParams>) {
  return useQuery({
    queryKey: computed(() => ['brands', 'list', toValue(params)] as const),
    queryFn: () => listBrands(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

export function useCreateBrand() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateBrandRequest) => createBrand(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['brands', 'list'] }),
  })
}

export function useUpdateBrand() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateBrandRequest }) =>
      updateBrand(publicId, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['brands', 'list'] }),
  })
}

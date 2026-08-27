import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { createProduct, getAdminProduct, listAdminProducts, updateProduct, type AdminProductListParams } from './api'
import type { CreateProductRequest, UpdateProductRequest } from './types'

export function useAdminProductList(params: MaybeRefOrGetter<AdminProductListParams>) {
  return useQuery({
    queryKey: computed(() => ['admin-products', 'list', toValue(params)] as const),
    queryFn: () => listAdminProducts(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

export function useAdminProductDetail(publicId: MaybeRefOrGetter<string | undefined>) {
  return useQuery({
    queryKey: computed(() => ['admin-products', 'detail', toValue(publicId)] as const),
    queryFn: () => getAdminProduct(toValue(publicId) as string),
    enabled: computed(() => Boolean(toValue(publicId))),
  })
}

export function useCreateProduct() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateProductRequest) => createProduct(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-products', 'list'] }),
  })
}

export function useUpdateProduct() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateProductRequest }) =>
      updateProduct(publicId, request),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['admin-products', 'list'] })
      queryClient.invalidateQueries({ queryKey: ['admin-products', 'detail', variables.publicId] })
    },
  })
}

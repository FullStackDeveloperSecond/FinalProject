import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { createSku, deleteSku, updateSku } from './api'
import type { CreateSkuRequest, UpdateSkuRequest } from './types'

function invalidateProduct(queryClient: ReturnType<typeof useQueryClient>, productPublicId: string) {
  queryClient.invalidateQueries({ queryKey: ['admin-products', 'detail', productPublicId] })
}

export function useCreateSku(productPublicId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateSkuRequest) => createSku(productPublicId, request),
    onSuccess: () => invalidateProduct(queryClient, productPublicId),
  })
}

export function useUpdateSku(productPublicId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ skuPublicId, request }: { skuPublicId: string, request: UpdateSkuRequest }) =>
      updateSku(skuPublicId, request),
    onSuccess: () => invalidateProduct(queryClient, productPublicId),
  })
}

export function useDeleteSku(productPublicId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ skuPublicId, rowVersion }: { skuPublicId: string, rowVersion: string }) =>
      deleteSku(skuPublicId, rowVersion),
    onSuccess: () => invalidateProduct(queryClient, productPublicId),
  })
}

import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { createSku, deleteSku, updateSku } from './api'
import type { CreateSkuRequest, UpdateSkuRequest } from './types'

function invalidateProduct(queryClient: ReturnType<typeof useQueryClient>, productPublicId: string) {
  queryClient.invalidateQueries({ queryKey: ['admin-products', 'detail', productPublicId] })
}

/**
 * PR #24 review round 8 (P2): round 7's fix (a `MaybeRefOrGetter<string>` re-resolved via
 * `toValue()` separately inside mutationFn *and* onSuccess) pinned the write target correctly at
 * call time, but onSuccess re-read the getter again at completion time — if the admin navigates
 * to a different product while the request is still in flight, that later read picks up the
 * *new* product's id and invalidates its cache entry instead of the one this mutation actually
 * wrote to. `productPublicId` is now part of the mutation's own variables, supplied by the caller
 * at `.mutate()` time and never re-resolved afterward — mutationFn and onSuccess both receive the
 * exact same, already-frozen id via `variables`, regardless of what the page has navigated to by
 * the time the request settles.
 */
export function useCreateSku() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ productPublicId, request }: { productPublicId: string, request: CreateSkuRequest }) =>
      createSku(productPublicId, request),
    onSuccess: (_data, variables) => invalidateProduct(queryClient, variables.productPublicId),
  })
}

export function useUpdateSku() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ skuPublicId, request }: { productPublicId: string, skuPublicId: string, request: UpdateSkuRequest }) =>
      updateSku(skuPublicId, request),
    onSuccess: (_data, variables) => invalidateProduct(queryClient, variables.productPublicId),
  })
}

export function useDeleteSku() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ skuPublicId, rowVersion }: { productPublicId: string, skuPublicId: string, rowVersion: string }) =>
      deleteSku(skuPublicId, rowVersion),
    onSuccess: (_data, variables) => invalidateProduct(queryClient, variables.productPublicId),
  })
}

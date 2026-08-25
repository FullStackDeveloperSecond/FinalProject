import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { toValue, type MaybeRefOrGetter } from 'vue'
import { createSku, deleteSku, updateSku } from './api'
import type { CreateSkuRequest, UpdateSkuRequest } from './types'

function invalidateProduct(queryClient: ReturnType<typeof useQueryClient>, productPublicId: string) {
  queryClient.invalidateQueries({ queryKey: ['admin-products', 'detail', productPublicId] })
}

/**
 * PR #24 review round 7 (P2): productPublicId used to be captured once as a plain string at
 * setup time. Vue Router reuses the same ProductEditPage instance across a param-only navigation
 * (`/products/A` -> `/products/B`) — the query and form already switch to B, but a plain string
 * captured at setup stays A, so "新增 SKU" on what's visibly product B's page would silently
 * write the new SKU onto product A and invalidate A's cache entry instead of B's. Accepting a
 * getter and resolving it with toValue() at call time (not setup time) keeps this in sync with
 * whichever product the page is currently showing.
 */
export function useCreateSku(productPublicId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateSkuRequest) => createSku(toValue(productPublicId), request),
    onSuccess: () => invalidateProduct(queryClient, toValue(productPublicId)),
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

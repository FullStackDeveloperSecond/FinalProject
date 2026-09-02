import { useMutation, useQuery } from '@tanstack/vue-query'
import { fetchCheckoutPolicyVersions, submitCheckout, type CreateOrderRequest } from './api'
import { getOrCreateGuestCartKey } from '../cart/guestCartKey'

export function useCheckoutPolicyVersionsQuery() {
  return useQuery({
    queryKey: ['checkout', 'policy-versions'] as const,
    queryFn: fetchCheckoutPolicyVersions,
    // Policy versions only change through an admin action, not per-shopper state — safe to treat
    // as effectively static for the lifetime of one checkout visit.
    staleTime: Infinity,
  })
}

export function useSubmitCheckoutMutation() {
  return useMutation({
    mutationFn: (params: { body: CreateOrderRequest, idempotencyKey: string }) =>
      submitCheckout(params.body, params.idempotencyKey, getOrCreateGuestCartKey()),
  })
}

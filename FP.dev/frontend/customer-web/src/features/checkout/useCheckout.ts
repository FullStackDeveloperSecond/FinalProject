import { useMutation, useQuery } from '@tanstack/vue-query'
import { createOrder, fetchShippingOptions, type CreateOrderRequest, type OrderDto } from './api'

export function useShippingOptions() {
  return useQuery({
    queryKey: ['checkout', 'shipping-options'],
    queryFn: fetchShippingOptions,
    staleTime: 60_000,
  })
}

export function useCreateOrder() {
  return useMutation({
    mutationFn: (params: { body: CreateOrderRequest, idempotencyKey: string, isMember: boolean }) =>
      createOrder(params.body, params.idempotencyKey, params.isMember),
  })
}

export type { OrderDto }

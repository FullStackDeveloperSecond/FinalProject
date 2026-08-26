import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  addCartItem,
  getCart,
  mergeCartOnLogin,
  removeCartItem,
  revalidateCart,
  updateCartItemQuantity,
} from './api'
import { clearGuestCartKey, getOrCreateGuestCartKey } from './guestCartKey'

const cartQueryKey = ['cart'] as const

export function useCart() {
  return useQuery({
    queryKey: cartQueryKey,
    queryFn: () => getCart(getOrCreateGuestCartKey()),
  })
}

export function useAddCartItem() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (params: { skuPublicId: string, quantity: number, cartRowVersion: string | null }) =>
      addCartItem(params.skuPublicId, params.quantity, params.cartRowVersion, getOrCreateGuestCartKey()),
    onSuccess: (cart) => {
      queryClient.setQueryData(cartQueryKey, cart)
    },
  })
}

export function useUpdateCartItemQuantity() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (params: { itemPublicId: string, quantity: number, itemRowVersion: string, cartRowVersion: string }) =>
      updateCartItemQuantity(
        params.itemPublicId,
        params.quantity,
        params.itemRowVersion,
        params.cartRowVersion,
        getOrCreateGuestCartKey(),
      ),
    onSuccess: (cart) => {
      queryClient.setQueryData(cartQueryKey, cart)
    },
  })
}

export function useRemoveCartItem() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (params: { itemPublicId: string, itemRowVersion: string }) =>
      removeCartItem(params.itemPublicId, params.itemRowVersion, getOrCreateGuestCartKey()),
    onSuccess: (cart) => {
      queryClient.setQueryData(cartQueryKey, cart)
    },
  })
}

export function useRevalidateCart() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => revalidateCart(getOrCreateGuestCartKey()),
    onSuccess: (validation) => {
      queryClient.setQueryData(cartQueryKey, validation.cart)
    },
  })
}

/**
 * Exported for a future login flow to call directly — not wired to any UI here, since no
 * login flow exists yet in this repo (see `features/cart/api.ts`'s `mergeCartOnLogin` remarks).
 */
export function useMergeCartOnLogin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (idempotencyKey: string) => mergeCartOnLogin(getOrCreateGuestCartKey(), idempotencyKey),
    onSuccess: (result) => {
      queryClient.setQueryData(cartQueryKey, result.cart)
      clearGuestCartKey()
    },
  })
}

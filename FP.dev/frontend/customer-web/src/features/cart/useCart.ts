import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed } from 'vue'
import {
  addCartItem,
  getCart,
  mergeCartOnLogin,
  removeCartItem,
  revalidateCart,
  updateCartItemQuantity,
} from './api'
import { clearGuestCartKey, getOrCreateGuestCartKey } from './guestCartKey'
import { useSessionStore } from '../../stores/session'
import type { CartDto } from './types'

/**
 * 組長 PR #29 review round 3, P1: every shopper's cart used the same fixed query key (['cart'])
 * regardless of who they were. TanStack Query's QueryClient is a single SPA-wide singleton, and
 * login/logout only update the session store — they never touch this cache — so member A's
 * cached cart could still be what a component reads for a moment after A logs out, before the
 * next request for the new (guest, or member B) identity's cart replaces it. Scoping the key
 * itself to "whose cart this is" (member PublicId, or the guest cart key) makes that leak
 * structurally impossible: a different identity is a genuinely different cache entry, never
 * served in place of the current one, regardless of timing.
 */
function useCartIdentityKey() {
  const sessionStore = useSessionStore()
  return computed(() =>
    sessionStore.isAuthenticated && sessionStore.user
      ? (['cart', 'member', sessionStore.user.publicId] as const)
      : (['cart', 'guest', getOrCreateGuestCartKey()] as const),
  )
}

export function useCart() {
  const sessionStore = useSessionStore()
  const identityKey = useCartIdentityKey()
  return useQuery({
    queryKey: identityKey,
    queryFn: () => getCart(getOrCreateGuestCartKey()),
    // Session status starts 'loading' on every fresh app load (App.vue's onMounted kicks off
    // sessionStore.refresh()) — fetching a guest-keyed cart before that resolves would show a
    // guest cart that's immediately thrown away and replaced by the real member cart once
    // refresh() completes, the same kind of flash this fix is meant to eliminate.
    enabled: computed(() => sessionStore.status !== 'loading'),
  })
}

export function useAddCartItem() {
  const queryClient = useQueryClient()
  const identityKey = useCartIdentityKey()
  return useMutation({
    mutationFn: (params: { skuPublicId: string, quantity: number, cartRowVersion: string | null }) =>
      addCartItem(params.skuPublicId, params.quantity, params.cartRowVersion, getOrCreateGuestCartKey()),
    onSuccess: (cart) => {
      queryClient.setQueryData(identityKey.value, cart)
    },
  })
}

export function useUpdateCartItemQuantity() {
  const queryClient = useQueryClient()
  const identityKey = useCartIdentityKey()
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
      queryClient.setQueryData(identityKey.value, cart)
    },
  })
}

export function useRemoveCartItem() {
  const queryClient = useQueryClient()
  const identityKey = useCartIdentityKey()
  return useMutation({
    mutationFn: (params: { itemPublicId: string, itemRowVersion: string }) =>
      removeCartItem(params.itemPublicId, params.itemRowVersion, getOrCreateGuestCartKey()),
    onSuccess: (cart) => {
      queryClient.setQueryData(identityKey.value, cart)
    },
  })
}

export function useRevalidateCart() {
  const queryClient = useQueryClient()
  const identityKey = useCartIdentityKey()
  return useMutation({
    mutationFn: () => revalidateCart(getOrCreateGuestCartKey()),
    onSuccess: (validation) => {
      queryClient.setQueryData(identityKey.value, validation.cart)
    },
  })
}

/**
 * A silent reload for recovering from a stale-RowVersion conflict: writes straight into the
 * query cache via `setQueryData` instead of going through the query's own `refetch()`. `refetch`
 * shares its `isError`/`error` state with the query's normal `isPending`/`data`, so a failed
 * recovery reload would flip the *whole* cart page over to the generic full-page ErrorState
 * (CartPage.vue checks `isError` before `cart` in its template) — collapsing the item list,
 * warnings, and checkout section along with it, not just showing a small recoverable message next
 * to the row that failed. This bypasses that entirely: a failure here only ever surfaces through
 * whatever the caller does with the thrown error, never through the reactive query's own state.
 */
export function useReloadCart() {
  const queryClient = useQueryClient()
  const identityKey = useCartIdentityKey()
  return async function reloadCart(): Promise<CartDto> {
    const cart = await getCart(getOrCreateGuestCartKey())
    queryClient.setQueryData(identityKey.value, cart)
    return cart
  }
}

/**
 * Exported for a future login flow to call directly — not wired to any UI here, since the merge
 * endpoint's own 409 whole-merge-rejection response still isn't handled by the shared client (see
 * `features/cart/api.ts`'s `mergeCartOnLogin` remarks).
 */
export function useMergeCartOnLogin() {
  const queryClient = useQueryClient()
  const identityKey = useCartIdentityKey()
  return useMutation({
    mutationFn: (idempotencyKey: string) => mergeCartOnLogin(getOrCreateGuestCartKey(), idempotencyKey),
    onSuccess: (result) => {
      queryClient.setQueryData(identityKey.value, result.cart)
      clearGuestCartKey()
    },
  })
}

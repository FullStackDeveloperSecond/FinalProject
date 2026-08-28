import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, watch } from 'vue'
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

/**
 * 組長 PR #29 round-6 review, P1: `useCart()`'s own GET is gated on `status !== 'loading'`, but
 * nothing gated the mutations — a shopper whose member Cookie the backend already recognizes, but
 * whose frontend session refresh hasn't resolved yet, could still add to cart. `identityKey`
 * treats "not confirmedly authenticated" (which 'loading' is) the same as "guest", so the request
 * gets snapshotted under the guest cache key while the backend actually acts on the member's real
 * cart — the response then lands in the wrong identity's cache entirely.
 *
 * Thrown from `onMutate` (not checked separately in a component before calling `.mutate()`)
 * because `onMutate` is the one place TanStack Query guarantees runs synchronously, exactly once,
 * at the instant `mutate()`/`mutateAsync()` is actually invoked — the same moment the snapshot
 * below is taken. A throw there aborts before `mutationFn` ever runs (verified against
 * @tanstack/query-core's Mutation#execute: `onMutate` is awaited before `retryer.start()`, so a
 * throw here skips the network call entirely), so this is a real gate, not just a UI hint that a
 * caller could forget to check.
 */
export class CartIdentityNotResolvedError extends Error {
  constructor() {
    super('Cart identity is not resolved yet — session status is still \'loading\'.')
    this.name = 'CartIdentityNotResolvedError'
  }
}

function snapshotCartMutationIdentity(sessionStore: ReturnType<typeof useSessionStore>) {
  if (sessionStore.status === 'loading') {
    throw new CartIdentityNotResolvedError()
  }
  return sessionStore.isAuthenticated && sessionStore.user
    ? (['cart', 'member', sessionStore.user.publicId] as const)
    : (['cart', 'guest', getOrCreateGuestCartKey()] as const)
}

/**
 * 組長 PR #29 round-4 review, P1 (widened in round-6 review, point 3): the identity-snapshot fix
 * on the mutations below stops a stale in-flight response from being written into the *new*
 * identity's cache, but on its own it leaves the *old* identity's cache entry (and any request
 * still in flight for it) sitting around indefinitely after a login/logout/account-switch. This
 * used to live inside useCart() itself, so it only ran while CartPage happened to be mounted — a
 * shopper who logged out (or switched member accounts) while browsing anywhere else, e.g.
 * ProductDetailPage, left the previous identity's cart cache with nothing to evict it. Call this
 * once, globally, from App.vue (mounted for the SPA's entire lifetime) instead of from a page that
 * mounts and unmounts with routing, so the cleanup no longer depends on which page happens to be
 * open at the moment identity changes.
 */
export function useCartIdentityCacheCleanup(): void {
  const queryClient = useQueryClient()
  const identityKey = useCartIdentityKey()
  watch(identityKey, (_current, previous) => {
    if (previous) {
      void queryClient.cancelQueries({ queryKey: previous })
      queryClient.removeQueries({ queryKey: previous })
    }
  })
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
    // 組長 PR #29 review round 5, P2: with the shared QueryClient's 30s staleTime, revisiting
    // /cart after being away longer than that used to show the cached cart immediately (isPending
    // already false) while TanStack's own default mount-refetch quietly re-GET the same query in
    // the background — racing CartPage.vue's explicit `revalidate` call, which is the one place
    // that keeps `issues`/`isCheckoutReady` in sync with the cart it writes. Whichever of the two
    // requests landed second could silently overwrite the other's result, leaving the rendered
    // items and the checkout gate disagreeing. There is no scenario where this second, implicit
    // fetch is needed: every path that can make the cart stale (initial mount, and every mutation
    // in this file) already ends in an explicit revalidate call, so refetchOnMount would only ever
    // be racing that call, never doing useful work TanStack Query.
    refetchOnMount: false,
  })
}

// 組長 PR #29 round-4 review, P1: every mutation below used to write its response back with
// `queryClient.setQueryData(identityKey.value, ...)` inside `onSuccess` — reading the *reactive*
// current identity at the moment the response arrives, not whatever identity was active when the
// request was actually sent. If the shopper logs out (or a different member logs in) while the
// request is still in flight, the response would land in the *new* identity's cache instead of
// the one it actually belongs to — a real cross-identity cart exposure. `onMutate` runs
// synchronously the instant `mutate()`/`mutateAsync()` is called, before `mutationFn` starts, so
// its return value becomes an immutable snapshot of the identity key at request-start time;
// TanStack Query passes that snapshot through to `onSuccess` as its third argument regardless of
// what `identityKey.value` becomes in the meantime.
export function useAddCartItem() {
  const queryClient = useQueryClient()
  const sessionStore = useSessionStore()
  return useMutation({
    mutationFn: (params: { skuPublicId: string, quantity: number, cartRowVersion: string | null }) =>
      addCartItem(params.skuPublicId, params.quantity, params.cartRowVersion, getOrCreateGuestCartKey()),
    onMutate: () => snapshotCartMutationIdentity(sessionStore),
    onSuccess: (cart, _variables, targetKey) => {
      queryClient.setQueryData(targetKey, cart)
    },
  })
}

export function useUpdateCartItemQuantity() {
  const queryClient = useQueryClient()
  const sessionStore = useSessionStore()
  return useMutation({
    mutationFn: (params: { itemPublicId: string, quantity: number, itemRowVersion: string, cartRowVersion: string }) =>
      updateCartItemQuantity(
        params.itemPublicId,
        params.quantity,
        params.itemRowVersion,
        params.cartRowVersion,
        getOrCreateGuestCartKey(),
      ),
    onMutate: () => snapshotCartMutationIdentity(sessionStore),
    onSuccess: (cart, _variables, targetKey) => {
      queryClient.setQueryData(targetKey, cart)
    },
  })
}

export function useRemoveCartItem() {
  const queryClient = useQueryClient()
  const sessionStore = useSessionStore()
  return useMutation({
    mutationFn: (params: { itemPublicId: string, itemRowVersion: string }) =>
      removeCartItem(params.itemPublicId, params.itemRowVersion, getOrCreateGuestCartKey()),
    onMutate: () => snapshotCartMutationIdentity(sessionStore),
    onSuccess: (cart, _variables, targetKey) => {
      queryClient.setQueryData(targetKey, cart)
    },
  })
}

export function useRevalidateCart() {
  const queryClient = useQueryClient()
  const sessionStore = useSessionStore()
  return useMutation({
    mutationFn: () => revalidateCart(getOrCreateGuestCartKey()),
    onMutate: () => snapshotCartMutationIdentity(sessionStore),
    onSuccess: (validation, _variables, targetKey) => {
      queryClient.setQueryData(targetKey, validation.cart)
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
  const sessionStore = useSessionStore()
  return async function reloadCart(): Promise<CartDto> {
    // Not a useMutation, so there's no onMutate hook to snapshot in — capture the identity
    // synchronously here, before the request's own await, for the same reasons as the mutations
    // above: writing back to whatever's current when the response lands (not when it was sent)
    // risks a cross-identity write if identity changes mid-flight, and firing while session status
    // is still 'loading' risks writing a member's cart into the guest cache key.
    const targetKey = snapshotCartMutationIdentity(sessionStore)
    const cart = await getCart(getOrCreateGuestCartKey())
    queryClient.setQueryData(targetKey, cart)
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
  const sessionStore = useSessionStore()
  return useMutation({
    mutationFn: (idempotencyKey: string) => mergeCartOnLogin(getOrCreateGuestCartKey(), idempotencyKey),
    onMutate: () => snapshotCartMutationIdentity(sessionStore),
    onSuccess: (result, _variables, targetKey) => {
      queryClient.setQueryData(targetKey, result.cart)
      clearGuestCartKey()
    },
  })
}

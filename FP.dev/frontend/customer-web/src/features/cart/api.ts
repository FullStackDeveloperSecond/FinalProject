import { apiClient } from '../../api/client'
import type { CartDto, CartMergeResultDto, CartValidationDto } from './types'

function guestHeaders(guestCartKey?: string): HeadersInit | undefined {
  return guestCartKey ? { 'X-DoSelect-Guest-Cart-Key': guestCartKey } : undefined
}

/**
 * The shared API client's middleware throws an `ApiError` for any non-OK response (see
 * `frontend/shared/src/api/client.ts`), so `data` is always populated on the success path
 * handled here — callers do not need to additionally check openapi-fetch's own `error` field.
 */
export async function getCart(guestCartKey?: string): Promise<CartDto> {
  const { data } = await apiClient.GET('/api/v1/cart', {
    headers: guestHeaders(guestCartKey),
  })
  return data!
}

export async function addCartItem(
  skuPublicId: string,
  quantity: number,
  cartRowVersion: string | null,
  guestCartKey?: string,
): Promise<CartDto> {
  const { data } = await apiClient.POST('/api/v1/cart/items', {
    body: { skuPublicId, quantity, cartRowVersion },
    headers: guestHeaders(guestCartKey),
  })
  return data!
}

export async function updateCartItemQuantity(
  itemPublicId: string,
  quantity: number,
  itemRowVersion: string,
  cartRowVersion: string,
  guestCartKey?: string,
): Promise<CartDto> {
  const { data } = await apiClient.PATCH('/api/v1/cart/items/{id}', {
    params: { path: { id: itemPublicId } },
    body: { quantity, itemRowVersion, cartRowVersion },
    headers: guestHeaders(guestCartKey),
  })
  return data!
}

export async function removeCartItem(
  itemPublicId: string,
  itemRowVersion: string,
  guestCartKey?: string,
): Promise<CartDto> {
  const { data } = await apiClient.DELETE('/api/v1/cart/items/{id}', {
    params: { path: { id: itemPublicId } },
    body: { itemRowVersion },
    headers: guestHeaders(guestCartKey),
  })
  return data!
}

export async function revalidateCart(guestCartKey?: string): Promise<CartValidationDto> {
  const { data } = await apiClient.POST('/api/v1/cart/actions/revalidate', {
    headers: guestHeaders(guestCartKey),
  })
  return data!
}

/**
 * Member-only — no guest header needed, the caller must already be signed in. Not called from
 * anywhere yet: there is no real login flow in this repo for it to hook into (same non-goal as
 * slice 1's admin-web 401/403 wiring).
 *
 * PR #29 review round 2 (組長): this PR's actual scope is the cart page plus this not-yet-wired
 * merge hook — it does not deliver UC-CART-02's "guest cart merges automatically on login"
 * behavior end-to-end, and must not be represented as doing so. Tracked gate before that can be
 * true: a real login flow (haru's M-01/M-02) needs to call `useMergeCartOnLogin` right after a
 * successful sign-in, and whoever wires that up also needs to handle the merge endpoint's 409
 * whole-merge-rejection response — the generated OpenAPI schema doesn't document a typed 409 body
 * for this route (only 200), so the shared client's error middleware currently discards it into a
 * generic thrown ApiError.
 */
export async function mergeCartOnLogin(
  guestCartKey: string,
  idempotencyKey: string,
): Promise<CartMergeResultDto> {
  const { data } = await apiClient.POST('/api/v1/cart/actions/merge', {
    body: { guestCartKey, strategy: 'mergeAndReportConflicts', idempotencyKey },
  })
  return data!
}

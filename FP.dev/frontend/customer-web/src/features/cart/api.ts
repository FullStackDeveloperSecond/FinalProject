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

/**
 * 組長 PR #29 round 7 review, P1（AUTO-DEC-015）: one atomic server-side removal of every row
 * sharing this AssemblyGroupKey. Deliberately NOT a loop of per-item DELETEs on the client — a
 * failure part-way through that loop would leave the group split apart, which is exactly the state
 * the "assembly groups can't be edited individually" rule exists to prevent. Cart-level RowVersion
 * (not an item's), since a group spans multiple rows.
 */
export async function removeCartAssemblyGroup(
  assemblyGroupKey: string,
  cartRowVersion: string,
  guestCartKey?: string,
): Promise<CartDto> {
  const { data } = await apiClient.DELETE('/api/v1/cart/assembly-groups/{assemblyGroupKey}', {
    params: { path: { assemblyGroupKey } },
    body: { cartRowVersion },
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

/**
 * 套用優惠碼，回傳重算後的購物車。
 *
 * 折扣由伺服器算 —— 前端不自己把金額扣掉，也不判斷券適不適用。不合用時
 * 後端回 `coupon_not_applicable`／`coupon_usage_exhausted`／`coupon_not_active`。
 */
export async function applyCartCoupon(
  code: string,
  cartRowVersion: string,
  guestCartKey?: string,
): Promise<CartDto> {
  const { data } = await apiClient.POST('/api/v1/cart/coupon', {
    body: { code, cartRowVersion },
    headers: guestHeaders(guestCartKey),
  })
  return data!
}

/**
 * 移除優惠碼。
 *
 * 折扣沒有被保存下來，套用是每次重算的，所以這支只是回傳目前的購物車 ——
 * 端點存在是為了讓前端有對稱的 API。
 */
export async function removeCartCoupon(guestCartKey?: string): Promise<CartDto> {
  const { data } = await apiClient.DELETE('/api/v1/cart/coupon', {
    headers: guestHeaders(guestCartKey),
  })
  return data!
}

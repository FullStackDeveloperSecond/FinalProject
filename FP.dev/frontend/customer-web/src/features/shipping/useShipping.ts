import { useQuery } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { getShippingOptions, searchConvenienceStores, type ConvenienceStoreSearchParams } from './api'
import { getOrCreateGuestCartKey } from '../cart/guestCartKey'
import { useSessionStore } from '../../stores/session'

/**
 * Shipping options are computed for *a* cart, so they are as identity-scoped as the cart itself.
 * This mirrors `useCart`'s identity key exactly (組長 PR #29 round-4/6/7 reviews): a member and a
 * guest are genuinely different cache entries, "not confirmedly authenticated" counts as guest,
 * and the query stays disabled until the session is confirmed — otherwise a guest's options could
 * be served to a member for the flash between mount and session refresh.
 */
function useShippingIdentity() {
  const sessionStore = useSessionStore()
  return computed(() =>
    sessionStore.isAuthenticated && sessionStore.user
      ? (['member', sessionStore.user.publicId] as const)
      : (['guest', getOrCreateGuestCartKey()] as const),
  )
}

/**
 * 自我審查發現：購物車的每個 mutation 都只用 `setQueryData` 寫回 cart 這個快取鍵，沒有任何東西會
 * 讓配送選項失效——改完數量、刪掉一件重的商品之後，畫面上的超取資格與運費仍是舊的算法結果。
 *
 * 修法不是在每個 mutation 補一行 invalidate（總會有人新增 mutation 時忘記），而是把購物車的
 * RowVersion 直接放進 query key：購物車一變就是另一個鍵，舊結果結構上不可能被當成新的用。後端的
 * `ShippingOptionsDto` 也回傳 `cartRowVersion`，本來就是「這組選項屬於哪個購物車版本」的語意。
 */
export function useShippingOptions(
  enabled: MaybeRefOrGetter<boolean> = true,
  cartRowVersion: MaybeRefOrGetter<string | null | undefined> = null,
) {
  const sessionStore = useSessionStore()
  const identity = useShippingIdentity()

  return useQuery({
    queryKey: computed(() => ['shipping-options', ...identity.value, toValue(cartRowVersion) ?? ''] as const),
    queryFn: () => getShippingOptions(getOrCreateGuestCartKey()),
    enabled: computed(() => sessionStore.isIdentityConfirmed && toValue(enabled)),
    // 換購物車版本時保留上一版結果，避免每次改數量畫面都閃一次載入中。
    placeholderData: (previous) => previous,
  })
}

export function useConvenienceStoreSearch(
  params: MaybeRefOrGetter<ConvenienceStoreSearchParams>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  return useQuery({
    queryKey: computed(() => ['convenience-stores', toValue(params)] as const),
    queryFn: () => searchConvenienceStores(toValue(params)),
    enabled: computed(() => toValue(enabled)),
    placeholderData: (previous) => previous,
  })
}

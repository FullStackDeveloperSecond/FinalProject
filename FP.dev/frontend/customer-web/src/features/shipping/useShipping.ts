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
  couponCode: MaybeRefOrGetter<string | null | undefined> = null,
) {
  const sessionStore = useSessionStore()
  const identity = useShippingIdentity()

  return useQuery({
    queryKey: computed(() => [
      'shipping-options',
      ...identity.value,
      toValue(cartRowVersion) ?? '',
      toValue(couponCode) ?? '',
    ] as const),
    queryFn: () => getShippingOptions(
      getOrCreateGuestCartKey(),
      toValue(couponCode) || undefined,
    ),
    enabled: computed(() => sessionStore.isIdentityConfirmed && toValue(enabled)),
    // 組長 PR #79 round-2 review item 1：這裡原本用 `placeholderData` 保留上一版結果避免閃爍，
    // 但那正好抵銷了把 RowVersion 放進 key 的意義——購物車改完之後，新請求回來之前畫面上仍是
    // 舊運費與舊資格，selectable 模式下甚至可能被選走。寧可閃一下載入中，也不給一組屬於別台
    // 購物車的選項。
  })
}

export function useConvenienceStoreSearch(
  params: MaybeRefOrGetter<ConvenienceStoreSearchParams>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  return useQuery({
    queryKey: computed(() => ['convenience-stores', toValue(params)] as const),
    queryFn: () => searchConvenienceStores(toValue(params)),
    // 組長 PR #79 round-2 review item 3：同理，門市搜尋也不跨篩選條件沿用上一組結果——新結果
    // 回來之前顯示並允許選取不符合目前條件的舊門市，是會讓顧客選錯門市的。
    enabled: computed(() => toValue(enabled)),
  })
}

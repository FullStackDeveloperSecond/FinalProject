<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref, watch } from 'vue'
import CartLineItem from '../features/cart/components/CartLineItem.vue'
import ShippingOptionList from '../features/shipping/components/ShippingOptionList.vue'
import { useShippingOptions } from '../features/shipping/useShipping'
import {
  useCart,
  useReloadCart,
  useRemoveCartAssemblyGroup,
  useRemoveCartItem,
  useRevalidateCart,
  useUpdateCartItemQuantity,
} from '../features/cart/useCart'
import { useSessionStore } from '../stores/session'
import type { CartItemDto, CartIssueDto } from '../features/cart/types'

const sessionStore = useSessionStore()

// 組長 PR #29 review: bare issue codes ("cart_item_requires_attention") aren't something a
// shopper can act on — CartWarningDto already carries a backend-authored human message, but
// CartIssueDto only ever carries a stable code (see ShoppingWriteException.ErrorCodes /
// EfCartService.GetAvailabilityConcern), so the mapping has to live here. Keep this in sync with
// every CartIssueDto.Code the backend actually emits.
const ISSUE_MESSAGES: Record<string, string> = {
  sku_unavailable: '此商品已下架，請移除此品項。',
  cart_item_requires_attention: '庫存不足，請調整數量或移除此品項。',
  cart_item_limit_exceeded: '購物車已超過 100 件上限，請先清空部分品項再合併。',
  cart_merge_conflict: '登入合併時發生衝突，請調整數量或移除此品項。',
}

const ACTION_LABELS: Record<string, string> = {
  'reduce-quantity': '調整數量',
  remove: '移除',
  // 組長 PR #29 round 7 review, P1（AUTO-DEC-015）: the backend now reports this instead of the
  // reduce-quantity/remove it would itself refuse for a grouped item.
  'remove-group': '整組移除',
}

function describeIssue(issue: CartIssueDto): string {
  return ISSUE_MESSAGES[issue.code] ?? issue.code
}

function describeIssueActions(issue: CartIssueDto): string {
  return issue.availableActions.map((action) => ACTION_LABELS[action] ?? action).join('、')
}

const { data: cart, isPending, isError, error, refetch } = useCart()
const updateQuantity = useUpdateCartItemQuantity()
const removeItem = useRemoveCartItem()
const removeAssemblyGroup = useRemoveCartAssemblyGroup()
const revalidate = useRevalidateCart()
const reloadCart = useReloadCart()

interface CartItemGroup {
  assemblyGroupKey: string | null
  items: CartItemDto[]
}

// 組長 PR #29 review: an assembled build must render as one group with its individual SKUs kept
// visible underneath (商品、組裝與相容性.md: "組裝電腦在畫面上以群組顯示，但底層保留每一個 SKU"); a flat
// v-for over cart.items ignored assemblyGroupKey entirely and interleaved a build's parts with
// unrelated SKUs. Plain items (assemblyGroupKey null) each become their own single-item group so
// the template can render both shapes with one loop.
const itemGroups = computed<CartItemGroup[]>(() => {
  if (!cart.value) {
    return []
  }

  const groups: CartItemGroup[] = []
  const groupIndexByKey = new Map<string, number>()
  for (const item of cart.value.items) {
    if (item.assemblyGroupKey === null) {
      groups.push({ assemblyGroupKey: null, items: [item] })
      continue
    }

    const existingIndex = groupIndexByKey.get(item.assemblyGroupKey)
    if (existingIndex === undefined) {
      groupIndexByKey.set(item.assemblyGroupKey, groups.length)
      groups.push({ assemblyGroupKey: item.assemblyGroupKey, items: [item] })
    } else {
      groups[existingIndex].items.push(item)
    }
  }

  return groups
})

// 組長 PR #29 review: onChangeQuantity/onRemoveItem only handled onSuccess — a failed write left
// the shopper with no feedback beyond the button re-enabling, and gave no way to tell whether the
// server actually applied it before the response was lost. itemActionError surfaces a retryable
// message next to the affected row; a concurrency_conflict specifically means this cart's/item's
// RowVersion the shopper was acting on is already stale, so it also refetches the live cart
// before the shopper tries again, instead of letting them retry against data that's already wrong.
const itemActionError = ref<{ itemPublicId: string, message: string } | null>(null)

// 組長 PR #29 review round 3, P2: the original fix fired `void refetch()` and considered recovery
// done — but the mutation's own `isPending` (part of `isBusy`) had already gone false by the time
// this ran, so the shopper could click another action using the same stale RowVersion before the
// refetch even landed, walking straight into a second conflict. `isRecoveringFromConflict` keeps
// every control disabled for the whole recovery window; a failed refetch shows its own error
// instead of silently leaving the "已重新載入" message up for a reload that never happened; and a
// successful refetch is followed by a real revalidate (awaited, not fire-and-forget) so `issues`/
// `isCheckoutReady` reflect the just-reloaded cart, not whatever they were before the conflict.
const isRecoveringFromConflict = ref(false)

function describeItemActionError(caught: unknown): string {
  if (isApiError(caught) && caught.code === 'concurrency_conflict') {
    return '此品項已被更新，購物車已重新載入，請確認後再試一次。'
  }
  if (isApiError(caught)) {
    return `操作失敗（${caught.code}），請重試。`
  }
  return '操作失敗，請重試。'
}

async function onItemActionError(itemPublicId: string, caught: unknown): Promise<void> {
  itemActionError.value = { itemPublicId, message: describeItemActionError(caught) }
  if (!isApiError(caught) || caught.code !== 'concurrency_conflict') {
    return
  }

  isRecoveringFromConflict.value = true
  try {
    await reloadCart()
    await runRevalidate()
  } catch {
    itemActionError.value = { itemPublicId, message: '購物車重新載入失敗，請重試。' }
  } finally {
    isRecoveringFromConflict.value = false
  }
}

const issues = ref<CartIssueDto[]>([])
const isCheckoutReady = ref(false)
const revalidateError = ref<unknown>(null)

// 組長 PR #29 round-6 review, P2: `isCheckoutReady` used to only ever be *set* on a successful
// revalidate — starting a new revalidate, a failed one, or a Cart mutation racing ahead of its
// own follow-up revalidate all left whatever the *previous* successful result happened to be in
// place, so the checkout button could stay enabled against a Cart that no longer matches what was
// actually validated. Rather than track "has a revalidate ever succeeded" as a separate boolean
// that has to be remembered to reset on every one of those cases, `validatedForRowVersion` records
// *which* Cart RowVersion the last successful revalidate actually matched; `canCheckout` below
// requires that to equal the *current* `cart.value.rowVersion` — a mutation's own onSuccess writes
// the new RowVersion into the query cache before this page's follow-up revalidate call even
// starts (mutation-level onSuccess runs before the call-site onSuccess that triggers it), so the
// mismatch is real starting from the instant the mutation lands, not just once revalidate notices.
// Switching identity (a completely different Cart, so a different RowVersion) is covered by the
// same comparison — no separate identity tracking needed.
const validatedForRowVersion = ref<string | null>(null)

const canCheckout = computed(() => {
  if (!cart.value || revalidate.isPending.value || revalidateError.value) {
    return false
  }
  return validatedForRowVersion.value === cart.value.rowVersion && isCheckoutReady.value
})

// PR #29 review round 2: runRevalidate used to be fired-and-forgotten from onMounted and every
// mutation's onSuccess with no lifecycle management — overlapping calls could resolve out of
// order and let a stale response overwrite a newer one's issues/checkout-gate state, failures
// were silently swallowed (no error shown, no retry), and item mutations stayed enabled while
// a revalidate was still in flight. `needsAnotherRevalidate` coalesces any trigger that arrives
// while one is already running into exactly one follow-up call instead of a second concurrent
// request, which also means only one response is ever in flight — there's nothing "stale" left
// to race.
const needsAnotherRevalidate = ref(false)

async function runRevalidate(): Promise<void> {
  if (revalidate.isPending.value) {
    needsAnotherRevalidate.value = true
    return
  }

  revalidateError.value = null
  try {
    const result = await revalidate.mutateAsync()
    issues.value = result.issues
    isCheckoutReady.value = result.isCheckoutReady
    validatedForRowVersion.value = result.cart.rowVersion
  } catch (caught) {
    revalidateError.value = caught
  } finally {
    if (needsAnotherRevalidate.value) {
      needsAnotherRevalidate.value = false
      void runRevalidate()
    }
  }
}

// 組長 PR #29 round-4 review, P2: this used to fire unconditionally from onMounted, starting
// concurrently with useCart()'s own initial GET (itself gated on session no longer 'loading').
// If revalidate resolved first and wrote the freshest cart into the query cache, the initial GET
// — started earlier but arriving later — could still overwrite it with older data, while
// `issues`/`isCheckoutReady` kept reflecting revalidate's newer (now-orphaned) result: the
// rendered cart and the checkout gate would disagree. `isPending` (from useCart()) is true until
// the query has *never yet* resolved — waiting for it to become false, with no error, means the
// initial GET has already landed with nothing older left in flight that could still clobber a
// revalidate's result. `hasStartedInitialRevalidate` guards this to firing exactly once per page
// load (purely a "don't re-trigger the automatic kickoff" latch — not the checkout-gate's own
// validity tracking, which now lives in `validatedForRowVersion` above); a retry after an initial
// load failure (ErrorState's @retry) still triggers exactly one revalidate once the retried GET
// succeeds, since that path doesn't go through this watch at all.
//
// 組長 PR #29 round-5 review, P2: `isPending` alone isn't enough when the query already has cached
// data on mount (the shopper had the cart open before, came back after the 30s staleTime expired)
// — `isPending` is false immediately in that case, but TanStack's own default mount-refetch used
// to kick off a *second*, implicit GET in the background at the same moment, racing this
// revalidate call exactly like the round-4 finding above. Fixed at the source instead of by
// widening this guard: useCart.ts now sets `refetchOnMount: false`, since every path that can make
// the cart stale already funnels through an explicit revalidate (this one, and every mutation's
// onSuccess below) — there is no longer a second fetch for this watch to race against.
//
// 組長 PR #29 round-6 review, P2: this used to be a fire-*once-ever* guard
// (`hasRevalidated`/`hasStartedInitialRevalidate`) — correct for the initial mount, but it also
// meant a shopper who logged in (or switched member accounts) after that first revalidate had
// already run would never get an automatic revalidate for the newly-loaded Cart: `isPending`
// toggles true -> false again once the new identity's Cart finishes its own first fetch (a brand
// -new query key always fetches once regardless of `refetchOnMount: false`, which only suppresses
// refetching *already-cached* data), but the old one-shot flag silently swallowed that second
// resolution. Tracking *which RowVersion* was last auto-triggered (instead of "was this ever
// triggered") fires again for a genuinely different Cart while still not re-firing redundantly for
// the same Cart settling multiple times. This doesn't double up with the explicit per-mutation
// onSuccess calls below: a mutation's own `setQueryData` doesn't toggle `isPending`, so this watch
// only ever reacts to real query (re)fetches — initial load and identity switches — not mutations.
let lastAutoRevalidatedForRowVersion: string | null = null
watch(
  isPending,
  (pending) => {
    if (pending || isError.value || !cart.value) {
      return
    }
    if (lastAutoRevalidatedForRowVersion !== cart.value.rowVersion) {
      lastAutoRevalidatedForRowVersion = cart.value.rowVersion
      void runRevalidate()
    }
  },
  { immediate: true },
)

// Mutation controls are disabled (see isBusy/template) whenever a revalidate is in flight, so
// this only guards the two callers of onChangeQuantity/onRemoveItem that aren't gated by that
// disabled state: a mutation's own onSuccess (which can itself only run once, so not a real
// overlap risk) and defensive-programming against a future caller that forgets to check isBusy.
function onChangeQuantity(itemPublicId: string, itemRowVersion: string, quantity: number): void {
  if (!cart.value || isBusy.value) {
    return
  }

  itemActionError.value = null
  updateQuantity.mutate(
    { itemPublicId, quantity, itemRowVersion, cartRowVersion: cart.value.rowVersion },
    {
      onSuccess: () => { void runRevalidate() },
      onError: (caught) => { void onItemActionError(itemPublicId, caught) },
    },
  )
}

function onRemoveItem(itemPublicId: string, itemRowVersion: string): void {
  if (isBusy.value) {
    return
  }

  itemActionError.value = null
  removeItem.mutate(
    { itemPublicId, itemRowVersion },
    {
      onSuccess: () => { void runRevalidate() },
      onError: (caught) => { void onItemActionError(itemPublicId, caught) },
    },
  )
}

// 組長 PR #29 round 7 review, P1（AUTO-DEC-015）: the only action that works on an assembly group
// — every per-item mutation is rejected server-side for a grouped item, so before this a group
// whose SKU went unavailable held the checkout gate open with no way for the shopper to clear it.
// One atomic backend call, never a client-side loop of per-item DELETEs (which could fail
// part-way and split the group apart). Errors surface on the group's first item, reusing the same
// per-row error slot the individual items already use.
const groupActionError = ref<{ assemblyGroupKey: string, message: string } | null>(null)

async function onRemoveAssemblyGroup(assemblyGroupKey: string): Promise<void> {
  if (!cart.value || isBusy.value) {
    return
  }

  groupActionError.value = null
  itemActionError.value = null
  try {
    await removeAssemblyGroup.mutateAsync({ assemblyGroupKey, cartRowVersion: cart.value.rowVersion })
    await runRevalidate()
  } catch (caught) {
    groupActionError.value = { assemblyGroupKey, message: describeItemActionError(caught) }
    if (isApiError(caught) && caught.code === 'concurrency_conflict') {
      isRecoveringFromConflict.value = true
      try {
        await reloadCart()
        await runRevalidate()
      } catch {
        groupActionError.value = { assemblyGroupKey, message: '購物車重新載入失敗，請重試。' }
      } finally {
        isRecoveringFromConflict.value = false
      }
    }
  }
}

const isMutating = computed(() =>
  updateQuantity.isPending.value || removeItem.isPending.value || removeAssemblyGroup.isPending.value)
const isBusy = computed(() => isMutating.value || revalidate.isPending.value || isRecoveringFromConflict.value)

function formatTwd(amount: number | string): string {
  return `NT$${Number(amount).toLocaleString('zh-Hant-TW')}`
}

// Testability only: lets a test drive the coalescing behavior directly (a disabled button
// can't be clicked to simulate a trigger arriving mid-revalidate — the DOM itself suppresses
// click on disabled controls, which is also exactly the real UI guard this relies on).
defineExpose({ runRevalidate })

/**
 * C-13 的「Shipping Options」：購物車頁預覽可用的配送方式與運費，讓顧客在進結帳前就知道超取能不能
 * 用、為什麼不能用（購物車、訂單、付款與物流.md：「只能選擇宅配並顯示原因」）。這裡只是預覽，
 * 選擇配送方式是結帳頁的事，所以不傳 selectable。
 *
 * 只在購物車有商品時查：空車問後端要配送選項沒有意義，而且後端會對空車回一組全部不可用的選項，
 * 顯示出來只會讓顧客困惑。
 */
const hasCartItems = computed(() => (cart.value?.items.length ?? 0) > 0)
const {
  data: shippingOptions,
  isPending: isShippingPending,
  isError: isShippingError,
  refetch: refetchShipping,
} = useShippingOptions(hasCartItems, computed(() => cart.value?.rowVersion))
</script>

<template>
  <section aria-labelledby="cart-page-title">
    <h1 id="cart-page-title">
      購物車
    </h1>

    <div
      v-if="sessionStore.status === 'error'"
      class="cart-page__identity-error"
      role="alert"
    >
      <p>無法確認登入狀態，暫時無法顯示購物車。</p>
      <button
        type="button"
        @click="sessionStore.refresh()"
      >
        重試
      </button>
    </div>
    <LoadingState
      v-else-if="isPending"
      label="購物車載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="(error as { correlationId?: string })?.correlationId"
      @retry="refetch"
    />
    <EmptyState
      v-else-if="cart && cart.items.length === 0"
      title="購物車是空的"
      description="逛逛商品，把喜歡的加進購物車吧。"
    >
      <RouterLink to="/">
        回首頁
      </RouterLink>
    </EmptyState>

    <div
      v-else-if="cart"
      class="cart-page"
    >
      <div class="cart-page__toolbar">
        <button
          type="button"
          :disabled="isBusy"
          @click="runRevalidate"
        >
          重新檢查
        </button>
      </div>

      <p
        v-if="revalidateError"
        class="cart-page__revalidate-error"
      >
        購物車檢查失敗{{ isApiError(revalidateError) ? `（${revalidateError.code}）` : '' }}。
        <button
          type="button"
          :disabled="isBusy"
          @click="runRevalidate"
        >
          重試
        </button>
      </p>

      <ul
        class="cart-page__items"
        aria-label="購物車品項"
      >
        <template
          v-for="group in itemGroups"
          :key="group.assemblyGroupKey ?? group.items[0].publicId"
        >
          <li
            v-if="group.assemblyGroupKey"
            class="cart-page__assembly-group"
          >
            <p class="cart-page__assembly-group-label">
              自訂組裝
            </p>
            <p class="cart-page__assembly-group-hint">
              組裝品項不可單獨調整數量或移除；如需更換零件，請至「我的組裝清單」修改後重新加入購物車，或按下方「整組移除」清除這一整台。
            </p>
            <ul
              class="cart-page__assembly-group-items"
              :aria-label="`組裝品項：${group.assemblyGroupKey}`"
            >
              <CartLineItem
                v-for="item in group.items"
                :key="item.publicId"
                :item="item"
                :pending="isBusy"
                readonly
                :error="itemActionError?.itemPublicId === item.publicId ? itemActionError.message : null"
              />
            </ul>
            <div class="cart-page__assembly-group-actions">
              <button
                type="button"
                :disabled="isBusy"
                @click="onRemoveAssemblyGroup(group.assemblyGroupKey)"
              >
                整組移除
              </button>
            </div>
            <p
              v-if="groupActionError?.assemblyGroupKey === group.assemblyGroupKey"
              class="cart-page__assembly-group-error"
              role="alert"
            >
              {{ groupActionError.message }}
            </p>
          </li>
          <CartLineItem
            v-else
            :item="group.items[0]"
            :pending="isBusy"
            :error="itemActionError?.itemPublicId === group.items[0].publicId ? itemActionError.message : null"
            @change-quantity="(quantity) => onChangeQuantity(group.items[0].publicId, group.items[0].rowVersion, quantity)"
            @remove="onRemoveItem(group.items[0].publicId, group.items[0].rowVersion)"
          />
        </template>
      </ul>

      <ul
        v-if="cart.warnings.length > 0"
        class="cart-page__warnings"
        aria-label="購物車提醒"
      >
        <li
          v-for="(warning, index) in cart.warnings"
          :key="index"
        >
          {{ warning.message }}
        </li>
      </ul>

      <ul
        v-if="issues.length > 0"
        class="cart-page__issues"
        aria-label="需要處理的項目"
      >
        <li
          v-for="(issue, index) in issues"
          :key="index"
        >
          {{ describeIssue(issue) }}
          <template v-if="issue.availableActions.length > 0">
            （可{{ describeIssueActions(issue) }}）
          </template>
        </li>
      </ul>

      <section
        v-if="hasCartItems"
        class="cart-page__shipping"
        aria-labelledby="cart-shipping-title"
      >
        <h2 id="cart-shipping-title">
          配送方式
        </h2>
        <LoadingState
          v-if="isShippingPending"
          label="配送方式載入中"
        />
        <p
          v-else-if="isShippingError"
          class="cart-page__shipping-error"
        >
          配送方式暫時無法載入。
          <button
            type="button"
            @click="refetchShipping()"
          >
            重試
          </button>
        </p>
        <template v-else-if="shippingOptions">
          <ShippingOptionList :options="shippingOptions.options" />
          <p class="cart-page__shipping-note">
            運費以結帳時的最終計算為準；配送方式在結帳頁選擇。
          </p>
        </template>
      </section>

      <div class="cart-page__summary">
        <p class="cart-page__total">
          合計：{{ formatTwd(cart.amounts.totalEstimate) }}
        </p>
        <button
          type="button"
          class="cart-page__checkout"
          :disabled="!canCheckout"
          :title="!canCheckout ? '請先完成購物車檢查才能結帳' : undefined"
        >
          前往結帳（開發中）
        </button>
      </div>
    </div>
  </section>
</template>

<style scoped>
.cart-page {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  max-width: 48rem;
}

.cart-page__toolbar {
  display: flex;
  justify-content: flex-end;
}

.cart-page__items {
  list-style: none;
  margin: 0;
  padding: 0;
}

.cart-page__assembly-group {
  padding: 0.75rem;
  margin-block-end: 0.5rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
}

.cart-page__assembly-group-label {
  margin: 0 0 0.5rem;
  font-weight: 600;
  font-size: 0.875rem;
}

.cart-page__assembly-group-hint {
  margin: 0 0 0.5rem;
  color: #4b5563;
  font-size: 0.8125rem;
}

.cart-page__assembly-group-items {
  list-style: none;
  margin: 0;
  padding: 0;
}

.cart-page__assembly-group-actions {
  display: flex;
  justify-content: flex-end;
  margin-block-start: 0.5rem;
}

.cart-page__assembly-group-error {
  margin: 0.5rem 0 0;
  color: #991b1b;
  font-size: 0.875rem;
}

.cart-page__warnings {
  padding: 0.75rem 1rem;
  background: #fef3c7;
  border-radius: 0.5rem;
  color: #92400e;
}

.cart-page__issues {
  padding: 0.75rem 1rem;
  background: #fef3c7;
  border-radius: 0.5rem;
  color: #92400e;
}

.cart-page__revalidate-error,
.cart-page__identity-error {
  padding: 0.75rem 1rem;
  background: #fee2e2;
  border-radius: 0.5rem;
  color: #991b1b;
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.cart-page__identity-error p {
  margin: 0;
}

.cart-page__shipping {
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  padding: 1rem;
}

.cart-page__shipping h2 {
  margin: 0 0 0.75rem;
  font-size: 1rem;
}

.cart-page__shipping-note {
  margin: 0.75rem 0 0;
  color: #6b7280;
  font-size: 0.8125rem;
}

.cart-page__shipping-error {
  color: #b91c1c;
  margin: 0;
}

.cart-page__summary {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-top: 1rem;
  border-top: 1px solid #e5e7eb;
}

.cart-page__total {
  margin: 0;
  font-size: 1.25rem;
  font-weight: 700;
}

.cart-page__checkout:disabled {
  background: #9ca3af;
  border-color: #9ca3af;
  cursor: not-allowed;
}
</style>

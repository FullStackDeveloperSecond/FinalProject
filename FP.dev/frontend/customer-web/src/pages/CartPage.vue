<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, onMounted, ref } from 'vue'
import CartLineItem from '../features/cart/components/CartLineItem.vue'
import { useCart, useRemoveCartItem, useRevalidateCart, useUpdateCartItemQuantity } from '../features/cart/useCart'
import type { CartItemDto, CartIssueDto } from '../features/cart/types'

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
const revalidate = useRevalidateCart()

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

function describeItemActionError(caught: unknown): string {
  if (isApiError(caught) && caught.code === 'concurrency_conflict') {
    return '此品項已被更新，購物車已重新載入，請確認後再試一次。'
  }
  if (isApiError(caught)) {
    return `操作失敗（${caught.code}），請重試。`
  }
  return '操作失敗，請重試。'
}

function onItemActionError(itemPublicId: string, caught: unknown): void {
  itemActionError.value = { itemPublicId, message: describeItemActionError(caught) }
  if (isApiError(caught) && caught.code === 'concurrency_conflict') {
    void refetch()
  }
}

const issues = ref<CartIssueDto[]>([])
const isCheckoutReady = ref(false)
const hasRevalidated = ref(false)
const revalidateError = ref<unknown>(null)

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
    hasRevalidated.value = true
  } catch (caught) {
    revalidateError.value = caught
  } finally {
    if (needsAnotherRevalidate.value) {
      needsAnotherRevalidate.value = false
      void runRevalidate()
    }
  }
}

onMounted(() => {
  void runRevalidate()
})

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
      onError: (caught) => onItemActionError(itemPublicId, caught),
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
      onError: (caught) => onItemActionError(itemPublicId, caught),
    },
  )
}

const isMutating = computed(() => updateQuantity.isPending.value || removeItem.isPending.value)
const isBusy = computed(() => isMutating.value || revalidate.isPending.value)

function formatTwd(amount: number): string {
  return `NT$${amount.toLocaleString('zh-Hant-TW')}`
}

// Testability only: lets a test drive the coalescing behavior directly (a disabled button
// can't be clicked to simulate a trigger arriving mid-revalidate — the DOM itself suppresses
// click on disabled controls, which is also exactly the real UI guard this relies on).
defineExpose({ runRevalidate })
</script>

<template>
  <section aria-labelledby="cart-page-title">
    <h1 id="cart-page-title">
      購物車
    </h1>

    <LoadingState
      v-if="isPending"
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
            <ul
              class="cart-page__assembly-group-items"
              :aria-label="`組裝品項：${group.assemblyGroupKey}`"
            >
              <CartLineItem
                v-for="item in group.items"
                :key="item.publicId"
                :item="item"
                :pending="isBusy"
                :error="itemActionError?.itemPublicId === item.publicId ? itemActionError.message : null"
                @change-quantity="(quantity) => onChangeQuantity(item.publicId, item.rowVersion, quantity)"
                @remove="onRemoveItem(item.publicId, item.rowVersion)"
              />
            </ul>
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

      <div class="cart-page__summary">
        <p class="cart-page__total">
          合計：{{ formatTwd(cart.amounts.totalEstimate) }}
        </p>
        <button
          type="button"
          class="cart-page__checkout"
          :disabled="!hasRevalidated || !isCheckoutReady"
          :title="!isCheckoutReady ? '請先處理購物車內的問題才能結帳' : undefined"
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

.cart-page__assembly-group-items {
  list-style: none;
  margin: 0;
  padding: 0;
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

.cart-page__revalidate-error {
  padding: 0.75rem 1rem;
  background: #fee2e2;
  border-radius: 0.5rem;
  color: #991b1b;
  display: flex;
  align-items: center;
  gap: 0.75rem;
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

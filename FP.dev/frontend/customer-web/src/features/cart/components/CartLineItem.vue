<script setup lang="ts">
import { computed } from 'vue'
import type { CartItemDto } from '../types'

const props = defineProps<{
  item: CartItemDto
  pending: boolean
  error?: string | null
  /**
   * 組長 PR #29 round-6 review, P1: an assembly-group item is one SKU of one physical build —
   * every member shares the same AssemblyGroupKey, one NT$300 assembly fee, and (once checkout
   * exists) one AssemblyJob. Offering the same per-item quantity/remove controls a plain SKU gets
   * would let a shopper change one member's quantity or remove it alone, leaving the rest of the
   * group referring to a build that no longer matches what was actually configured — the backend
   * now rejects that (cart_assembly_item_immutable), but CartPage.vue passes this true for a
   * grouped item so the UI never offers the action in the first place, rather than only catching
   * it after a doomed request. There is no group-level operation to offer instead yet (no atomic
   * "swap this group's part" or "remove whole group" API) — read-only until one exists.
   */
  readonly?: boolean
}>()

const emit = defineEmits<{
  changeQuantity: [quantity: number]
  remove: []
}>()

const availabilityLabel = computed(() => ({
  available: null,
  unavailable: '已下架',
  insufficient_stock: '庫存不足',
} as Record<string, string | null>)[props.item.availability])

/**
 * 組長 PR #29 review round 3, P3: the quantity `<select>` used to offer
 * `1..Math.max(maxPurchasableQuantity, quantity)` — if stock dropped below the quantity already
 * in the cart (e.g. 10 in cart, only 2 now purchasable), every value from 1 up to the stale 10
 * was still offered as if legal, and the backend would accept a change to any of them even
 * though revalidate would immediately flag the cart as blocked again. Only 1..maxPurchasableQuantity
 * are ever legal now; the current (now-illegal) quantity is shown as a disabled marker option
 * instead of silently folded into the legal range.
 */
const legalQuantityOptions = computed(() => {
  const max = Number(props.item.maxPurchasableQuantity)
  return Array.from({ length: Math.max(max, 0) }, (_, index) => index + 1)
})

const isOutOfPurchasableStock = computed(() => Number(props.item.maxPurchasableQuantity) <= 0)

const quantityExceedsLimit = computed(() =>
  Number(props.item.quantity) > Number(props.item.maxPurchasableQuantity),
)

function onQuantityInput(event: Event): void {
  if (props.readonly) {
    return
  }
  const value = Number((event.target as HTMLSelectElement).value)
  if (Number.isFinite(value) && value >= 1) {
    emit('changeQuantity', value)
  }
}

function onRemoveClick(): void {
  if (props.readonly) {
    return
  }
  emit('remove')
}

function formatTwd(amount: number | string): string {
  return `NT$${Number(amount).toLocaleString('zh-Hant-TW')}`
}
</script>

<template>
  <li class="cart-line-item">
    <div class="cart-line-item__info">
      <p class="cart-line-item__name">
        {{ item.skuCode }} — {{ item.name }}
      </p>
      <p
        v-if="availabilityLabel"
        class="cart-line-item__badge"
        :class="`cart-line-item__badge--${item.availability}`"
      >
        {{ availabilityLabel }}
      </p>
      <p
        v-if="item.priceChanged"
        class="cart-line-item__badge cart-line-item__badge--price-changed"
      >
        價格已更新
      </p>
    </div>

    <div class="cart-line-item__price">
      {{ formatTwd(item.unitPrice) }}
    </div>

    <div class="cart-line-item__quantity-group">
      <span
        v-if="readonly"
        class="cart-line-item__quantity-readonly"
        aria-label="數量"
      >
        {{ item.quantity }}
      </span>
      <select
        v-else
        class="cart-line-item__quantity"
        :value="item.quantity"
        :disabled="pending || isOutOfPurchasableStock"
        aria-label="數量"
        @change="onQuantityInput"
      >
        <option
          v-if="quantityExceedsLimit"
          :value="item.quantity"
          disabled
        >
          {{ item.quantity }}（超過可購數量）
        </option>
        <option
          v-for="quantity in legalQuantityOptions"
          :key="quantity"
          :value="quantity"
        >
          {{ quantity }}
        </option>
      </select>
      <p
        v-if="isOutOfPurchasableStock"
        class="cart-line-item__out-of-stock-hint"
      >
        已無足夠庫存，請移除此品項。
      </p>
    </div>

    <div class="cart-line-item__line-total">
      {{ formatTwd(item.lineTotal) }}
    </div>

    <button
      v-if="!readonly"
      type="button"
      class="cart-line-item__remove"
      :disabled="pending"
      @click="onRemoveClick"
    >
      移除
    </button>

    <p
      v-if="error"
      class="cart-line-item__error"
    >
      {{ error }}
    </p>
  </li>
</template>

<style scoped>
.cart-line-item {
  display: grid;
  grid-template-columns: 1fr auto auto auto auto;
  align-items: center;
  gap: 1rem;
  padding: 1rem 0;
  border-bottom: 1px solid var(--color-border-soft);
}

.cart-line-item__info {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.cart-line-item__name {
  margin: 0;
  font-weight: 600;
}

.cart-line-item__badge {
  display: inline-block;
  width: fit-content;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  font-size: 0.75rem;
}

.cart-line-item__badge--unavailable,
.cart-line-item__badge--insufficient_stock {
  background: var(--color-danger-bg);
  color: var(--color-danger);
}

.cart-line-item__badge--price-changed {
  background: var(--color-warning-bg);
  color: var(--color-warning);
}

.cart-line-item__price,
.cart-line-item__line-total {
  font-variant-numeric: tabular-nums;
}

.cart-line-item__quantity-group {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.cart-line-item__quantity {
  min-height: 2.5rem;
  padding: 0.25rem 0.5rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  font: inherit;
}

.cart-line-item__quantity-readonly {
  min-height: 2.5rem;
  display: inline-flex;
  align-items: center;
  padding: 0.25rem 0.5rem;
  font-variant-numeric: tabular-nums;
}

.cart-line-item__out-of-stock-hint {
  margin: 0;
  color: var(--color-danger);
  font-size: 0.75rem;
}

.cart-line-item__remove {
  background: none;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  padding: 0.5rem 0.75rem;
  cursor: pointer;
}

.cart-line-item__remove:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

.cart-line-item__error {
  grid-column: 1 / -1;
  margin: 0;
  color: var(--color-danger);
  font-size: 0.875rem;
}
</style>

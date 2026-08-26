<script setup lang="ts">
import { computed } from 'vue'
import type { CartItemDto } from '../types'

const props = defineProps<{
  item: CartItemDto
  pending: boolean
  error?: string | null
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

function onQuantityInput(event: Event): void {
  const value = Number((event.target as HTMLSelectElement).value)
  if (Number.isFinite(value) && value >= 1) {
    emit('changeQuantity', value)
  }
}

function formatTwd(amount: number): string {
  return `NT$${amount.toLocaleString('zh-Hant-TW')}`
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

    <select
      class="cart-line-item__quantity"
      :value="item.quantity"
      :disabled="pending"
      aria-label="數量"
      @change="onQuantityInput"
    >
      <option
        v-for="quantity in Math.max(item.maxPurchasableQuantity, item.quantity)"
        :key="quantity"
        :value="quantity"
      >
        {{ quantity }}
      </option>
    </select>

    <div class="cart-line-item__line-total">
      {{ formatTwd(item.lineTotal) }}
    </div>

    <button
      type="button"
      class="cart-line-item__remove"
      :disabled="pending"
      @click="emit('remove')"
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
  border-bottom: 1px solid #e5e7eb;
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
  background: #fee2e2;
  color: #991b1b;
}

.cart-line-item__badge--price-changed {
  background: #fef3c7;
  color: #92400e;
}

.cart-line-item__price,
.cart-line-item__line-total {
  font-variant-numeric: tabular-nums;
}

.cart-line-item__quantity {
  min-height: 2.5rem;
  padding: 0.25rem 0.5rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.cart-line-item__remove {
  background: none;
  border: 1px solid #d1d5db;
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
  color: #b91c1c;
  font-size: 0.875rem;
}
</style>

<script setup lang="ts">
import { computed } from 'vue'
import type { ProductCardDto } from '../types'

const props = defineProps<{
  product: ProductCardDto
}>()

const availabilityLabel = computed(() => ({
  inStock: '現貨供應',
  lowStock: '庫存有限',
  outOfStock: '缺貨中',
}[props.product.availability] ?? props.product.availability))

const formattedPrice = computed(() => formatTwd(props.product.price.sale ?? props.product.price.list))
const formattedListPrice = computed(() => formatTwd(props.product.price.list))
const hasSale = computed(() =>
  props.product.price.sale != null &&
  Number(props.product.price.sale) < Number(props.product.price.list))

function formatTwd(amount: number | string): string {
  return `NT$${Number(amount).toLocaleString('zh-Hant-TW')}`
}
</script>

<template>
  <RouterLink
    class="product-card"
    :to="{ name: 'product-detail', params: { productId: product.productPublicId } }"
  >
    <div
      class="product-card__image"
      aria-hidden="true"
    >
      <img
        v-if="product.primaryImage"
        :src="product.primaryImage.url"
        :alt="product.primaryImage.alt"
      >
      <span
        v-else
        class="product-card__image-placeholder"
      >尚無商品圖片</span>
    </div>
    <p class="product-card__brand">
      {{ product.brand.name }}
    </p>
    <h3 class="product-card__name">
      {{ product.name }}
    </h3>
    <p class="product-card__price">
      <span class="product-card__price-current">{{ formattedPrice }}</span>
      <span
        v-if="hasSale"
        class="product-card__price-original"
      >{{ formattedListPrice }}</span>
    </p>
    <span
      class="product-card__availability"
      :class="`product-card__availability--${product.availability}`"
    >
      {{ availabilityLabel }}
    </span>
  </RouterLink>
</template>

<style scoped>
/* 顏色、間距、圓角一律取自 design-tokens.css 的語意 token，不寫死色碼。 */
.product-card {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  padding: var(--space-4);
  border: 1px solid var(--color-border-soft);
  border-radius: var(--radius-lg);
  color: inherit;
  text-decoration: none;
  background: var(--color-surface);
}

.product-card:hover {
  border-color: var(--color-primary);
  box-shadow: var(--shadow-md);
}

.product-card__image {
  display: flex;
  align-items: center;
  justify-content: center;
  aspect-ratio: 4 / 3;
  border-radius: var(--radius-md);
  background: var(--color-surface-strong);
  overflow: hidden;
}

.product-card__image img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.product-card__image-placeholder {
  color: var(--color-text-faint);
  font-size: var(--fs-caption);
}

.product-card__brand {
  margin: 0;
  color: var(--color-text-muted);
  font-size: var(--fs-caption);
}

.product-card__name {
  margin: 0;
  font-size: var(--fs-body);
  color: var(--color-text);
}

.product-card__price {
  margin: 0;
  display: flex;
  align-items: baseline;
  gap: var(--space-2);
}

.product-card__price-current {
  font-weight: 700;
  font-size: var(--fs-h3);
  color: var(--color-primary-dark);
  font-variant-numeric: tabular-nums;
}

.product-card__price-original {
  color: var(--color-text-faint);
  text-decoration: line-through;
  font-size: var(--fs-caption);
  font-variant-numeric: tabular-nums;
}

.product-card__availability {
  align-self: flex-start;
  padding: 1px var(--space-3);
  border: 1px solid transparent;
  border-radius: 999px;
  font-size: var(--fs-caption);
  font-weight: 600;
}

.product-card__availability--inStock {
  background: var(--color-success-bg);
  border-color: var(--color-success-border);
  color: var(--color-primary-dark);
}

.product-card__availability--lowStock {
  background: var(--color-butter-soft);
  border-color: var(--color-butter-line);
  color: var(--color-navy);
}

.product-card__availability--outOfStock {
  background: var(--color-danger-bg);
  border-color: var(--color-danger-border);
  color: var(--color-danger);
}
</style>

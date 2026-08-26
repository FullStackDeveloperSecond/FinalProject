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
.product-card {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.75rem;
  color: inherit;
  text-decoration: none;
  background: #fff;
}

.product-card:hover {
  border-color: #93c5fd;
}

.product-card__image {
  display: flex;
  align-items: center;
  justify-content: center;
  aspect-ratio: 4 / 3;
  border-radius: 0.5rem;
  background: #f3f4f6;
  overflow: hidden;
}

.product-card__image img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.product-card__image-placeholder {
  color: #9ca3af;
  font-size: 0.875rem;
}

.product-card__brand {
  margin: 0;
  color: #6b7280;
  font-size: 0.75rem;
}

.product-card__name {
  margin: 0;
  font-size: 1rem;
}

.product-card__price {
  margin: 0;
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
}

.product-card__price-current {
  font-weight: 700;
  font-size: 1.125rem;
}

.product-card__price-original {
  color: #9ca3af;
  text-decoration: line-through;
  font-size: 0.875rem;
}

.product-card__availability {
  align-self: flex-start;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  font-size: 0.75rem;
}

.product-card__availability--inStock {
  background: #dcfce7;
  color: #166534;
}

.product-card__availability--lowStock {
  background: #fef3c7;
  color: #92400e;
}

.product-card__availability--outOfStock {
  background: #fee2e2;
  color: #991b1b;
}
</style>

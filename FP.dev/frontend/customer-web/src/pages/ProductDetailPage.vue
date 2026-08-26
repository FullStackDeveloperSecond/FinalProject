<script setup lang="ts">
import { ErrorState, HttpStatusPage, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useProductDetail } from '../features/catalog/useProductSearch'
import type { PublicSkuDto } from '../features/catalog/types'
import { useAddCartItem } from '../features/cart/useCart'

const route = useRoute()
const productPublicId = computed(() => route.params.productId as string)

const { data: product, isPending, isError, error, refetch } = useProductDetail(productPublicId)

const selectedSkuPublicId = ref<string>()
const selectedSku = computed<PublicSkuDto | undefined>(() =>
  product.value?.skus.find((sku) => sku.publicId === selectedSkuPublicId.value) ?? product.value?.skus[0],
)

// 組長 PR #29 review round 3, P1: the "加入購物車" button was permanently disabled with a
// "coming soon" label — useAddCartItem() existed but nothing on this page ever called it, so the
// primary product -> cart flow (UC-CART-01) didn't actually work for any shopper.
const addCartItemMutation = useAddCartItem()
const addToCartError = ref<string | null>(null)
const addToCartSucceeded = ref(false)

const ADD_TO_CART_ERROR_MESSAGES: Record<string, string> = {
  sku_unavailable: '此規格已下架，請選擇其他規格。',
  cart_quantity_exceeded: '已超過此規格可購買的數量上限。',
  cart_item_limit_exceeded: '購物車已達 100 件上限，請先清空部分品項。',
  resource_not_found: '找不到此規格，可能已被下架。',
}

function describeAddToCartError(caught: unknown): string {
  if (isApiError(caught)) {
    return ADD_TO_CART_ERROR_MESSAGES[caught.code] ?? `加入購物車失敗（${caught.code}），請重試。`
  }
  return '加入購物車失敗，請重試。'
}

const isAddToCartDisabled = computed(() => {
  if (!selectedSku.value || addCartItemMutation.isPending.value) {
    return true
  }
  return selectedSku.value.availability === 'outOfStock' || Number(selectedSku.value.maxPurchasableQuantity) <= 0
})

function onAddToCart(): void {
  if (isAddToCartDisabled.value || !selectedSku.value) {
    return
  }

  addToCartError.value = null
  addToCartSucceeded.value = false
  addCartItemMutation.mutate(
    { skuPublicId: selectedSku.value.publicId, quantity: 1, cartRowVersion: null },
    {
      onSuccess: () => { addToCartSucceeded.value = true },
      onError: (caught) => { addToCartError.value = describeAddToCartError(caught) },
    },
  )
}

// Switching to a different SKU makes a stale success/error message from the previous SKU
// misleading (it looks like it applies to the newly-selected one).
watch(selectedSkuPublicId, () => {
  addToCartError.value = null
  addToCartSucceeded.value = false
})

/**
 * PR #24 review round 10 (P3): `selectedSkuPublicId` was never reset when the product data
 * changed. Vue Router reuses this component instance across a param-only navigation on the same
 * route record (/products/A -> /products/B) — after picking a non-default SKU on A and then
 * navigating to B, `selectedSku` above already falls back to `product.skus[0]` correctly (since
 * `.find()` returns undefined for a publicId that isn't in B's list), but the `<select>` element
 * itself stays bound to A's stale publicId via v-model, which doesn't match any of B's `<option>`
 * values — the dropdown can render blank even though the price/spec content below it is already
 * showing B's first SKU, an inconsistent-looking mix. Reset explicitly to the new product's
 * default SKU (or its first SKU, if for some reason none is marked default) whenever the loaded
 * product changes and the current selection doesn't belong to it.
 */
watch(product, (value) => {
  if (!value) {
    return
  }
  if (value.skus.some((sku) => sku.publicId === selectedSkuPublicId.value)) {
    return
  }
  selectedSkuPublicId.value = value.skus.find((sku) => sku.isDefault)?.publicId ?? value.skus[0]?.publicId
})

const availabilityLabel = computed(() => {
  const availability = selectedSku.value?.availability
  return ({
    inStock: '現貨供應',
    lowStock: '庫存有限',
    outOfStock: '缺貨中',
  } as Record<string, string>)[availability ?? ''] ?? availability
})

function formatTwd(amount: number | string): string {
  return `NT$${Number(amount).toLocaleString('zh-Hant-TW')}`
}

const isNotFound = computed(() => isApiError(error.value) && error.value.status === 404)
</script>

<template>
  <LoadingState
    v-if="isPending"
    label="商品載入中"
  />
  <HttpStatusPage
    v-else-if="isNotFound"
    :status="404"
    home-href="/products"
  />
  <ErrorState
    v-else-if="isError"
    :correlation-id="(error as { correlationId?: string })?.correlationId"
    @retry="refetch"
  />
  <article
    v-else-if="product"
    class="product-detail"
  >
    <nav aria-label="麵包屑">
      <RouterLink to="/products">
        ← 回商品列表
      </RouterLink>
    </nav>

    <header class="product-detail__header">
      <p class="product-detail__brand">
        {{ product.brand.name }} · {{ product.category.name }}
      </p>
      <h1>{{ product.name }}</h1>
      <p
        v-if="product.badges.includes('featured')"
        class="product-detail__badge"
      >
        精選商品
      </p>
    </header>

    <ul
      v-if="product.images.length > 0"
      class="product-detail__gallery"
      aria-label="商品圖片"
    >
      <li
        v-for="image in product.images"
        :key="image.url"
      >
        <img
          :src="image.url"
          :alt="image.alt"
          :width="Number(image.width)"
          :height="Number(image.height)"
          loading="lazy"
        >
      </li>
    </ul>

    <section
      v-if="selectedSku"
      class="product-detail__purchase"
      aria-label="購買資訊"
    >
      <p class="product-detail__price">
        <span class="product-detail__price-current">
          {{ formatTwd(selectedSku.price.sale ?? selectedSku.price.list) }}
        </span>
        <span
          v-if="selectedSku.price.sale != null && Number(selectedSku.price.sale) < Number(selectedSku.price.list)"
          class="product-detail__price-original"
        >
          {{ formatTwd(selectedSku.price.list) }}
        </span>
      </p>
      <p
        class="product-detail__availability"
        :class="`product-detail__availability--${selectedSku.availability}`"
      >
        {{ availabilityLabel }}
      </p>
      <p
        v-if="product.warrantyMonths != null"
        class="product-detail__warranty"
      >
        保固 {{ product.warrantyMonths }} 個月
      </p>

      <div
        v-if="product.skus.length > 1"
        class="product-detail__sku-select"
      >
        <label for="sku-select">規格</label>
        <select
          id="sku-select"
          v-model="selectedSkuPublicId"
        >
          <option
            v-for="sku in product.skus"
            :key="sku.publicId"
            :value="sku.publicId"
          >
            {{ sku.name }} — {{ formatTwd(sku.price.sale ?? sku.price.list) }}
          </option>
        </select>
      </div>

      <button
        type="button"
        :disabled="isAddToCartDisabled"
        @click="onAddToCart"
      >
        {{ addCartItemMutation.isPending.value ? '加入中…' : '加入購物車' }}
      </button>
      <p
        v-if="addToCartSucceeded"
        class="product-detail__add-to-cart-success"
      >
        已加入購物車。
        <RouterLink to="/cart">
          前往購物車
        </RouterLink>
      </p>
      <p
        v-if="addToCartError"
        class="product-detail__add-to-cart-error"
      >
        {{ addToCartError }}
      </p>
    </section>

    <section
      v-if="product.description"
      aria-labelledby="product-description-title"
    >
      <h2 id="product-description-title">
        商品說明
      </h2>
      <p>{{ product.description }}</p>
    </section>

    <section
      v-if="selectedSku && selectedSku.specifications.length > 0"
      aria-labelledby="product-specs-title"
    >
      <h2 id="product-specs-title">
        規格
      </h2>
      <dl class="product-detail__specs">
        <template
          v-for="spec in selectedSku.specifications"
          :key="spec.semanticKey"
        >
          <dt>{{ spec.label }}</dt>
          <dd>{{ spec.value }}{{ spec.unit ? ` ${spec.unit}` : '' }}</dd>
        </template>
      </dl>
    </section>

    <section
      v-if="product.shippingRestrictions.length > 0"
      aria-labelledby="product-shipping-title"
    >
      <h2 id="product-shipping-title">
        配送限制
      </h2>
      <ul class="product-detail__shipping">
        <li
          v-for="restriction in product.shippingRestrictions"
          :key="restriction.method"
          :class="restriction.allowed ? 'product-detail__shipping-item--allowed' : 'product-detail__shipping-item--blocked'"
        >
          {{ restriction.method }} — {{ restriction.allowed ? '可配送' : '不可配送' }}
          <span v-if="restriction.reasonCode">（{{ restriction.reasonCode }}）</span>
        </li>
      </ul>
    </section>

    <section
      v-if="product.tags.length > 0"
      aria-labelledby="product-tags-title"
    >
      <h2 id="product-tags-title">
        標籤
      </h2>
      <ul class="product-detail__tags">
        <li
          v-for="tag in product.tags"
          :key="tag.code"
        >
          {{ tag.name }}
        </li>
      </ul>
    </section>
  </article>
</template>

<style scoped>
.product-detail {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  max-width: 48rem;
}

.product-detail__header h1 {
  margin: 0.25rem 0;
}

.product-detail__brand {
  margin: 0;
  color: #6b7280;
  font-size: 0.875rem;
}

.product-detail__badge {
  display: inline-block;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  background: #dbeafe;
  color: #1e40af;
  font-size: 0.75rem;
}

.product-detail__gallery {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  padding: 0;
  list-style: none;
}

.product-detail__gallery img {
  max-width: 12rem;
  height: auto;
  border-radius: 0.5rem;
  border: 1px solid #e5e7eb;
}

.product-detail__warranty {
  margin: 0;
  color: #6b7280;
  font-size: 0.875rem;
}

.product-detail__shipping {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  padding: 0;
  list-style: none;
  font-size: 0.875rem;
}

.product-detail__shipping-item--blocked {
  color: #b91c1c;
}

.product-detail__purchase {
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  align-items: flex-start;
}

.product-detail__price {
  margin: 0;
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
}

.product-detail__price-current {
  font-weight: 700;
  font-size: 1.5rem;
}

.product-detail__price-original {
  color: #9ca3af;
  text-decoration: line-through;
}

.product-detail__availability {
  margin: 0;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  font-size: 0.75rem;
  display: inline-block;
}

.product-detail__availability--inStock {
  background: #dcfce7;
  color: #166534;
}

.product-detail__availability--lowStock {
  background: #fef3c7;
  color: #92400e;
}

.product-detail__availability--outOfStock {
  background: #fee2e2;
  color: #991b1b;
}

.product-detail__sku-select {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.product-detail__sku-select select {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

button[disabled] {
  background: #9ca3af;
  border-color: #9ca3af;
  cursor: not-allowed;
}

.product-detail__add-to-cart-success {
  margin: 0;
  color: #166534;
  font-size: 0.875rem;
}

.product-detail__add-to-cart-error {
  margin: 0;
  color: #b91c1c;
  font-size: 0.875rem;
}

.product-detail__specs {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.375rem 1rem;
}

.product-detail__specs dt {
  color: #6b7280;
}

.product-detail__specs dd {
  margin: 0;
}

.product-detail__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  padding: 0;
  list-style: none;
}

.product-detail__tags li {
  padding: 0.125rem 0.625rem;
  border-radius: 999px;
  background: #f3f4f6;
  font-size: 0.75rem;
}
</style>

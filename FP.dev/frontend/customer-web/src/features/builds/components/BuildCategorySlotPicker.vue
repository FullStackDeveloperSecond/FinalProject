<script setup lang="ts">
/**
 * 組長 PR #35 review, item 1: replaces the raw "SKU PublicId (GUID)" text box with a real search
 * picker scoped to one build-component category, reusing `features/catalog`'s public search
 * endpoint (available now that catalog-frontend, PR #24, is merged). Debounces keystrokes the
 * same way a typeahead should — searching on every keystroke would spam the public endpoint.
 */
import { ref, watch } from 'vue'
import { getProductDetail, searchProducts } from '../../catalog/api'
import type { ProductCardDto, PublicSkuDto } from '../../catalog/types'

const props = defineProps<{
  categoryCode: string
  disabled?: boolean
  inStockOnly?: boolean
}>()

const emit = defineEmits<{
  select: [item: { skuPublicId: string, skuCode: string, name: string }]
}>()

const query = ref('')
const results = ref<ProductCardDto[]>([])
const isSearching = ref(false)
const searchError = ref<unknown>(null)
const isOpen = ref(false)

/**
 * 組長 PR #35 round-2 review, P1-3: picking a search result used to emit `product.defaultSkuPublicId`
 * directly — a product with multiple sellable SKUs (variants) had every SKU but the default one
 * permanently unreachable through this picker. Picking a product now loads its real SKU list via
 * the same product-detail endpoint ProductDetailPage.vue already uses, and only emits `select`
 * once a specific SKU is chosen from it — a product pick is not a SKU pick. A product with exactly
 * one SKU skips the extra list-then-click (there is no real choice to present) and resolves
 * immediately, same as before for that case.
 */
const activeProduct = ref<{ name: string, skus: PublicSkuDto[] } | null>(null)
const isLoadingSkus = ref(false)
const skuLoadError = ref<unknown>(null)

let debounceHandle: ReturnType<typeof setTimeout> | undefined
let searchToken = 0

// 組長 PR #35 round-2 review, P2-9: the token used to increment only once runSearch itself
// started (inside the 300ms debounce), so clearing or changing the query while a *previous*
// search was still in flight only cancelled the not-yet-fired debounce timer, not that in-flight
// request — its response would still see `token === searchToken` (nothing had bumped it yet) and
// re-open results for a query the input no longer shows. Bumping the token on every query change,
// synchronously, invalidates any request already in flight the instant the input changes, not
// just future ones.
//
// 組長 PR #35 round-6 review, P2-1: bumping the token alone left `isSearching`/`isLoadingSkus`
// stuck true whenever the invalidated request was the *only* thing that would have cleared them.
// `runSearch`'s/`pickProduct`'s own `finally` only resets loading `if (token === searchToken)` —
// once the token has moved on, that guard permanently skips the reset for that stale call. That
// self-heals as long as a *new* request eventually settles and clears the (now-current) loading
// flag itself — but clearing the query takes the early-return branch below, which never fires a
// new request at all, so nothing was left to ever flip loading back to false. Resetting both flags
// synchronously here, the moment the query changes, means the picker is never left mid-load for a
// request that no longer has anywhere to report back to.
//
// Self-review finding (same root cause as P2-1): `searchError` had the identical gap —
// `runSearch` only clears it at the *start* of a new attempt, so clearing the query took the same
// early-return branch and could leave "搜尋失敗，請重試。" showing over an empty, untouched input
// indefinitely. `skuLoadError` was already reset here; `searchError` belongs in the same reset for
// the same reason.
watch(query, (value) => {
  if (debounceHandle) {
    clearTimeout(debounceHandle)
  }
  searchToken += 1
  activeProduct.value = null
  searchError.value = null
  skuLoadError.value = null
  isSearching.value = false
  isLoadingSkus.value = false
  if (!value.trim()) {
    results.value = []
    isOpen.value = false
    return
  }
  const token = searchToken
  debounceHandle = setTimeout(() => { void runSearch(value, token) }, 300)
})

async function runSearch(value: string, token: number): Promise<void> {
  isSearching.value = true
  searchError.value = null
  try {
    const page = await searchProducts({
      q: value, category: props.categoryCode, inStock: props.inStockOnly ?? true, pageSize: 10,
    })
    // A later search that started after this one but resolved first must win — discard this
    // response if a newer search has since been kicked off, or if the query has since changed to
    // something this response doesn't answer (belt-and-suspenders alongside the token check).
    if (token !== searchToken || value !== query.value) {
      return
    }
    results.value = page.items
    isOpen.value = true
  } catch (caught) {
    if (token === searchToken) {
      searchError.value = caught
    }
  } finally {
    if (token === searchToken) {
      isSearching.value = false
    }
  }
}

async function pickProduct(product: ProductCardDto): Promise<void> {
  const token = searchToken
  skuLoadError.value = null
  isLoadingSkus.value = true
  try {
    const detail = await getProductDetail(product.productPublicId)
    // The shopper may have changed the search query while this was in flight — a stale SKU list
    // for a product they're no longer looking at must not suddenly appear.
    if (token !== searchToken) {
      return
    }
    if (detail.skus.length === 1) {
      pickSku(detail.skus[0]!)
      return
    }
    activeProduct.value = { name: detail.name, skus: detail.skus }
  } catch (caught) {
    if (token === searchToken) {
      skuLoadError.value = caught
    }
  } finally {
    if (token === searchToken) {
      isLoadingSkus.value = false
    }
  }
}

function pickSku(sku: PublicSkuDto): void {
  emit('select', { skuPublicId: sku.publicId, skuCode: sku.skuCode, name: sku.name })
  query.value = ''
  results.value = []
  activeProduct.value = null
  isOpen.value = false
}

function cancelSkuSelection(): void {
  activeProduct.value = null
}
</script>

<template>
  <div class="slot-picker">
    <input
      v-model="query"
      type="search"
      placeholder="搜尋商品名稱或代碼"
      :aria-label="`搜尋${categoryCode}商品`"
      :disabled="disabled"
      @focus="() => { if (results.length > 0) isOpen = true }"
    >
    <p
      v-if="isSearching || isLoadingSkus"
      class="slot-picker__status"
    >
      {{ isLoadingSkus ? '載入規格中…' : '搜尋中…' }}
    </p>
    <p
      v-else-if="searchError"
      class="slot-picker__status slot-picker__status--error"
    >
      搜尋失敗，請重試。
    </p>
    <p
      v-else-if="skuLoadError"
      class="slot-picker__status slot-picker__status--error"
    >
      載入規格失敗，請重試。
    </p>
    <ul
      v-if="activeProduct"
      class="slot-picker__results"
      :aria-label="`${activeProduct.name} 的規格`"
    >
      <li>
        <button
          type="button"
          class="slot-picker__back"
          @click="cancelSkuSelection"
        >
          ← 返回商品清單
        </button>
      </li>
      <li
        v-for="sku in activeProduct.skus"
        :key="sku.publicId"
      >
        <button
          type="button"
          :disabled="disabled"
          @click="pickSku(sku)"
        >
          <span class="slot-picker__result-name">{{ sku.name }}</span>
          <span class="slot-picker__result-code">{{ sku.skuCode }}</span>
          <span class="slot-picker__result-price">NT${{ sku.price.sale ?? sku.price.list }}</span>
        </button>
      </li>
    </ul>
    <ul
      v-else-if="isOpen && results.length > 0"
      class="slot-picker__results"
    >
      <li
        v-for="product in results"
        :key="product.defaultSkuPublicId"
      >
        <button
          type="button"
          :disabled="disabled || isLoadingSkus"
          @click="pickProduct(product)"
        >
          <span class="slot-picker__result-name">{{ product.name }}</span>
          <span class="slot-picker__result-code">{{ product.skuCode }}</span>
          <span class="slot-picker__result-price">NT${{ product.price.sale ?? product.price.list }}</span>
        </button>
      </li>
    </ul>
    <p
      v-else-if="isOpen && !isSearching"
      class="slot-picker__status"
    >
      找不到符合的商品。
    </p>
  </div>
</template>

<style scoped>
.slot-picker {
  position: relative;
  min-width: 14rem;
}

.slot-picker input {
  width: 100%;
  min-height: 2.5rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  font: inherit;
}

.slot-picker__status {
  margin: 0.25rem 0 0;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.slot-picker__back {
  color: var(--color-text-muted);
  font-size: 0.8125rem;
}

.slot-picker__status--error {
  color: var(--color-danger);
}

.slot-picker__results {
  position: absolute;
  z-index: 10;
  inset-inline: 0;
  margin: 0.25rem 0 0;
  padding: 0.25rem;
  list-style: none;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-md);
  max-height: 16rem;
  overflow-y: auto;
}

.slot-picker__results button {
  display: flex;
  flex-direction: column;
  width: 100%;
  padding: 0.5rem;
  border: none;
  border-radius: 0.375rem;
  background: transparent;
  text-align: left;
  cursor: pointer;
  font: inherit;
}

.slot-picker__results button:hover,
.slot-picker__results button:focus-visible {
  background: var(--color-surface-strong);
}

.slot-picker__result-name {
  font-weight: 600;
}

.slot-picker__result-code {
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 0.75rem;
  color: var(--color-text-muted);
}

.slot-picker__result-price {
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}
</style>

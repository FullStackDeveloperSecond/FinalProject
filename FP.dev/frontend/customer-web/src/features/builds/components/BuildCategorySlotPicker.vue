<script setup lang="ts">
/**
 * 組長 PR #35 review, item 1: replaces the raw "SKU PublicId (GUID)" text box with a real search
 * picker scoped to one build-component category, reusing `features/catalog`'s public search
 * endpoint (available now that catalog-frontend, PR #24, is merged). Debounces keystrokes the
 * same way a typeahead should — searching on every keystroke would spam the public endpoint.
 */
import { ref, watch } from 'vue'
import { searchProducts } from '../../catalog/api'
import type { ProductCardDto } from '../../catalog/types'

const props = defineProps<{
  categoryCode: string
  disabled?: boolean
}>()

const emit = defineEmits<{
  select: [item: { skuPublicId: string, skuCode: string, name: string }]
}>()

const query = ref('')
const results = ref<ProductCardDto[]>([])
const isSearching = ref(false)
const searchError = ref<unknown>(null)
const isOpen = ref(false)

let debounceHandle: ReturnType<typeof setTimeout> | undefined
let searchToken = 0

watch(query, (value) => {
  if (debounceHandle) {
    clearTimeout(debounceHandle)
  }
  if (!value.trim()) {
    results.value = []
    isOpen.value = false
    return
  }
  debounceHandle = setTimeout(() => { void runSearch(value) }, 300)
})

async function runSearch(value: string): Promise<void> {
  const token = ++searchToken
  isSearching.value = true
  searchError.value = null
  try {
    const page = await searchProducts({
      q: value, category: props.categoryCode, inStock: true, pageSize: 10,
    })
    // A later search that started after this one but resolved first must win — discard this
    // response if a newer search has since been kicked off (same stale-response hazard as any
    // other race between two overlapping requests keyed by nothing but arrival order).
    if (token !== searchToken) {
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

function pick(product: ProductCardDto): void {
  emit('select', { skuPublicId: product.defaultSkuPublicId, skuCode: product.skuCode, name: product.name })
  query.value = ''
  results.value = []
  isOpen.value = false
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
      v-if="isSearching"
      class="slot-picker__status"
    >
      搜尋中…
    </p>
    <p
      v-else-if="searchError"
      class="slot-picker__status slot-picker__status--error"
    >
      搜尋失敗，請重試。
    </p>
    <ul
      v-if="isOpen && results.length > 0"
      class="slot-picker__results"
    >
      <li
        v-for="product in results"
        :key="product.defaultSkuPublicId"
      >
        <button
          type="button"
          :disabled="disabled"
          @click="pick(product)"
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
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.slot-picker__status {
  margin: 0.25rem 0 0;
  font-size: 0.8125rem;
  color: #4b5563;
}

.slot-picker__status--error {
  color: #b91c1c;
}

.slot-picker__results {
  position: absolute;
  z-index: 10;
  inset-inline: 0;
  margin: 0.25rem 0 0;
  padding: 0.25rem;
  list-style: none;
  background: white;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  box-shadow: 0 4px 12px rgb(0 0 0 / 10%);
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
  background: #f3f4f6;
}

.slot-picker__result-name {
  font-weight: 600;
}

.slot-picker__result-code {
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 0.75rem;
  color: #6b7280;
}

.slot-picker__result-price {
  font-size: 0.8125rem;
  color: #4b5563;
}
</style>

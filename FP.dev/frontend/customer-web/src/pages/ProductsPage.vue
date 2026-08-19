<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { computed, reactive, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ProductCard from '../features/catalog/components/ProductCard.vue'
import { useCatalogFilterOptions, useProductSearch } from '../features/catalog/useProductSearch'

const route = useRoute()
const router = useRouter()

const filters = reactive({
  q: readQueryString('q'),
  category: readQueryString('category'),
  brand: readQueryString('brand'),
  inStock: route.query.inStock === 'true',
  sort: readQueryString('sort') ?? 'relevance',
})
const pageNumber = computed(() => Number(readQueryString('page') ?? '1') || 1)

const searchParams = computed(() => ({
  q: filters.q || undefined,
  category: filters.category || undefined,
  brand: filters.brand || undefined,
  inStock: filters.inStock || undefined,
  sort: filters.sort,
  pageNumber: pageNumber.value,
  pageSize: 20,
}))

const { data: result, isPending, isError, error, refetch } = useProductSearch(searchParams)
const { data: filterOptions } = useCatalogFilterOptions(() => filters.category)

const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

function readQueryString(key: string): string | undefined {
  const value = route.query[key]
  return typeof value === 'string' && value.length > 0 ? value : undefined
}

function applyFilters() {
  router.push({
    query: {
      ...(filters.q ? { q: filters.q } : {}),
      ...(filters.category ? { category: filters.category } : {}),
      ...(filters.brand ? { brand: filters.brand } : {}),
      ...(filters.inStock ? { inStock: 'true' } : {}),
      ...(filters.sort !== 'relevance' ? { sort: filters.sort } : {}),
    },
  })
}

function goToPage(nextPage: number) {
  router.push({ query: { ...route.query, page: String(nextPage) } })
}

// Route query is the shared source of truth for filters (per the
// M功能桌面UI與Route規格 "可分享列表條件" rule), so browser back/forward
// and shared links must resynchronize the local form state too.
watch(
  () => route.query,
  () => {
    filters.q = readQueryString('q')
    filters.category = readQueryString('category')
    filters.brand = readQueryString('brand')
    filters.inStock = route.query.inStock === 'true'
    filters.sort = readQueryString('sort') ?? 'relevance'
  },
)
</script>

<template>
  <section aria-labelledby="products-page-title">
    <h1 id="products-page-title">
      商品搜尋
    </h1>

    <form
      class="products-filters"
      aria-label="商品篩選"
      @submit.prevent="applyFilters"
    >
      <input
        v-model="filters.q"
        type="search"
        placeholder="搜尋商品名稱或代碼"
        aria-label="關鍵字"
      >
      <select
        v-model="filters.category"
        aria-label="分類"
      >
        <option value="">
          全部分類
        </option>
        <option
          v-for="category in filterOptions?.categories ?? []"
          :key="category.publicId"
          :value="category.code"
        >
          {{ category.name }}
        </option>
      </select>
      <select
        v-model="filters.brand"
        aria-label="品牌"
      >
        <option value="">
          全部品牌
        </option>
        <option
          v-for="brand in filterOptions?.brands ?? []"
          :key="brand.publicId"
          :value="brand.code"
        >
          {{ brand.name }}
        </option>
      </select>
      <label class="products-filters__checkbox">
        <input
          v-model="filters.inStock"
          type="checkbox"
        >
        只顯示現貨
      </label>
      <select
        v-model="filters.sort"
        aria-label="排序方式"
      >
        <option
          v-for="option in filterOptions?.sortOptions ?? ['relevance', 'priceAsc', 'priceDesc', 'newest']"
          :key="option"
          :value="option"
        >
          {{ { relevance: '相關度', priceAsc: '價格由低到高', priceDesc: '價格由高到低', newest: '最新上架' }[option] ?? option }}
        </option>
      </select>
      <button type="submit">
        套用篩選
      </button>
    </form>

    <LoadingState
      v-if="isPending"
      label="商品載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="(error as { correlationId?: string })?.correlationId"
      @retry="refetch"
    />
    <EmptyState
      v-else-if="result && result.items.length === 0"
      title="沒有符合條件的商品"
      description="調整關鍵字或篩選條件後再試一次。"
    />
    <template v-else-if="result">
      <p class="products-summary">
        共 {{ result.totalCount }} 項商品
      </p>
      <div class="products-grid">
        <ProductCard
          v-for="product in result.items"
          :key="product.defaultSkuPublicId"
          :product="product"
        />
      </div>
      <nav
        v-if="totalPages > 1"
        class="products-pagination"
        aria-label="分頁"
      >
        <button
          type="button"
          :disabled="pageNumber <= 1"
          @click="goToPage(pageNumber - 1)"
        >
          上一頁
        </button>
        <span>第 {{ pageNumber }} / {{ totalPages }} 頁</span>
        <button
          type="button"
          :disabled="pageNumber >= totalPages"
          @click="goToPage(pageNumber + 1)"
        >
          下一頁
        </button>
      </nav>
    </template>
  </section>
</template>

<style scoped>
.products-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  margin-block-end: 1.5rem;
}

.products-filters input[type='search'],
.products-filters select {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.products-filters__checkbox {
  display: flex;
  align-items: center;
  gap: 0.375rem;
}

.products-summary {
  color: #6b7280;
  font-size: 0.875rem;
}

.products-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr));
  gap: 1rem;
}

.products-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-block-start: 2rem;
}
</style>

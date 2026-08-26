<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAdminProductList } from '../features/products/useProducts'
import { useFullBrandList } from '../features/brands/useBrands'
import { useFullCategoryList } from '../features/categories/useCategories'

const route = useRoute()
const router = useRouter()

function readFiltersFromQuery() {
  return {
    q: readQueryString('q'),
    brandCode: readQueryString('brandCode'),
    categoryCode: readQueryString('categoryCode'),
    status: readQueryString('status'),
  }
}

// Draft form state — bound to the inputs, changes on every keystroke/selection. Not what's
// searched on; see `appliedFilters` below.
const filters = reactive(readFiltersFromQuery())

// PR #24 review round 3: `listParams` used to read straight off `filters`, so every keystroke
// or dropdown change re-ran the search immediately, before "套用篩選" pushed the URL — the
// visible results, the URL, and what a shared/reloaded/back-navigated link would show all
// disagreed with each other. The Route Query is the single source of truth for what's actually
// searched; the draft form only feeds it via `applyFilters()`.
const appliedFilters = computed(() => readFiltersFromQuery())

const pageNumber = computed(() => Number(readQueryString('page') ?? '1') || 1)

const listParams = computed(() => ({
  q: appliedFilters.value.q || undefined,
  brandCodes: appliedFilters.value.brandCode ? [appliedFilters.value.brandCode] : undefined,
  categoryCodes: appliedFilters.value.categoryCode ? [appliedFilters.value.categoryCode] : undefined,
  statuses: appliedFilters.value.status ? [appliedFilters.value.status] : undefined,
  pageNumber: pageNumber.value,
  pageSize: 20,
}))

// Route query is the shared source of truth for filters, so browser back/forward and shared
// links must resynchronize the draft form state too (the actual search itself already tracks
// the URL directly via `appliedFilters` above — this only keeps the visible form controls from
// showing stale draft values after a navigation).
watch(
  () => route.query,
  () => { Object.assign(filters, readFiltersFromQuery()) },
)

const { data: result, isPending, isError, error, refetch } = useAdminProductList(listParams)
const {
  data: brandResult,
  isError: isBrandLookupError,
  error: brandLookupError,
  refetch: refetchBrands,
} = useFullBrandList({ isActive: true })
const {
  data: categoryResult,
  isError: isCategoryLookupError,
  error: categoryLookupError,
  refetch: refetchCategories,
} = useFullCategoryList({ isActive: true })
const areFilterLookupsErrored = computed(() => isBrandLookupError.value || isCategoryLookupError.value)

function retryFilterLookups() {
  refetchBrands()
  refetchCategories()
}

const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

function readQueryString(key: string): string | undefined {
  const value = route.query[key]
  return typeof value === 'string' && value.length > 0 ? value : undefined
}

function applyFilters() {
  router.push({
    query: {
      ...(filters.q ? { q: filters.q } : {}),
      ...(filters.brandCode ? { brandCode: filters.brandCode } : {}),
      ...(filters.categoryCode ? { categoryCode: filters.categoryCode } : {}),
      ...(filters.status ? { status: filters.status } : {}),
    },
  })
}

function goToPage(nextPage: number) {
  router.push({ query: { ...route.query, page: String(nextPage) } })
}

/** UC-ADM-PROD-01 acceptance: "價格顯示最低至最高區間" — the DTO already carries minPrice/maxPrice, this just formats them into a range. */
function formatPriceRange(minPrice: number | string, maxPrice: number | string): string {
  const min = Number(minPrice)
  const max = Number(maxPrice)
  if (min === max) {
    return `NT$${min.toLocaleString('zh-Hant-TW')}`
  }
  return `NT$${min.toLocaleString('zh-Hant-TW')} - NT$${max.toLocaleString('zh-Hant-TW')}`
}

function formatProductStatus(status: string): string {
  return {
    Draft: '草稿',
    Published: '已上架',
    Unpublished: '已下架',
    Discontinued: '已停產',
  }[status] ?? status
}
</script>

<template>
  <section aria-labelledby="products-page-title">
    <div class="products-header">
      <h1 id="products-page-title">
        商品管理
      </h1>
      <RouterLink
        class="products-header__add"
        to="/products/new"
      >
        新增商品
      </RouterLink>
    </div>

    <ErrorState
      v-if="areFilterLookupsErrored"
      :correlation-id="isApiError(brandLookupError) ? brandLookupError.correlationId : isApiError(categoryLookupError) ? categoryLookupError.correlationId : undefined"
      @retry="retryFilterLookups"
    />

    <form
      class="products-filters"
      aria-label="商品篩選"
      @submit.prevent="applyFilters"
    >
      <input
        v-model="filters.q"
        type="search"
        placeholder="搜尋商品代碼或名稱"
        aria-label="關鍵字"
      >
      <select
        v-model="filters.brandCode"
        aria-label="品牌"
      >
        <option value="">
          全部品牌
        </option>
        <option
          v-for="brand in brandResult?.items ?? []"
          :key="brand.publicId"
          :value="brand.code"
        >
          {{ brand.nameZhTw }}
        </option>
      </select>
      <select
        v-model="filters.categoryCode"
        aria-label="分類"
      >
        <option value="">
          全部分類
        </option>
        <option
          v-for="category in categoryResult?.items ?? []"
          :key="category.publicId"
          :value="category.code"
        >
          {{ category.nameZhTw }}
        </option>
      </select>
      <select
        v-model="filters.status"
        aria-label="狀態"
      >
        <option value="">
          全部狀態
        </option>
        <option value="Draft">
          草稿
        </option>
        <option value="Published">
          已上架
        </option>
        <option value="Unpublished">
          已下架
        </option>
        <option value="Discontinued">
          已停產
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
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
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
      <table class="products-table">
        <thead>
          <tr>
            <th>代碼</th>
            <th>名稱</th>
            <th>品牌</th>
            <th>分類</th>
            <th>狀態</th>
            <th>SKU 數</th>
            <th>價格區間</th>
            <th>庫存</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="product in result.items"
            :key="product.publicId"
          >
            <td>{{ product.productCode }}</td>
            <td>{{ product.nameZhTw }}</td>
            <td>{{ product.brand.name }}</td>
            <td>{{ product.category.name }}</td>
            <td>{{ formatProductStatus(product.status) }}</td>
            <td>{{ product.skuCount }}</td>
            <td>{{ formatPriceRange(product.minPrice, product.maxPrice) }}</td>
            <td>{{ product.totalOnHandQuantity }}</td>
            <td>
              <RouterLink :to="`/products/${product.publicId}`">
                編輯
              </RouterLink>
            </td>
          </tr>
        </tbody>
      </table>
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
.products-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.products-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  margin-block: 1.5rem;
}

.products-filters input[type='search'],
.products-filters select {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.products-summary {
  color: #6b7280;
  font-size: 0.875rem;
}

.products-table {
  width: 100%;
  border-collapse: collapse;
}

.products-table th,
.products-table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.products-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-block-start: 2rem;
}
</style>

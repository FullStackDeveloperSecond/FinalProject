<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAdminProductList } from '../features/products/useProducts'
import { useBrandList } from '../features/brands/useBrands'
import { useCategoryList } from '../features/categories/useCategories'

const route = useRoute()
const router = useRouter()

const filters = reactive({
  q: readQueryString('q'),
  brandCode: readQueryString('brandCode'),
  categoryCode: readQueryString('categoryCode'),
  status: readQueryString('status'),
})
const pageNumber = computed(() => Number(readQueryString('page') ?? '1') || 1)

const listParams = computed(() => ({
  q: filters.q || undefined,
  brandCodes: filters.brandCode ? [filters.brandCode] : undefined,
  categoryCodes: filters.categoryCode ? [filters.categoryCode] : undefined,
  statuses: filters.status ? [filters.status] : undefined,
  pageNumber: pageNumber.value,
  pageSize: 20,
}))

const { data: result, isPending, isError, error, refetch } = useAdminProductList(listParams)
const { data: brandResult } = useBrandList({ isActive: true, pageSize: 100 })
const { data: categoryResult } = useCategoryList({ isActive: true, pageSize: 100 })

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
            <td>{{ product.status }}</td>
            <td>{{ product.skuCount }}</td>
            <td>{{ product.totalOnHandQuantity }}</td>
            <td>
              <RouterLink :to="`/products/${product.publicId}/edit`">
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

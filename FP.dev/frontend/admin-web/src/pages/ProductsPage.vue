<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  useAdminProductList,
  useBulkProductAction,
  useExportProducts,
} from '../features/products/useProducts'
import type {
  AdminProductExportFormat,
  BulkPriceAdjustmentMode,
  BulkProductAction,
} from '../features/products/types'
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

// ---------------------------------------------------------------------------
// UC-ADM-PROD-02 批次上架／下架／調價
// ---------------------------------------------------------------------------

/**
 * 只記住 PublicId 是不夠的：批次動作要一併送出每個商品的 RowVersion，而 RowVersion 只有在
 * 目前這份列表結果裡才拿得到。所以選取要連 RowVersion 一起記。
 */
const selected = ref(new Map<string, string>())

const selectedCount = computed(() => selected.value.size)

const visibleProducts = computed(() => result.value?.items ?? [])

const areAllVisibleSelected = computed(() =>
  visibleProducts.value.length > 0
  && visibleProducts.value.every((product) => selected.value.has(product.publicId)))

function isSelected(publicId: string): boolean {
  return selected.value.has(publicId)
}

function toggleSelection(publicId: string, rowVersion: string) {
  const next = new Map(selected.value)
  if (next.has(publicId)) {
    next.delete(publicId)
  }
  else {
    next.set(publicId, rowVersion)
  }
  selected.value = next
}

function toggleAllVisible() {
  if (areAllVisibleSelected.value) {
    selected.value = new Map()
    return
  }
  selected.value = new Map(
    visibleProducts.value.map((product) => [product.publicId, product.rowVersion]),
  )
}

/**
 * 換頁或換篩選就清空選取。留著會有兩個問題：畫面上看不到的商品被偷偷一起操作，以及那些
 * RowVersion 早就過期、整批直接 409。清單資料一換就重置，是唯一說得通的行為。
 */
watch(
  () => [listParams.value, result.value] as const,
  () => { selected.value = new Map() },
)

const bulkAction = useBulkProductAction()
const exportProducts = useExportProducts()

const priceMode = ref<BulkPriceAdjustmentMode>('percentage')
// v-model.number 在欄位被清空時給的是空字串而不是 null，所以判斷不能只看 null——否則清空之後
// 按鈕會重新啟用，送出一次 value: 0 的無效調價。
const priceValue = ref<number | string | null>(null)

const hasPriceValue = computed(() =>
  priceValue.value !== null && priceValue.value !== '' && Number.isFinite(Number(priceValue.value)))
const priceReason = ref('')
const bulkMessage = ref<string | null>(null)
const bulkErrorMessage = ref<string | null>(null)
const isPriceFormOpen = ref(false)

function selectionPayload() {
  const entries = [...selected.value.entries()]
  return {
    productPublicIds: entries.map(([publicId]) => publicId),
    rowVersions: entries.map(([productPublicId, rowVersion]) => ({ productPublicId, rowVersion })),
  }
}

async function runBulkAction(action: BulkProductAction) {
  if (selectedCount.value === 0 || bulkAction.isPending.value) return
  bulkMessage.value = null
  bulkErrorMessage.value = null

  // 契約上 priceAdjustment 是可為 null 的欄位而不是可省略的欄位，所以非調價時送 null。
  const priceAdjustment = action === 'adjust-price'
    ? {
        mode: priceMode.value,
        value: Number(priceValue.value ?? 0),
        reason: priceReason.value.trim(),
      }
    : null

  try {
    const outcome = await bulkAction.mutateAsync({
      action,
      request: { ...selectionPayload(), priceAdjustment },
    })
    bulkMessage.value = action === 'adjust-price'
      ? `已調整 ${outcome.affectedProductCount} 項商品、共 ${outcome.affectedSkuCount} 個 SKU 的售價。`
      : `已${action === 'publish' ? '上架' : '下架'} ${outcome.affectedProductCount} 項商品。`
    selected.value = new Map()
    isPriceFormOpen.value = false
    priceValue.value = null
    priceReason.value = ''
  }
  catch (caught) {
    bulkErrorMessage.value = bulkActionErrorMessage(caught)
  }
}

/**
 * 批次動作的失敗一定是整批失敗（後端單一交易），所以訊息要講清楚「什麼都沒有改」——不然管理員
 * 會不知道該不該重做。
 */
function bulkActionErrorMessage(caught: unknown): string {
  const code = isApiError(caught) ? caught.code : undefined
  if (code === 'product_unavailable') {
    return '選取的商品中有已停產的項目，整批未執行。請取消勾選後再試。'
  }
  if (code === 'concurrency_conflict') {
    return '選取的商品已被其他人修改，整批未執行。請重新整理列表後再試。'
  }
  if (code === 'validation_failed') {
    return '批次條件不正確，整批未執行。請確認調價模式、數值與原因後再試。'
  }
  return '批次操作失敗，整批未執行。請稍後再試。'
}

async function runExport(format: AdminProductExportFormat) {
  if (exportProducts.isPending.value) return
  bulkErrorMessage.value = null
  try {
    // 帶的是 listParams（目前套用中的篩選），不是草稿表單——匯出的必須是管理員眼前那一組。
    await exportProducts.mutateAsync({ params: listParams.value, format })
  }
  catch {
    bulkErrorMessage.value = '匯出失敗，請稍後再試。'
  }
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
      class="products-filters card"
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

      <div
        class="products-bulk"
        role="group"
        aria-label="批次操作"
      >
        <p class="products-bulk__count">
          已選取 {{ selectedCount }} 項
        </p>
        <button
          type="button"
          :disabled="selectedCount === 0 || bulkAction.isPending.value"
          @click="runBulkAction('publish')"
        >
          批次上架
        </button>
        <button
          type="button"
          :disabled="selectedCount === 0 || bulkAction.isPending.value"
          @click="runBulkAction('unpublish')"
        >
          批次下架
        </button>
        <button
          type="button"
          :disabled="selectedCount === 0 || bulkAction.isPending.value"
          @click="isPriceFormOpen = !isPriceFormOpen"
        >
          批次調價
        </button>
        <span class="products-bulk__spacer" />
        <button
          type="button"
          :disabled="exportProducts.isPending.value"
          @click="runExport('csv')"
        >
          匯出 CSV
        </button>
        <button
          type="button"
          :disabled="exportProducts.isPending.value"
          @click="runExport('xlsx')"
        >
          匯出 XLSX
        </button>
      </div>

      <form
        v-if="isPriceFormOpen"
        class="products-bulk-price"
        aria-label="批次調價"
        @submit.prevent="runBulkAction('adjust-price')"
      >
        <select
          v-model="priceMode"
          aria-label="調價模式"
        >
          <option value="percentage">
            依百分比
          </option>
          <option value="amount">
            依金額
          </option>
        </select>
        <input
          v-model.number="priceValue"
          type="number"
          step="0.01"
          :aria-label="priceMode === 'percentage' ? '調價百分比' : '調價金額'"
          :min="priceMode === 'percentage' ? -90 : undefined"
          :max="priceMode === 'percentage' ? 100 : undefined"
          :placeholder="priceMode === 'percentage' ? '-90 ～ +100，例如 -10 表示打九折' : '例如 -100 表示每個 SKU 減 100'"
        >
        <input
          v-model="priceReason"
          type="text"
          maxlength="500"
          aria-label="調價原因"
          placeholder="調價原因（會寫入稽核紀錄）"
        >
        <button
          type="submit"
          :disabled="selectedCount === 0 || !hasPriceValue || priceReason.trim().length === 0 || bulkAction.isPending.value"
        >
          套用調價
        </button>
        <p class="products-bulk-price__hint">
          百分比限 -90 ～ +100；任一 SKU 調整後為負數即整批不執行。
        </p>
      </form>

      <p
        v-if="bulkMessage"
        class="products-bulk__message"
        role="status"
      >
        {{ bulkMessage }}
      </p>
      <p
        v-if="bulkErrorMessage"
        class="products-bulk__error"
        role="alert"
      >
        {{ bulkErrorMessage }}
      </p>
      <div class="table-scroll">
        <table class="products-table">
          <thead>
            <tr>
              <th class="products-table__select">
                <input
                  type="checkbox"
                  aria-label="全選本頁商品"
                  :checked="areAllVisibleSelected"
                  @change="toggleAllVisible"
                >
              </th>
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
              <td class="products-table__select">
                <input
                  type="checkbox"
                  :aria-label="`選取 ${product.nameZhTw}`"
                  :checked="isSelected(product.publicId)"
                  @change="toggleSelection(product.publicId, product.rowVersion)"
                >
              </td>
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
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  font: inherit;
}

.products-summary {
  color: var(--color-text-muted);
  font-size: 0.875rem;
}

.products-table {
  width: 100%;
  border-collapse: collapse;
}

.products-table th,
.products-table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--color-border);
  text-align: left;
}

.products-table__select {
  width: 2.5rem;
}

.products-bulk {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin-block: 0.75rem;
}

.products-bulk__count {
  margin: 0;
  color: #374151;
  font-size: 0.875rem;
}

.products-bulk__spacer {
  flex: 1 1 auto;
}

.products-bulk-price {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  margin-block-end: 0.75rem;
}

.products-bulk-price input,
.products-bulk-price select {
  min-height: 2.5rem;
  padding: 0.375rem 0.625rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.products-bulk-price__hint {
  flex-basis: 100%;
  margin: 0;
  color: #6b7280;
  font-size: 0.8125rem;
}

.products-bulk__message {
  color: #047857;
}

.products-bulk__error {
  color: #b91c1c;
}

.products-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-block-start: 2rem;
}
</style>

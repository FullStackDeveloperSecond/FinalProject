<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState, PagePager } from '@doselect/web-shared/components'
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ProductCard from '../features/catalog/components/ProductCard.vue'
import ProductBuildImportButton from '../features/builds/components/ProductBuildImportButton.vue'
import { categoryLabel } from '../features/catalog/categoryLabels'
import { useCatalogFilterOptions, useProductSearch } from '../features/catalog/useProductSearch'
import type { SpecFilterRequest } from '../features/catalog/types'

const route = useRoute()
const router = useRouter()

const SPEC_OPTIONS_QUERY_PREFIX = 'spec_'
const SPEC_RANGE_MIN_QUERY_PREFIX = 'specMin_'
const SPEC_RANGE_MAX_QUERY_PREFIX = 'specMax_'
const SPEC_BOOLEAN_QUERY_PREFIX = 'specBool_'

function readFiltersFromQuery() {
  return {
    q: readQueryString('q'),
    category: readQueryString('category') ?? '',
    brand: readQueryString('brand') ?? '',
    minPrice: readQueryString('minPrice'),
    maxPrice: readQueryString('maxPrice'),
    inStock: route.query.inStock === 'true',
    sort: readQueryString('sort') ?? 'relevance',
    // PR #24 review: C-02 requires whitelist spec filtering (API already supports it via
    // `Specs`/SpecFilterRequest[]) — one selected-option-codes array per semanticKey, for specs
    // that expose a closed `options` whitelist (CatalogFilterOptionsDto.specificationFilters[].options).
    specSelections: readSpecSelectionsFromQuery(SPEC_OPTIONS_QUERY_PREFIX),
    // PR #24 review round 2: 數值型規格（valueType Decimal, no options）用 gte／lte 做範圍篩選。
    specRanges: readSpecRangesFromQuery(),
    // PR #24 review round 2: 布林型規格（valueType Boolean, no options）用 eq 做是／否篩選。
    specBooleans: readSpecBooleansFromQuery(),
  }
}

// Draft form state — bound to the inputs, changes on every keystroke/toggle. Not what's
// searched on; see `appliedFilters` below.
const filters = reactive(readFiltersFromQuery())

// PR #24 review round 3: `searchParams` used to read straight off `filters`, so every
// keystroke or option toggle re-ran the search immediately, before "套用篩選" pushed the URL —
// the visible results, the URL, and what a shared/reloaded/back-navigated link would show all
// disagreed with each other, plus every unsubmitted keystroke fired its own request. The Route
// Query is the single source of truth for what's actually searched; the draft form only feeds
// it via `applyFilters()`.
const appliedFilters = computed(() => readFiltersFromQuery())
const activeFilterLabels = computed(() => {
  const f = appliedFilters.value
  return [f.q && `關鍵字：${f.q}`, f.category && `分類：${categoryLabel(f.category)}`, f.brand && `品牌：${f.brand}`, f.minPrice && `最低 NT$${f.minPrice}`, f.maxPrice && `最高 NT$${f.maxPrice}`, f.inStock && '只顯示現貨'].filter(Boolean)
})

const pageNumber = computed(() => Number(readQueryString('page') ?? '1') || 1)

const optionSpecFilterRequests = computed<SpecFilterRequest[]>(() =>
  Object.entries(appliedFilters.value.specSelections)
    .filter(([, values]) => values.length > 0)
    .map(([semanticKey, values]) => ({ semanticKey, operator: 'in', values })))

const rangeSpecFilterRequests = computed<SpecFilterRequest[]>(() =>
  Object.entries(appliedFilters.value.specRanges).flatMap(([semanticKey, range]) => {
    const requests: SpecFilterRequest[] = []
    if (range.min) {
      requests.push({ semanticKey, operator: 'gte', value: range.min })
    }
    if (range.max) {
      requests.push({ semanticKey, operator: 'lte', value: range.max })
    }
    return requests
  }))

const booleanSpecFilterRequests = computed<SpecFilterRequest[]>(() =>
  Object.entries(appliedFilters.value.specBooleans)
    .filter(([, value]) => value)
    .map(([semanticKey, value]) => ({ semanticKey, operator: 'eq', value })))

const specFilters = computed<SpecFilterRequest[]>(() => [
  ...optionSpecFilterRequests.value,
  ...rangeSpecFilterRequests.value,
  ...booleanSpecFilterRequests.value,
])

const searchParams = computed(() => ({
  q: appliedFilters.value.q || undefined,
  category: appliedFilters.value.category || undefined,
  brand: appliedFilters.value.brand || undefined,
  minPrice: appliedFilters.value.minPrice ? Number(appliedFilters.value.minPrice) : undefined,
  maxPrice: appliedFilters.value.maxPrice ? Number(appliedFilters.value.maxPrice) : undefined,
  inStock: appliedFilters.value.inStock,
  specs: specFilters.value.length > 0 ? specFilters.value : undefined,
  sort: appliedFilters.value.sort,
  pageNumber: pageNumber.value,
  pageSize: 24,
}))

const { data: result, isPending, isError, error, refetch } = useProductSearch(searchParams)
const {
  data: filterOptions,
  isError: isFilterOptionsError,
  refetch: refetchFilterOptions,
} = useCatalogFilterOptions(() => filters.category)

function retryCatalogFilterOptions() {
  refetchFilterOptions()
}

/*
 * `/api/v1/catalog/filter-options` 的 `categories` 是「還可以再往下鑽的分類」：
 * 沒帶 Category 時回頂層清單，帶了 Category 時回**該分類的子分類**
 * （EfCatalogFilterOptionsService.GetCategoriesAsync）。
 *
 * 所以深連結進 `/products?category=CPU`（首頁分類卡就是這樣進來的）時，回應裡
 * 根本不會有 CPU 自己 —— seeded 的 CPU／GPU 都是沒有子分類的頂層分類，回的是空陣列。
 * 原生 <select> 找不到對應的 <option> 就只能落回空值，使用者看到「全部分類」
 * 卻拿到 CPU 的搜尋結果。同樣的情形也發生在「還沒套用就先在下拉選了分類」的當下。
 *
 * 這裡只補**顯示用**的選項：不寫回 filters.category、不 push route、不觸發搜尋，
 * 因此尚未套用的編輯不會被蓋掉，route query 也仍然是 applied filters 的唯一來源。
 */
const knownCategoryNames = ref<Record<string, string>>({})

watch(filterOptions, (value) => {
  for (const category of value?.categories ?? []) {
    knownCategoryNames.value[category.code] = category.name
  }
}, { immediate: true })

const categoryOptions = computed(() => {
  const fromApi = filterOptions.value?.categories ?? []
  const selected = filters.category
  if (!selected || fromApi.some((category) => category.code === selected)) {
    return fromApi
  }
  return [
    {
      // 合成的顯示項目：publicId 只當 v-for key，不會送到後端
      publicId: `applied:${selected}`,
      code: selected,
      // 先用 API 給過的權威名稱；沒看過就退回本地對照表，最後才是代碼本身
      name: knownCategoryNames.value[selected] ?? categoryLabel(selected),
    },
    ...fromApi,
  ]
})

const optionSpecFilters = computed(() =>
  (filterOptions.value?.specificationFilters ?? []).filter((spec) => spec.options && spec.options.length > 0))
const rangeSpecFilters = computed(() =>
  (filterOptions.value?.specificationFilters ?? []).filter((spec) => !spec.options && spec.valueType === 'Decimal'))
const booleanSpecFilters = computed(() =>
  (filterOptions.value?.specificationFilters ?? []).filter((spec) => !spec.options && spec.valueType === 'Boolean'))

// PR #24 review round 2: switching category used to leave the old category's spec selections
// sitting in state (and the URL) — the backend validates SemanticKey against the *new*
// category and rejects it as search_filter_unsupported, while the UI gave no way to see or
// clear the now-invalid condition. Prune any selection whose semanticKey isn't offered by the
// newly-loaded category's own filter options.
watch(filterOptions, (value) => {
  if (!value) {
    return
  }
  const validKeys = new Set(value.specificationFilters.map((spec) => spec.semanticKey))
  for (const key of Object.keys(filters.specSelections)) {
    if (!validKeys.has(key)) {
      delete filters.specSelections[key]
    }
  }
  for (const key of Object.keys(filters.specRanges)) {
    if (!validKeys.has(key)) {
      delete filters.specRanges[key]
    }
  }
  for (const key of Object.keys(filters.specBooleans)) {
    if (!validKeys.has(key)) {
      delete filters.specBooleans[key]
    }
  }
})

const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

function readQueryString(key: string): string | undefined {
  const value = route.query[key]
  return typeof value === 'string' && value.length > 0 ? value : undefined
}

function readSpecSelectionsFromQuery(prefix: string): Record<string, string[]> {
  const selections: Record<string, string[]> = {}
  for (const [key, value] of Object.entries(route.query)) {
    if (key.startsWith(prefix) && typeof value === 'string' && value.length > 0) {
      selections[key.slice(prefix.length)] = value.split(',')
    }
  }
  return selections
}

function readSpecRangesFromQuery(): Record<string, { min?: string, max?: string }> {
  const ranges: Record<string, { min?: string, max?: string }> = {}
  for (const [key, value] of Object.entries(route.query)) {
    if (typeof value !== 'string' || value.length === 0) {
      continue
    }
    if (key.startsWith(SPEC_RANGE_MIN_QUERY_PREFIX)) {
      const semanticKey = key.slice(SPEC_RANGE_MIN_QUERY_PREFIX.length)
      ranges[semanticKey] = { ...ranges[semanticKey], min: value }
    } else if (key.startsWith(SPEC_RANGE_MAX_QUERY_PREFIX)) {
      const semanticKey = key.slice(SPEC_RANGE_MAX_QUERY_PREFIX.length)
      ranges[semanticKey] = { ...ranges[semanticKey], max: value }
    }
  }
  return ranges
}

function readSpecBooleansFromQuery(): Record<string, string> {
  const booleans: Record<string, string> = {}
  for (const [key, value] of Object.entries(route.query)) {
    if (key.startsWith(SPEC_BOOLEAN_QUERY_PREFIX) && typeof value === 'string' && value.length > 0) {
      booleans[key.slice(SPEC_BOOLEAN_QUERY_PREFIX.length)] = value
    }
  }
  return booleans
}

function toggleSpecOption(semanticKey: string, optionCode: string, checked: boolean) {
  const current = filters.specSelections[semanticKey] ?? []
  filters.specSelections[semanticKey] = checked
    ? [...current, optionCode]
    : current.filter((code) => code !== optionCode)
}

function applyFilters() {
  const specOptionsQuery = Object.fromEntries(
    Object.entries(filters.specSelections)
      .filter(([, values]) => values.length > 0)
      .map(([semanticKey, values]) => [`${SPEC_OPTIONS_QUERY_PREFIX}${semanticKey}`, values.join(',')]))
  const specRangeQuery = Object.fromEntries(
    Object.entries(filters.specRanges).flatMap(([semanticKey, range]) => {
      const entries: [string, string][] = []
      if (range.min) {
        entries.push([`${SPEC_RANGE_MIN_QUERY_PREFIX}${semanticKey}`, range.min])
      }
      if (range.max) {
        entries.push([`${SPEC_RANGE_MAX_QUERY_PREFIX}${semanticKey}`, range.max])
      }
      return entries
    }))
  const specBooleanQuery = Object.fromEntries(
    Object.entries(filters.specBooleans)
      .filter(([, value]) => value)
      .map(([semanticKey, value]) => [`${SPEC_BOOLEAN_QUERY_PREFIX}${semanticKey}`, value]))
  router.push({
    query: {
      ...(filters.q ? { q: filters.q } : {}),
      ...(filters.category ? { category: filters.category } : {}),
      ...(filters.brand ? { brand: filters.brand } : {}),
      ...(filters.minPrice ? { minPrice: filters.minPrice } : {}),
      ...(filters.maxPrice ? { maxPrice: filters.maxPrice } : {}),
      ...(filters.inStock ? { inStock: 'true' } : {}),
      ...(filters.sort !== 'relevance' ? { sort: filters.sort } : {}),
      ...specOptionsQuery,
      ...specRangeQuery,
      ...specBooleanQuery,
    },
  })
}

function applyCategoryFilter() {
  // 分類一變，舊分類的規格條件就不再有語意；先移除再更新網址，避免送出
  // 「新分類＋舊規格」而被 API 拒絕。
  filters.specSelections = {}
  filters.specRanges = {}
  filters.specBooleans = {}
  applyFilters()
}

function goToPage(nextPage: number) {
  router.push({ query: { ...route.query, page: String(nextPage) } })
}

function clearAllFilters() {
  router.push({ query: {} })
}

// Route query is the shared source of truth for filters (per the
// M功能桌面UI與Route規格 "可分享列表條件" rule), so browser back/forward
// and shared links must resynchronize the draft form state too (the actual search itself
// already tracks the URL directly via `appliedFilters` above — this only keeps the visible
// form controls from showing stale draft values after a navigation).
watch(
  () => route.query,
  () => { Object.assign(filters, readFiltersFromQuery()) },
)
</script>

<template>
  <section aria-labelledby="products-page-title">
    <header class="catalog-head">
      <h1 id="products-page-title">
        商品搜尋
      </h1>
      <p>先選用途和預算，再看細節。看不懂的規格我們都翻成「適合做什麼」。</p>
    </header>

    <p
      v-if="isFilterOptionsError"
      class="products-filter-options-error"
      role="alert"
    >
      目錄篩選資料載入失敗，既有網址條件可能仍在套用。
      <button
        type="button"
        @click="retryCatalogFilterOptions"
      >
        重試
      </button>
      <button
        type="button"
        @click="clearAllFilters"
      >
        清除全部篩選
      </button>
    </p>

    <form
      class="products-filters"
      aria-label="商品篩選"
      @submit.prevent="applyFilters"
    >
      <label class="products-filter-field products-filter-field--wide">
        <span>關鍵字</span>
        <input
          v-model="filters.q"
          type="search"
          placeholder="搜尋商品名稱或代碼"
          aria-label="關鍵字"
        >
      </label>
      <label class="products-filter-field">
        <span>分類</span>
        <select
          v-model="filters.category"
          aria-label="分類"
          @change="applyCategoryFilter"
        >
          <option value="">
            全部分類
          </option>
          <option
            v-for="category in categoryOptions"
            :key="category.publicId"
            :value="category.code"
          >
            {{ category.name }}
          </option>
        </select>
      </label>
      <label class="products-filter-field">
        <span>品牌</span>
        <select
          v-model="filters.brand"
          aria-label="品牌"
          @change="applyFilters"
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
      </label>
      <label class="products-filter-field products-filters__price">
        <span>最低價</span>
        <input
          v-model="filters.minPrice"
          type="number"
          min="0"
          aria-label="最低價"
        >
      </label>
      <label class="products-filter-field products-filters__price">
        <span>最高價</span>
        <input
          v-model="filters.maxPrice"
          type="number"
          min="0"
          aria-label="最高價"
        >
      </label>
      <label class="products-filter-field">
        <span>排序</span>
        <select
          v-model="filters.sort"
          aria-label="排序方式"
          @change="applyFilters"
        >
          <option
            v-for="option in filterOptions?.sortOptions ?? ['relevance', 'priceAsc', 'priceDesc', 'newest']"
            :key="option"
            :value="option"
          >
            {{ { relevance: '相關度', priceAsc: '價格由低到高', priceDesc: '價格由高到低', newest: '最新上架' }[option] ?? option }}
          </option>
        </select>
      </label>
      <label class="products-filters__checkbox">
        <input
          v-model="filters.inStock"
          type="checkbox"
        >
        只顯示現貨
      </label>
      <button
        class="products-filters__submit"
        type="submit"
      >
        套用篩選
      </button>

      <fieldset
        v-for="spec in optionSpecFilters"
        :key="spec.semanticKey"
        class="products-filters__spec"
      >
        <legend>{{ spec.label }}</legend>
        <label
          v-for="option in spec.options ?? []"
          :key="option.code"
        >
          <input
            type="checkbox"
            :checked="(filters.specSelections[spec.semanticKey] ?? []).includes(option.code)"
            @change="toggleSpecOption(spec.semanticKey, option.code, ($event.target as HTMLInputElement).checked)"
          >
          {{ option.label }}
        </label>
      </fieldset>

      <fieldset
        v-for="spec in rangeSpecFilters"
        :key="spec.semanticKey"
        class="products-filters__spec"
      >
        <legend>{{ spec.label }}{{ spec.unit ? `（${spec.unit}）` : '' }}</legend>
        <label>
          最小值
          <input
            :value="filters.specRanges[spec.semanticKey]?.min ?? ''"
            type="number"
            :aria-label="`${spec.label} 最小值`"
            @input="filters.specRanges[spec.semanticKey] = { ...filters.specRanges[spec.semanticKey], min: ($event.target as HTMLInputElement).value }"
          >
        </label>
        <label>
          最大值
          <input
            :value="filters.specRanges[spec.semanticKey]?.max ?? ''"
            type="number"
            :aria-label="`${spec.label} 最大值`"
            @input="filters.specRanges[spec.semanticKey] = { ...filters.specRanges[spec.semanticKey], max: ($event.target as HTMLInputElement).value }"
          >
        </label>
      </fieldset>

      <fieldset
        v-for="spec in booleanSpecFilters"
        :key="spec.semanticKey"
        class="products-filters__spec"
      >
        <legend>{{ spec.label }}</legend>
        <select
          v-model="filters.specBooleans[spec.semanticKey]"
          :aria-label="spec.label"
          @change="applyFilters"
        >
          <option value="">
            不限
          </option>
          <option value="true">
            是
          </option>
          <option value="false">
            否
          </option>
        </select>
      </fieldset>
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
      <div
        v-if="activeFilterLabels.length"
        class="active-filter-chips"
        aria-label="已套用篩選"
      >
        <span
          v-for="label in activeFilterLabels"
          :key="String(label)"
        >{{ label }}</span>
      </div>
      <p class="products-summary">
        共 {{ result.totalCount }} 項商品
      </p>
      <div class="products-grid">
        <div
          v-for="product in result.items"
          :key="product.defaultSkuPublicId"
        >
          <ProductCard :product="product" />
          <ProductBuildImportButton :product-public-id="product.productPublicId" />
        </div>
      </div>
      <PagePager
        v-if="totalPages > 1"
        class="products-pagination"
        :page="pageNumber"
        :page-size="24"
        :total-records="Number(result.totalCount)"
        aria-label="商品分頁"
        @update:page="goToPage"
      />
    </template>
  </section>
</template>

<style scoped>
/* 每個欄位都是同一種結構：標籤在上、控制項 44px 在下。
   之前搜尋框沒有標籤、價格與排序的標籤是內嵌的，六個欄位落在六個不同的
   垂直位置；統一之後配合 align-items: end 就能對齊同一條基線。 */
.products-filter-field {
  display: grid;
  gap: var(--space-1);
  min-width: 0;
}

.products-filter-field > span {
  font-size: var(--fs-caption);
  font-weight: 700;
  color: var(--color-text-muted);
}

.products-filter-field select,
.products-filter-field input {
  width: 100%;
}

/* 關鍵字欄位比其他欄寬，佔兩格 */
.products-filter-field--wide {
  grid-column: span 2;
}

/* 篩選列包成一張卡片，與商品卡使用同一組表面語彙 */
.products-filters {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  align-items: end;
  gap: var(--space-4) var(--space-3);
  margin-block-end: var(--space-5);
  padding: var(--space-4) var(--space-5);
  border: 1px solid var(--color-border-soft);
  border-radius: var(--radius-lg);
  background: var(--color-surface);
  box-shadow: var(--shadow-sm);
}

.products-filters input[type='search'],
.products-filters input[type='number'],
.products-filters select {
  min-height: var(--control-height);
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  font: inherit;
}

/* 勾選框沒有上方標籤，補到同樣的 44px 讓它跟其他控制項齊底 */
.products-filters__checkbox {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--control-height);
  font-size: var(--fs-caption);
  color: var(--color-text-muted);
  white-space: nowrap;
}

.products-filters__submit {
  min-height: var(--control-height);
  padding: 0 var(--space-5);
  border: 1px solid var(--color-primary);
  border-radius: var(--radius-sm);
  background: var(--color-primary);
  color: var(--color-on-primary);
  font: inherit;
  font-weight: 700;
  cursor: pointer;
}

.products-filters__submit:hover {
  background: var(--color-primary-hover);
  border-color: var(--color-primary-hover);
}

/* 規格篩選是整列展開的，跨滿所有格 */
.products-filters__spec {
  grid-column: 1 / -1;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-3);
  margin: 0;
  border: 1px solid var(--color-border-soft);
  border-radius: var(--radius-sm);
  padding: var(--space-2) var(--space-3);
}

/* 桌機固定六欄，版面才不會隨欄位增減跳動：
   第一列 關鍵字（跨兩格）／分類／品牌／最低價／最高價 剛好填滿，
   第二列 排序／只顯示現貨 在左，送出鈕釘在最右格。 */
@media (min-width: 901px) {
  .products-filters {
    grid-template-columns: repeat(6, minmax(0, 1fr));
  }

  .products-filters__submit {
    grid-column: 6;
    justify-self: end;
  }
}

@media (max-width: 600px) {
  /* 窄畫面一欄到底，關鍵字不再跨兩格 */
  .products-filters {
    grid-template-columns: 1fr;
    padding: var(--space-4);
  }

  .products-filter-field--wide {
    grid-column: span 1;
  }

  .products-filters__submit {
    justify-self: stretch;
  }
}

.products-filters__spec label {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.875rem;
}

.products-summary {
  color: var(--color-text-muted);
  font-size: 0.875rem;
}

.products-filter-options-error {
  color: var(--color-danger);
}

.products-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 1rem;
}

.products-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-block-start: 2rem;
}

@media (min-width: 76rem) {
  .products-grid { grid-template-columns: repeat(4, minmax(0, 1fr)); }
}
@media (max-width: 55rem) {
  .products-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
@media (max-width: 36rem) {
  .products-grid { grid-template-columns: minmax(0, 1fr); }
}
</style>

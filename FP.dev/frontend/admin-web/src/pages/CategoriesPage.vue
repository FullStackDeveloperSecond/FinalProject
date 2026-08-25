<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import CatalogLookupTable, { type CatalogLookupItem } from '../components/catalog/CatalogLookupTable.vue'
import { useCategoryList, useCreateCategory, useFullCategoryList, useUpdateCategory } from '../features/categories/useCategories'
import type { CategoryDto } from '../features/categories/types'

interface CategoryCreateState {
  code: string
  nameZhTw: string
  slug: string
  description: string
  parentCategoryPublicId: string
  sortOrder: number
  isActive: boolean
}

const pageSize = 20
const filters = reactive({ q: '', pageNumber: 1 })
const listParams = computed(() => ({ q: filters.q, pageNumber: filters.pageNumber, pageSize }))
const { data: result, isPending, isError, error, refetch } = useCategoryList(listParams)
const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

// PR #24 review: the parent-category dropdown used to reuse `result.items` (the current,
// paginated/filtered page) — an existing category's parent fell out of the dropdown's
// options the moment it wasn't on the currently-viewed page, and picking a new parent was
// impossible for anything beyond page 1. A separate, unfiltered, large-enough lookup keeps
// every category available as a parent option regardless of the main table's pagination.
const {
  data: allCategoriesResult,
  isError: isAllCategoriesError,
  refetch: refetchAllCategories,
} = useFullCategoryList()

function retryAllCategories() {
  refetchAllCategories()
}

function search() {
  filters.pageNumber = 1
}

function goToPage(nextPage: number) {
  filters.pageNumber = nextPage
}

const createMutation = useCreateCategory()
const updateMutation = useUpdateCategory()
const table = ref<InstanceType<typeof CatalogLookupTable> | null>(null)

function makeEditState(lookupItem: CatalogLookupItem): Record<string, unknown> {
  const item = lookupItem as CategoryDto
  const state: CategoryCreateState = {
    code: item.code,
    nameZhTw: item.nameZhTw,
    slug: item.slug,
    description: item.description ?? '',
    parentCategoryPublicId: item.parentCategoryPublicId ?? '',
    sortOrder: Number(item.sortOrder),
    isActive: item.isActive,
  }
  return state as unknown as Record<string, unknown>
}

function makeCreateState(): Record<string, unknown> {
  const state: CategoryCreateState = { code: '', nameZhTw: '', slug: '', description: '', parentCategoryPublicId: '', sortOrder: 0, isActive: true }
  return state as unknown as Record<string, unknown>
}

function handleCreate(rawState: Record<string, unknown>) {
  const state = rawState as unknown as CategoryCreateState
  createMutation.mutate({
    code: state.code,
    nameZhTw: state.nameZhTw,
    slug: state.slug,
    description: state.description || null,
    parentCategoryPublicId: state.parentCategoryPublicId || null,
    sortOrder: state.sortOrder,
    isActive: state.isActive,
  }, {
    onSuccess: () => table.value?.closeCreateRow(),
  })
}

function handleUpdate(publicId: string, rowVersion: string, rawState: Record<string, unknown>) {
  const state = rawState as unknown as CategoryCreateState
  updateMutation.mutate({
    publicId,
    request: {
      nameZhTw: state.nameZhTw,
      slug: state.slug,
      description: state.description || null,
      parentCategoryPublicId: state.parentCategoryPublicId || null,
      sortOrder: state.sortOrder,
      isActive: state.isActive,
      rowVersion,
    },
  }, {
    onSuccess: () => table.value?.closeEditRow(),
  })
}
</script>

<template>
  <section aria-labelledby="categories-page-title">
    <h1 id="categories-page-title">
      分類管理
    </h1>

    <form
      class="categories-filters"
      aria-label="分類搜尋"
      @submit.prevent="search"
    >
      <input
        v-model="filters.q"
        type="search"
        placeholder="搜尋分類代碼或名稱"
        aria-label="關鍵字"
      >
      <button type="submit">
        搜尋
      </button>
    </form>

    <p
      v-if="isAllCategoriesError"
      class="categories-lookup-error"
      role="alert"
    >
      上層分類資料載入失敗，為避免清除既有關聯，暫停新增與編輯。
      <button
        type="button"
        @click="retryAllCategories"
      >
        重試
      </button>
    </p>

    <CatalogLookupTable
      ref="table"
      :items="result?.items"
      :is-pending="isPending"
      :is-error="isError"
      :error="error"
      empty-title="沒有符合條件的分類"
      :creating="createMutation.isPending.value"
      :create-error="createMutation.error.value"
      :saving-id="updateMutation.isPending.value ? updateMutation.variables.value?.publicId ?? null : null"
      :update-error="updateMutation.error.value"
      :make-edit-state="makeEditState"
      :make-create-state="makeCreateState"
      :operations-disabled="isAllCategoriesError"
      @retry="refetch"
      @create="handleCreate"
      @update="handleUpdate"
    >
      <template #extra-headers>
        <th>Slug</th>
        <th>上層分類</th>
      </template>
      <template #extra-fields="{ state }">
        <td>
          <input
            v-model="(state as unknown as CategoryCreateState).slug"
            aria-label="Slug"
          >
        </td>
        <td>
          <select
            v-model="(state as unknown as CategoryCreateState).parentCategoryPublicId"
            aria-label="上層分類"
          >
            <option value="">
              （無）
            </option>
            <option
              v-for="category in allCategoriesResult?.items ?? []"
              :key="category.publicId"
              :value="category.publicId"
            >
              {{ category.nameZhTw }}
            </option>
          </select>
        </td>
      </template>
      <template #extra-columns="{ item }">
        <td>{{ (item as CategoryDto).slug }}</td>
        <td>{{ allCategoriesResult?.items?.find(c => c.publicId === (item as CategoryDto).parentCategoryPublicId)?.nameZhTw ?? '—' }}</td>
      </template>
    </CatalogLookupTable>
    <nav
      v-if="totalPages > 1"
      class="categories-pagination"
      aria-label="分頁"
    >
      <button
        type="button"
        :disabled="filters.pageNumber <= 1"
        @click="goToPage(filters.pageNumber - 1)"
      >
        上一頁
      </button>
      <span>第 {{ filters.pageNumber }} / {{ totalPages }} 頁</span>
      <button
        type="button"
        :disabled="filters.pageNumber >= totalPages"
        @click="goToPage(filters.pageNumber + 1)"
      >
        下一頁
      </button>
    </nav>
  </section>
</template>

<style scoped>
.categories-filters {
  display: flex;
  gap: 0.75rem;
  margin-block-end: 1.5rem;
}

.categories-filters input {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.categories-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-block-start: 1.5rem;
}

.categories-lookup-error {
  color: #b91c1c;
}
</style>

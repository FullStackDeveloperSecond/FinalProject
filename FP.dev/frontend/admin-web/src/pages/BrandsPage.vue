<script setup lang="ts">
import { computed, ref } from 'vue'
import CatalogLookupTable, { type CatalogLookupItem } from '../components/catalog/CatalogLookupTable.vue'
import { useBrandList, useCreateBrand, useUpdateBrand } from '../features/brands/useBrands'
import { useSearchFilters } from '../features/shared/useSearchFilters'
import type { BrandDto } from '../features/brands/types'

interface BrandCreateState {
  code: string
  nameZhTw: string
  description: string
  websiteUrl: string
  sortOrder: number
  isActive: boolean
}

const { filters, listParams, search, goToPage } = useSearchFilters(20)
const { data: result, isPending, isError, error, refetch } = useBrandList(listParams)
const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

const createMutation = useCreateBrand()
const updateMutation = useUpdateBrand()
const table = ref<InstanceType<typeof CatalogLookupTable> | null>(null)

function makeEditState(lookupItem: CatalogLookupItem): Record<string, unknown> {
  const item = lookupItem as BrandDto
  const state: BrandCreateState = {
    code: item.code,
    nameZhTw: item.nameZhTw,
    description: item.description ?? '',
    websiteUrl: item.websiteUrl ?? '',
    sortOrder: Number(item.sortOrder),
    isActive: item.isActive,
  }
  return state as unknown as Record<string, unknown>
}

function makeCreateState(): Record<string, unknown> {
  const state: BrandCreateState = { code: '', nameZhTw: '', description: '', websiteUrl: '', sortOrder: 0, isActive: true }
  return state as unknown as Record<string, unknown>
}

function handleCreate(rawState: Record<string, unknown>) {
  const state = rawState as unknown as BrandCreateState
  createMutation.mutate({
    code: state.code,
    nameZhTw: state.nameZhTw,
    description: state.description || null,
    websiteUrl: state.websiteUrl || null,
    sortOrder: state.sortOrder,
    isActive: state.isActive,
  }, {
    onSuccess: () => table.value?.closeCreateRow(),
  })
}

function handleUpdate(publicId: string, rowVersion: string, rawState: Record<string, unknown>) {
  const state = rawState as unknown as BrandCreateState
  updateMutation.mutate({
    publicId,
    request: {
      nameZhTw: state.nameZhTw,
      description: state.description || null,
      websiteUrl: state.websiteUrl || null,
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
  <section aria-labelledby="brands-page-title">
    <h1 id="brands-page-title">
      品牌管理
    </h1>

    <form
      class="brands-filters"
      aria-label="品牌搜尋"
      @submit.prevent="search"
    >
      <input
        v-model="filters.q"
        type="search"
        placeholder="搜尋品牌代碼或名稱"
        aria-label="關鍵字"
      >
      <button type="submit">
        搜尋
      </button>
    </form>

    <CatalogLookupTable
      ref="table"
      :items="result?.items"
      :is-pending="isPending"
      :is-error="isError"
      :error="error"
      empty-title="沒有符合條件的品牌"
      :creating="createMutation.isPending.value"
      :create-error="createMutation.error.value"
      :saving-id="updateMutation.isPending.value ? updateMutation.variables.value?.publicId ?? null : null"
      :update-error="updateMutation.error.value"
      :make-edit-state="makeEditState"
      :make-create-state="makeCreateState"
      @retry="refetch"
      @create="handleCreate"
      @update="handleUpdate"
      @create-started="createMutation.reset()"
      @edit-started="updateMutation.reset()"
    >
      <template #extra-headers>
        <th>網站</th>
      </template>
      <template #extra-fields="{ state }">
        <td>
          <input
            v-model="(state as unknown as BrandCreateState).websiteUrl"
            aria-label="網站"
          >
        </td>
      </template>
      <template #extra-columns="{ item }">
        <td>{{ (item as BrandDto).websiteUrl ?? '—' }}</td>
      </template>
    </CatalogLookupTable>
    <nav
      v-if="totalPages > 1"
      class="brands-pagination"
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
.brands-filters {
  display: flex;
  gap: 0.75rem;
  margin-block-end: 1.5rem;
}

.brands-filters input {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.brands-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-block-start: 1.5rem;
}
</style>

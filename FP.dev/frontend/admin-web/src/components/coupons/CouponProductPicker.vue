<script setup lang="ts">
/**
 * 以關鍵字搜尋商品並維護一份已選清單。
 *
 * 「適用商品」與「排除商品」共用這個元件，兩者的差別只有標題與說明文字。
 */
import { computed, ref } from 'vue'
import { isApiError } from '@doselect/web-shared/api'
import {
  useProductOptionLabels,
  useProductOptionSearch,
} from '../../features/catalog-reference/useCatalogReference'
import { describeApiError } from '../../features/shared/errorMessages'

const props = defineProps<{
  label: string
  hint: string
  modelValue: string[]
}>()

const emit = defineEmits<{
  'update:modelValue': [string[]]
}>()

const searchPageSize = 10

const term = ref('')
const submittedTerm = ref('')

const searchParams = computed(() => ({
  q: submittedTerm.value,
  pageNumber: 1,
  pageSize: searchPageSize,
}))

const {
  data: results,
  isFetching,
  isError: isSearchError,
  error: searchError,
} = useProductOptionSearch(searchParams, computed(() => submittedTerm.value !== ''))

const { data: labels } = useProductOptionLabels(computed(() => props.modelValue))

function runSearch() {
  submittedTerm.value = term.value.trim()
}

function isSelected(publicId: string): boolean {
  return props.modelValue.includes(publicId)
}

function toggle(publicId: string) {
  emit(
    'update:modelValue',
    isSelected(publicId)
      ? props.modelValue.filter(selected => selected !== publicId)
      : [...props.modelValue, publicId],
  )
}

/**
 * 已選項目的顯示文字。
 *
 * 解析不到名稱時退回顯示 `publicId` —— 商品可能已下架或刪除，但那筆設定仍然
 * 存在於這張券上，藏起來會讓管理員以為自己沒選過它。
 */
function describeSelected(publicId: string): string {
  const option = labels.value?.[publicId]
  return option ? `${option.name}（${option.code}）` : publicId
}
</script>

<template>
  <div class="scope-products">
    <h4>{{ props.label }}</h4>
    <p class="scope-hint">
      {{ props.hint }}
    </p>

    <div class="scope-search">
      <input
        v-model="term"
        type="search"
        :aria-label="`搜尋${props.label}`"
        placeholder="輸入商品名稱或型號"
        @keydown.enter.prevent="runSearch"
      >
      <button
        type="button"
        @click="runSearch"
      >
        搜尋
      </button>
    </div>

    <p
      v-if="isSearchError"
      class="scope-error"
      role="alert"
    >
      商品搜尋失敗：{{ isApiError(searchError) ? describeApiError(searchError) : '請稍後再試。' }}
    </p>

    <p v-else-if="submittedTerm !== '' && isFetching">
      搜尋中…
    </p>

    <p v-else-if="submittedTerm !== '' && results && results.items.length === 0">
      沒有符合「{{ submittedTerm }}」的已上架商品。
    </p>

    <ul
      v-else-if="results && results.items.length > 0"
      class="scope-results"
    >
      <li
        v-for="option in results.items"
        :key="option.publicId"
      >
        <label>
          <input
            type="checkbox"
            :checked="isSelected(option.publicId)"
            @change="toggle(option.publicId)"
          >
          {{ option.name }}（{{ option.code }}）
        </label>
      </li>
    </ul>

    <p
      v-if="results && Number(results.totalPages) > 1"
      class="scope-hint"
    >
      只顯示前 {{ searchPageSize }} 筆，請輸入更精確的關鍵字。
    </p>

    <h5>已選 {{ props.modelValue.length }} 項</h5>
    <p v-if="props.modelValue.length === 0">
      尚未選擇。
    </p>
    <ul
      v-else
      class="scope-selected"
    >
      <li
        v-for="publicId in props.modelValue"
        :key="publicId"
      >
        <span>{{ describeSelected(publicId) }}</span>
        <button
          type="button"
          :aria-label="`移除 ${describeSelected(publicId)}`"
          @click="toggle(publicId)"
        >
          移除
        </button>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.scope-products {
  border: 1px solid #d0d0d0;
  border-radius: 4px;
  padding: 0.75rem;
}

.scope-hint {
  color: #555;
  font-size: 0.875rem;
}

.scope-error {
  color: #b00020;
}

.scope-search {
  display: flex;
  gap: 0.5rem;
}

.scope-results,
.scope-selected {
  list-style: none;
  margin: 0.5rem 0;
  max-height: 12rem;
  overflow-y: auto;
  padding: 0;
}

.scope-selected li {
  display: flex;
  gap: 0.5rem;
  justify-content: space-between;
}
</style>

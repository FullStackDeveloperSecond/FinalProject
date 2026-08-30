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
import { productStatusLabels } from '../../features/catalog-reference/types'

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
const pageNumber = ref(1)

const searchParams = computed(() => ({
  q: submittedTerm.value,
  pageNumber: pageNumber.value,
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
  // 換關鍵字要回到第一頁，否則會拿新關鍵字去查舊頁碼。
  pageNumber.value = 1
}

/**
 * 看下一頁。
 *
 * `hasMore` 沒有翻頁的方法就只是一個沒有出口的狀態 —— 介面顯示「還有更多」
 * 卻讓管理員無從取得，等於那些商品選不到（alex #69 P2 的同一個問題，
 * 端點修好之後這裡也要跟上）。
 */
function nextPage() {
  pageNumber.value += 1
}

function previousPage() {
  pageNumber.value = Math.max(pageNumber.value - 1, 1)
}

/** 這個商品能不能新增。停售品只會出現在既有已選清單，不會出現在搜尋結果。 */
function describeOption(option: { name: string, code: string, status: string }): string {
  return `${option.name}（${option.code}）`
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
  if (option === undefined) {
    return publicId
  }

  // 停售品要標出來：它仍然生效，但管理員多半會想改掉。
  const status = option.isSelectable ? '' : `（${productStatusLabels[option.status]}）`
  return `${option.name}（${option.code}）${status}`
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
            :disabled="!option.isSelectable"
            @change="toggle(option.publicId)"
          >
          {{ describeOption(option) }}
          <span class="scope-status">{{ productStatusLabels[option.status] }}</span>
        </label>
      </li>
    </ul>

    <nav
      v-if="results && (results.hasMore || pageNumber > 1)"
      class="scope-pages"
      :aria-label="`${props.label}分頁`"
    >
      <button
        type="button"
        :disabled="pageNumber <= 1"
        @click="previousPage"
      >
        上一頁
      </button>
      <span>第 {{ pageNumber }} 頁</span>
      <button
        type="button"
        :disabled="!results.hasMore"
        @click="nextPage"
      >
        下一頁
      </button>
    </nav>

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

.scope-pages {
  align-items: center;
  display: flex;
  gap: 0.5rem;
}

.scope-status {
  color: #555;
  font-size: 0.8125rem;
}

.scope-selected li {
  display: flex;
  gap: 0.5rem;
  justify-content: space-between;
}
</style>

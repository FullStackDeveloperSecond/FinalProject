<script setup lang="ts">
/**
 * C-14 的「示範門市」選擇器。超取配送方式（`requiresStore`）必須選一家門市，結帳才送得出去。
 *
 * 送給後端的只有門市的 PublicId——名稱、地址等顯示資料由後端在建單時自己快照，前台不回傳價格或
 * 快照欄位（M功能桌面UI與Route規格 C-14：「只送識別與使用者輸入，不送價格」）。
 */
import { computed, reactive, ref } from 'vue'
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { useConvenienceStoreSearch } from '../useShipping'
import type { ConvenienceStoreOptionDto } from '../types'

/**
 * 組長 PR #79 round-2 review item 2：只靠「目前搜尋結果裡找得到」還原選取，等於要求父層先搜尋、
 * 而且該門市正好落在目前這一頁——回到上一步或剛掛載時根本顯示不出既有選擇。所以父層若已經有
 * 選取，就連同顯示用摘要一起傳進來。
 *
 * round-3 review [P2]：上一版把摘要放在 `update:modelValue` 的第二個參數，那是拿不到的——`v-model`
 * 編譯後只會把第一個 `$event` 寫回 model，第二個參數直接被丟掉，父層永遠存不到摘要。改成兩個各自
 * 獨立的具名 model：
 *
 *     v-model="storePublicId" v-model:selected-summary="storeSummary"
 *
 * 主 model 仍是 PublicId——那是結帳送出去的唯一欄位（名稱／地址由後端建單時自己快照）；摘要是
 * 純顯示用的第二個 model，父層保存它才能在重新掛載時原樣傳回。
 */
const props = defineProps<{
  modelValue: string | null
  selectedSummary?: ConvenienceStoreOptionDto | null
}>()
const emit = defineEmits<{
  'update:modelValue': [string | null]
  'update:selectedSummary': [ConvenienceStoreOptionDto | null]
}>()

// 搜尋條件只綁草稿，按下搜尋才套用並把頁碼歸 1（與後台頁一致的理由：避免「新條件配舊頁碼」）。
const draft = reactive({ city: '', district: '', q: '' })
const applied = reactive({ city: '', district: '', q: '' })
const pageNumber = ref(1)
const hasSearched = ref(false)

const searchParams = computed(() => ({
  city: applied.city || undefined,
  district: applied.district || undefined,
  q: applied.q || undefined,
  pageNumber: pageNumber.value,
  pageSize: 20,
}))

// 門市共 100 筆，一次全撈對顧客沒有意義；先搜尋才查，避免一進結帳頁就打一支沒有條件的清單。
const { data: result, isPending, isError, error, refetch } = useConvenienceStoreSearch(
  searchParams,
  computed(() => hasSearched.value),
)

const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

function search() {
  applied.city = draft.city
  applied.district = draft.district
  applied.q = draft.q
  pageNumber.value = 1
  hasSearched.value = true
}

function goToPage(next: number) {
  pageNumber.value = next
}

const pickedStore = ref<ConvenienceStoreOptionDto | null>(null)

/**
 * 自我審查發現：只記住「這次點選的門市」的話，當 modelValue 是由父層帶進來的（例如回上一步、
 * 草稿還原）就永遠不顯示已選門市。改成先用這次點選的，找不到再從目前搜尋結果裡對回來。
 */
const selectedStore = computed<ConvenienceStoreOptionDto | null>(() => {
  if (!props.modelValue) {
    return null
  }
  if (pickedStore.value?.publicId === props.modelValue) {
    return pickedStore.value
  }
  // 父層帶進來的摘要優先——不需要先搜尋就能顯示；找不到才退回目前結果頁。
  if (props.selectedSummary?.publicId === props.modelValue) {
    return props.selectedSummary
  }
  return result.value?.items.find((store) => store.publicId === props.modelValue) ?? null
})

function select(store: ConvenienceStoreOptionDto) {
  pickedStore.value = store
  emit('update:modelValue', store.publicId)
  // 兩個 model 一起更新：父層保存了摘要，重新掛載時才傳得回來。
  emit('update:selectedSummary', store)
}

function clearSelection() {
  pickedStore.value = null
  emit('update:modelValue', null)
  emit('update:selectedSummary', null)
}
</script>

<template>
  <section
    class="store-picker"
    aria-label="選擇取貨門市"
  >
    <p
      v-if="selectedStore"
      class="store-picker__selected"
      aria-live="polite"
    >
      已選門市：{{ selectedStore.name }}（{{ selectedStore.storeCode }}）— {{ selectedStore.city }}{{ selectedStore.district }}{{ selectedStore.address }}
      <button
        type="button"
        @click="clearSelection"
      >
        重新選擇
      </button>
    </p>

    <form
      class="store-picker__filters"
      aria-label="門市搜尋"
      @submit.prevent="search"
    >
      <label>
        縣市
        <input
          v-model="draft.city"
          aria-label="門市縣市"
          maxlength="60"
        >
      </label>
      <label>
        行政區
        <input
          v-model="draft.district"
          aria-label="門市行政區"
          maxlength="60"
        >
      </label>
      <label>
        門市名稱或代碼
        <input
          v-model="draft.q"
          aria-label="門市關鍵字"
          maxlength="64"
        >
      </label>
      <button type="submit">
        搜尋門市
      </button>
    </form>

    <p
      v-if="!hasSearched"
      class="store-picker__hint"
    >
      請輸入縣市或關鍵字後搜尋門市。本站門市為專題展示用的虛構資料。
    </p>
    <LoadingState
      v-else-if="isPending"
      label="門市搜尋中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />
    <EmptyState
      v-else-if="(result?.items.length ?? 0) === 0"
      title="沒有符合條件的門市"
    />
    <template v-else>
      <ul class="store-picker__list">
        <li
          v-for="store in result!.items"
          :key="store.publicId"
          class="store-picker__item"
        >
          <div>
            <p class="store-picker__name">
              {{ store.name }}
              <span class="store-picker__code">{{ store.storeCode }}</span>
              <span
                v-if="store.isDemoData"
                class="store-picker__badge"
              >展示資料</span>
            </p>
            <p class="store-picker__address">
              {{ store.city }}{{ store.district }}{{ store.address }}
            </p>
          </div>
          <button
            type="button"
            :disabled="modelValue === store.publicId"
            @click="select(store)"
          >
            {{ modelValue === store.publicId ? '已選擇' : '選擇' }}
          </button>
        </li>
      </ul>

      <div
        v-if="totalPages > 1"
        class="store-picker__pagination"
      >
        <button
          type="button"
          :disabled="pageNumber <= 1"
          @click="goToPage(pageNumber - 1)"
        >
          上一頁
        </button>
        <span>{{ pageNumber }} / {{ totalPages }}</span>
        <button
          type="button"
          :disabled="pageNumber >= totalPages"
          @click="goToPage(pageNumber + 1)"
        >
          下一頁
        </button>
      </div>
    </template>
  </section>
</template>

<style scoped>
.store-picker__filters {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.75rem;
  margin-block-end: 1rem;
}

.store-picker__filters label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
}

.store-picker__selected {
  padding: 0.75rem;
  border: 1px solid #bbf7d0;
  border-radius: 0.5rem;
  background: #f0fdf4;
  margin-block-end: 1rem;
}

.store-picker__hint {
  color: #6b7280;
  font-size: 0.875rem;
}

.store-picker__list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.store-picker__item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.75rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
}

.store-picker__name {
  margin: 0;
  font-weight: 600;
}

.store-picker__code {
  margin-inline-start: 0.375rem;
  color: #6b7280;
  font-weight: 400;
  font-size: 0.8125rem;
}

.store-picker__badge {
  margin-inline-start: 0.375rem;
  padding: 0.125rem 0.375rem;
  border-radius: 0.25rem;
  background: #e0f2fe;
  color: #075985;
  font-size: 0.75rem;
  font-weight: 400;
}

.store-picker__address {
  margin: 0.25rem 0 0;
  color: #6b7280;
  font-size: 0.8125rem;
}

.store-picker__pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  margin-block-start: 1rem;
}
</style>

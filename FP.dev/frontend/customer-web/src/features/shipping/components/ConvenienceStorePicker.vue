<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ErrorState, LoadingState, PagePager } from '@doselect/web-shared/components'
import { useConvenienceStoreRegions, useConvenienceStoreSearch } from '../useShipping'
import type { ConvenienceStoreOptionDto } from '../types'

const props = defineProps<{
  modelValue: string | null
  selectedSummary?: ConvenienceStoreOptionDto | null
}>()
const emit = defineEmits<{
  'update:modelValue': [string | null]
  'update:selectedSummary': [ConvenienceStoreOptionDto | null]
}>()

const providerCode = ref(props.selectedSummary?.providerCode ?? '')
const city = ref(props.selectedSummary?.city ?? '')
const district = ref(props.selectedSummary?.district ?? '')
const cityPage = ref(1)
const districtPage = ref(1)
const storePage = ref(1)
const pickedStore = ref<ConvenienceStoreOptionDto | null>(null)
const cities = useConvenienceStoreRegions(
  computed(() => ({ providerCode: providerCode.value, pageNumber: cityPage.value })),
  computed(() => Boolean(providerCode.value)),
)
const districts = useConvenienceStoreRegions(
  computed(() => ({ providerCode: providerCode.value, city: city.value, pageNumber: districtPage.value })),
  computed(() => Boolean(providerCode.value && city.value)),
)
const stores = useConvenienceStoreSearch(
  computed(() => ({ providerCode: providerCode.value, city: city.value, district: district.value, pageNumber: storePage.value, pageSize: 100 })),
  computed(() => Boolean(providerCode.value && city.value && district.value)),
)
const selectedStore = computed(() => {
  if (!props.modelValue) return null
  return [pickedStore.value, props.selectedSummary, ...(stores.data.value?.items ?? [])]
    .find(store => store?.publicId === props.modelValue) ?? null
})

function clearSelection(): void {
  pickedStore.value = null
  emit('update:modelValue', null)
  emit('update:selectedSummary', null)
}
watch(providerCode, () => {
  city.value = ''
  district.value = ''
  cityPage.value = districtPage.value = storePage.value = 1
  clearSelection()
}, { flush: 'sync' })
watch(city, () => {
  district.value = ''
  districtPage.value = storePage.value = 1
  clearSelection()
}, { flush: 'sync' })
watch(district, () => {
  storePage.value = 1
  clearSelection()
}, { flush: 'sync' })

function selectStore(event: Event): void {
  if (stores.isPending.value || stores.isError.value) return
  const id = (event.target as HTMLSelectElement).value
  const store = stores.data.value?.items.find(candidate => candidate.publicId === id)
  if (!store) { clearSelection(); return }
  pickedStore.value = store
  // 僅 PublicId 用於建單，名稱與地址摘要只供顯示；後端仍重新確認門市。
  emit('update:modelValue', store.publicId)
  emit('update:selectedSummary', store)
}
</script>

<template>
  <section
    class="store-picker"
    aria-label="選擇取貨門市"
  >
    <p>請依序選擇超商、縣市、行政區與門市，選單變更會立即更新。標示 * 為必填。</p>
    <p>本站門市為專題展示用的虛構資料，並非即時官方門市；貨到付款是否可用依本次配送與商品條件顯示。</p>
    <div class="store-picker__filters">
      <label>
        超商品牌 *
        <select
          v-model="providerCode"
          aria-label="超商品牌"
          required
        >
          <option value="">請選擇超商</option>
          <option value="7-11">7-ELEVEN（7-11）</option>
          <option value="FamilyMart">FamilyMart（全家）</option>
        </select>
      </label>
      <label>
        縣市 *
        <select
          v-model="city"
          aria-label="門市縣市"
          :disabled="!providerCode || cities.isPending.value || cities.isError.value"
          required
        >
          <option value="">請選擇縣市</option>
          <option
            v-for="item in cities.data.value?.items"
            :key="item"
            :value="item"
          >{{ item }}</option>
        </select>
      </label>
      <label>
        行政區 *
        <select
          v-model="district"
          aria-label="門市行政區"
          :disabled="!city || districts.isPending.value || districts.isError.value"
          required
        >
          <option value="">請選擇行政區</option>
          <option
            v-for="item in districts.data.value?.items"
            :key="item"
            :value="item"
          >{{ item }}</option>
        </select>
      </label>
      <label>
        取貨門市 *
        <select
          :value="modelValue ?? ''"
          aria-label="取貨門市"
          :disabled="!district || stores.isPending.value || stores.isError.value"
          required
          @change="selectStore"
        >
          <option value="">請選擇門市</option>
          <option
            v-if="selectedStore && !stores.data.value?.items.some(item => item.publicId === selectedStore?.publicId)"
            :value="selectedStore.publicId"
          >
            {{ selectedStore.name }}（{{ selectedStore.storeCode }}）
          </option>
          <option
            v-for="store in stores.data.value?.items"
            :key="store.publicId"
            :value="store.publicId"
          >
            {{ store.name }}（{{ store.storeCode }}）— {{ store.address }}{{ store.isDemoData ? '［展示資料］' : '' }}
          </option>
        </select>
      </label>
    </div>
    <template
      v-for="entry in [
        { label: '縣市', query: cities, enabled: !!providerCode, page: cityPage, change: (n: number) => cityPage = n },
        { label: '行政區', query: districts, enabled: !!city, page: districtPage, change: (n: number) => districtPage = n },
        { label: '門市', query: stores, enabled: !!district, page: storePage, change: (n: number) => storePage = n },
      ]"
      :key="entry.label"
    >
      <template v-if="entry.enabled">
        <LoadingState
          v-if="entry.query.isPending.value"
          :label="`${entry.label}載入中`"
        />
        <ErrorState
          v-else-if="entry.query.isError.value"
          @retry="entry.query.refetch()"
        />
        <p
          v-else-if="entry.query.data.value?.items.length === 0"
          role="status"
        >
          沒有符合條件的{{ entry.label }}，請重新選擇。
        </p>
        <PagePager
          v-if="entry.query.data.value && (Number(entry.query.data.value.totalPages) > 1 || entry.page > 1)"
          :aria-label="`${entry.label}分頁`"
          :page="entry.page"
          :page-size="1"
          :total-records="Number(entry.query.data.value?.totalPages ?? 0)"
          :busy="entry.query.isPending.value"
          @update:page="entry.change"
        />
      </template>
    </template>
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
  </section>
</template>

<style scoped>
.store-picker__filters { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 14rem), 1fr)); gap: 0.75rem; }
.store-picker__filters label { display: flex; flex-direction: column; gap: 0.25rem; min-width: 0; }
.store-picker__filters select { width: 100%; }
.store-picker__selected { padding: 0.75rem; border: 1px solid #bbf7d0; border-radius: 0.5rem; background: #f0fdf4; }
</style>

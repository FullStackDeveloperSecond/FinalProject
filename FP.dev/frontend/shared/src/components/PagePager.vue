<script setup lang="ts">
import { computed, watch, watchEffect } from 'vue'
import Paginator from 'primevue/paginator'
import { usePrimeVue } from 'primevue/config'

const primeVue = usePrimeVue()
const pageLabel = (number: number) => (primeVue.config.locale?.aria?.pageLabel ?? '{page}').replace('{page}', String(number))

const props = defineProps<{
  /** Current page, 1-based. Two-way bound via `v-model:page`. */
  page: number
  /** Total number of records across all pages. Must be a non-negative integer. */
  totalRecords: number
  /** Records per page. Must be a positive integer. */
  pageSize: number
  /** Accessible name for the pager navigation landmark, from the host app's locale. */
  ariaLabel?: string
  busy?: boolean
}>()

const emit = defineEmits<{
  'update:page': [page: number]
}>()

const isPositiveInt = (value: number) => Number.isInteger(value) && value > 0
const isNonNegativeInt = (value: number) => Number.isInteger(value) && value >= 0

// An out-of-range `page` is not invalid input — it is correctable, and the watch below
// hands the consumer a clamped value. Only a non-finite `page` is unusable.
const pageUsable = computed(() => Number.isFinite(props.page))
const pageSizeValid = computed(() => isPositiveInt(props.pageSize))
const totalRecordsValid = computed(() => isNonNegativeInt(props.totalRecords))

const inputsValid = computed(
  () => pageUsable.value && pageSizeValid.value && totalRecordsValid.value,
)

// ── Render-only safe values ────────────────────────────────────────────────────
// Nothing below is allowed to reach PrimeVue as NaN, Infinity, zero or negative:
// Paginator divides by `rows` and offsets by `first`, so a bad prop would otherwise
// surface as NaN page counts or a negative offset.

/** Positive integer; 1 when `pageSize` is unusable. */
const safePageSize = computed(() => (pageSizeValid.value ? props.pageSize : 1))

/** Non-negative integer; 0 when `totalRecords` is unusable. */
const safeTotalRecords = computed(() => (totalRecordsValid.value ? props.totalRecords : 0))

/** At least 1, even when there are no records. */
const pageCount = computed(() =>
  Math.max(1, Math.ceil(safeTotalRecords.value / safePageSize.value)),
)

/** `page` clamped into [1, pageCount] — what the UI actually reflects. */
const safePage = computed(() =>
  pageUsable.value ? Math.min(Math.max(1, Math.trunc(props.page)), pageCount.value) : 1,
)

/** PrimeVue Paginator wants a 0-based record offset; always a non-negative integer. */
const first = computed(() => (safePage.value - 1) * safePageSize.value)

// Five nearby pages plus explicit endpoints; never pretend a cursor result has a total.
const visiblePages = computed(() => {
  const start = Math.max(1, Math.min(safePage.value - 2, pageCount.value - 4))
  return Array.from({ length: Math.min(5, pageCount.value) }, (_, index) => start + index)
})

if (import.meta.env.DEV) {
  watchEffect(() => {
    if (!pageSizeValid.value) {
      console.warn(
        `[PagePager] \`pageSize\` must be a positive integer; received ${props.pageSize}. `
        + 'Rendering with 1 and withholding page updates.',
      )
    }
    if (!totalRecordsValid.value) {
      console.warn(
        `[PagePager] \`totalRecords\` must be a non-negative integer; received ${props.totalRecords}. `
        + 'Rendering with 0 and withholding page updates.',
      )
    }
    if (!pageUsable.value) {
      console.warn(
        `[PagePager] \`page\` must be a finite number; received ${props.page}. `
        + 'Rendering page 1 and withholding page updates.',
      )
    }
  })
}

// When totalRecords / pageSize / page move the current page out of range, hand the
// consumer a corrected, in-range page. The equality guard makes this a fixed point:
// clamp(clamp(x)) === clamp(x), so the parent applying our value produces no further emit.
// Invalid input never reaches here — a corrected page derived from a fallback would be a
// guess, so the pager renders defensively and stays silent instead.
watch(
  [() => props.page, () => props.totalRecords, () => props.pageSize],
  () => {
    if (inputsValid.value && safePage.value !== props.page) {
      emit('update:page', safePage.value)
    }
  },
  { immediate: true, flush: 'post' },
)

function onPage(event: { page: number }) {
  if (!inputsValid.value || props.busy) {
    return
  }
  const next = event.page + 1
  if (next !== props.page) {
    emit('update:page', next)
  }
}
</script>

<template>
  <Paginator
    class="ds-page-pager"
    :aria-label="ariaLabel ?? $attrs['aria-label']"
    :first="first"
    :rows="safePageSize"
    :total-records="safeTotalRecords"
    @page="onPage"
  >
    <template #container>
      <div class="ds-page-pager__controls">
        <button
          type="button"
          :disabled="busy || !inputsValid || safePage <= 1"
          @click="onPage({ page: safePage - 2 })"
        >
          {{ primeVue.config.locale?.aria?.prevPageLabel }}
        </button>
        <template v-if="visiblePages[0]! > 1">
          <button
            type="button"
            :disabled="busy || !inputsValid"
            :aria-label="pageLabel(1)"
            @click="onPage({ page: 0 })"
          >
            1
          </button>
          <span
            v-if="visiblePages[0]! > 2"
            aria-hidden="true"
          >...</span>
        </template>
        <button
          v-for="number in visiblePages"
          :key="number"
          type="button"
          :disabled="busy || !inputsValid"
          :aria-label="pageLabel(number)"
          :aria-current="number === safePage ? 'page' : undefined"
          @click="onPage({ page: number - 1 })"
        >
          {{ number }}
        </button>
        <template v-if="visiblePages[visiblePages.length - 1]! < pageCount">
          <span
            v-if="visiblePages[visiblePages.length - 1]! < pageCount - 1"
            aria-hidden="true"
          >...</span>
          <button
            type="button"
            :disabled="busy || !inputsValid"
            :aria-label="pageLabel(pageCount)"
            @click="onPage({ page: pageCount - 1 })"
          >
            {{ pageCount }}
          </button>
        </template>
        <button
          type="button"
          :disabled="busy || !inputsValid || safePage >= pageCount"
          @click="onPage({ page: safePage })"
        >
          {{ primeVue.config.locale?.aria?.nextPageLabel }}
        </button>
      </div>
    </template>
  </Paginator>
</template>

<style scoped>
.ds-page-pager__controls { display: flex; flex-wrap: wrap; gap: .4rem; align-items: center; justify-content: center; }
.ds-page-pager__controls button { min-width: 2.5rem; min-height: 2.5rem; }
.ds-page-pager__controls [aria-current="page"] { font-weight: 700; outline: 2px solid currentColor; outline-offset: -2px; }
</style>

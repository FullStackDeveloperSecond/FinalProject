<script setup lang="ts">
import { computed, watch, watchEffect } from 'vue'
import Paginator from 'primevue/paginator'

const props = defineProps<{
  /** Current page, 1-based. Two-way bound via `v-model:page`. */
  page: number
  /** Total number of records across all pages. Must be a non-negative integer. */
  totalRecords: number
  /** Records per page. Must be a positive integer. */
  pageSize: number
  /** Accessible name for the pager navigation landmark, from the host app's locale. */
  ariaLabel: string
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
  if (!inputsValid.value) {
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
    :aria-label="ariaLabel"
    :first="first"
    :rows="safePageSize"
    :total-records="safeTotalRecords"
    @page="onPage"
  />
</template>

<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState, PagePager } from '@doselect/web-shared/components'
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { useAdminReturnListQuery } from '../../features/returns/queries'
import { formatDateTime, priorityLabels, reasonLabels, statusLabels } from '../../features/returns/labels'
import type { AdminReturnSortOrder, AdminReturnSummaryDto } from '../../features/returns/types'
import { endOfLocalDayExclusiveBoundary, startOfLocalDay } from '../../features/inventory/dateRange'

const route = useRoute()
const router = useRouter()
const statusOptions = Object.entries(statusLabels)
const reasonOptions = Object.entries(reasonLabels)
const sortOptions: Array<{ value: AdminReturnSortOrder, label: string }> = [
  { value: 'updatedDesc', label: '最近更新優先' },
  { value: 'updatedAsc', label: '最早更新優先' },
  { value: 'requestedDesc', label: '最新申請優先' },
  { value: 'requestedAsc', label: '最早申請優先' },
  { value: 'shipmentDeadlineAsc', label: '寄回期限近優先' },
  { value: 'shipmentDeadlineDesc', label: '寄回期限遠優先' },
]
const pageSizeOptions = [20, 50, 100] as const

const page = ref(1)
const search = ref('')
const appliedSearch = ref('')
const filtersState = reactive({
  status: '' as AdminReturnSummaryDto['status'] | '',
  reason: '',
  from: '',
  to: '',
  sort: 'updatedDesc' as AdminReturnSortOrder,
  pageSize: 20,
})
let restoringFromUrl = false

function queryValue(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function dateQueryValue(value: unknown): string {
  const candidate = queryValue(value)
  return /^\d{4}-\d{2}-\d{2}$/.test(candidate) ? candidate : ''
}

function restoreFromUrl() {
  restoringFromUrl = true
  const q = queryValue(route.query.q).trim()
  const status = queryValue(route.query.status)
  const reason = queryValue(route.query.reason)
  const sort = queryValue(route.query.sort)
  const requestedPage = Number(route.query.page)
  const requestedPageSize = Number(route.query.pageSize)

  search.value = q
  appliedSearch.value = q
  filtersState.status = statusOptions.some(([value]) => value === status)
    ? status as AdminReturnSummaryDto['status']
    : ''
  filtersState.reason = reasonOptions.some(([value]) => value === reason) ? reason : ''
  filtersState.from = dateQueryValue(route.query.from)
  filtersState.to = dateQueryValue(route.query.to)
  filtersState.sort = sortOptions.some(option => option.value === sort)
    ? sort as AdminReturnSortOrder
    : 'updatedDesc'
  filtersState.pageSize = pageSizeOptions.includes(requestedPageSize as typeof pageSizeOptions[number])
    ? requestedPageSize
    : 20
  page.value = Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1
  restoringFromUrl = false
}

watch(() => route.fullPath, restoreFromUrl, { immediate: true })

watch(search, (value, _previous, cleanup) => {
  const normalized = value.trim()
  if (restoringFromUrl || normalized === appliedSearch.value) return
  const timer = setTimeout(() => {
    appliedSearch.value = normalized
    page.value = 1
  }, 300)
  cleanup(() => clearTimeout(timer))
})

watch(
  () => [
    filtersState.status,
    filtersState.reason,
    filtersState.from,
    filtersState.to,
    filtersState.sort,
    filtersState.pageSize,
  ],
  () => {
    if (!restoringFromUrl) page.value = 1
  },
  { flush: 'sync' },
)

watch(
  () => ({
    q: appliedSearch.value,
    status: filtersState.status,
    reason: filtersState.reason,
    from: filtersState.from,
    to: filtersState.to,
    sort: filtersState.sort,
    pageSize: filtersState.pageSize,
    page: page.value,
  }),
  (state) => {
    if (restoringFromUrl) return
    const query: Record<string, string> = {}
    if (state.q) query.q = state.q
    if (state.status) query.status = state.status
    if (state.reason) query.reason = state.reason
    if (state.from) query.from = state.from
    if (state.to) query.to = state.to
    if (state.sort !== 'updatedDesc') query.sort = state.sort
    if (state.pageSize !== 20) query.pageSize = String(state.pageSize)
    if (state.page > 1) query.page = String(state.page)
    if (JSON.stringify(query) !== JSON.stringify(route.query)) {
      void router.replace({ query })
    }
  },
  { deep: true },
)

const filters = computed(() => ({
  PageNumber: page.value,
  PageSize: filtersState.pageSize,
  Statuses: filtersState.status ? [filtersState.status] : undefined,
  ReasonCodes: filtersState.reason ? [filtersState.reason] : undefined,
  From: filtersState.from ? startOfLocalDay(filtersState.from).toISOString() : undefined,
  To: filtersState.to ? endOfLocalDayExclusiveBoundary(filtersState.to).toISOString() : undefined,
  Q: appliedSearch.value || undefined,
  Sort: filtersState.sort,
}))
const { data, isPending, isFetching, isError, error, refetch } = useAdminReturnListQuery(filters)

function deadlineLabel(item: AdminReturnSummaryDto): string {
  if (!item.needsAttention || item.status !== 'awaitingShipment' || !item.returnShipmentDueAtUtc) return ''
  const due = Date.parse(item.returnShipmentDueAtUtc)
  if (!Number.isFinite(due)) return ''
  return due <= Date.now() ? '已逾期' : '即將逾期'
}
</script>

<template>
  <section aria-labelledby="admin-returns-title">
    <h1 id="admin-returns-title">
      退貨案件
    </h1>

    <form
      class="admin-returns__filters card"
      role="search"
      aria-label="退貨案件篩選"
      @submit.prevent
    >
      <label>
        退貨或訂單編號
        <input
          v-model="search"
          type="search"
          maxlength="100"
          aria-label="退貨或訂單編號"
          placeholder="輸入退貨或訂單編號"
        >
      </label>
      <label>
        退貨狀態
        <select
          v-model="filtersState.status"
          aria-label="退貨狀態"
        >
          <option value="">全部狀態</option>
          <option
            v-for="([value, label]) in statusOptions"
            :key="value"
            :value="value"
          >{{ label }}</option>
        </select>
      </label>
      <label>
        退貨原因
        <select
          v-model="filtersState.reason"
          aria-label="退貨原因"
        >
          <option value="">全部原因</option>
          <option
            v-for="([value, label]) in reasonOptions"
            :key="value"
            :value="value"
          >{{ label }}</option>
        </select>
      </label>
      <fieldset class="admin-returns__date-range">
        <legend>申請日期</legend>
        <label>
          從
          <input
            v-model="filtersState.from"
            type="date"
            aria-label="申請日期從"
            :max="filtersState.to || undefined"
          >
        </label>
        <label>
          到
          <input
            v-model="filtersState.to"
            type="date"
            aria-label="申請日期到"
            :min="filtersState.from || undefined"
          >
        </label>
      </fieldset>
      <label>
        排序
        <select
          v-model="filtersState.sort"
          aria-label="退貨案件排序"
        >
          <option
            v-for="option in sortOptions"
            :key="option.value"
            :value="option.value"
          >{{ option.label }}</option>
        </select>
      </label>
      <label>
        每頁筆數
        <select
          v-model.number="filtersState.pageSize"
          aria-label="每頁筆數"
        >
          <option
            v-for="value in pageSizeOptions"
            :key="value"
            :value="value"
          >{{ value }} 筆</option>
        </select>
      </label>
    </form>

    <LoadingState v-if="isPending" />
    <ErrorState
      v-else-if="isError"
      :description="isApiError(error) ? error.message : '請稍後再試一次。'"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      :trace-id="isApiError(error) ? error.traceId : undefined"
      @retry="refetch()"
    />
    <EmptyState
      v-else-if="data && data.items.length === 0"
      title="目前沒有退貨案件"
      description="調整篩選條件，或稍後再回來查看。"
    />
    <div
      v-else-if="data"
      class="table-scroll"
    >
      <table class="admin-returns__table">
        <thead>
          <tr>
            <th scope="col">
              退貨編號
            </th>
            <th scope="col">
              訂單編號
            </th>
            <th scope="col">
              狀態
            </th>
            <th scope="col">
              優先度
            </th>
            <th scope="col">
              品項數
            </th>
            <th scope="col">
              申請時間
            </th>
            <th scope="col">
              寄回期限
            </th>
            <th scope="col" />
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="item in data.items"
            :key="item.publicId"
            :class="{ 'admin-returns__row--attention': item.needsAttention }"
          >
            <td>
              <RouterLink :to="`/returns/${item.publicId}`">
                {{ item.returnNumber }}
              </RouterLink>
            </td>
            <td>{{ item.orderNumber }}</td>
            <td>{{ statusLabels[item.status] }}</td>
            <td>{{ priorityLabels[item.priority] }}</td>
            <td>{{ item.itemCount }}</td>
            <td>{{ formatDateTime(item.requestedAtUtc) }}</td>
            <td>
              {{ formatDateTime(item.returnShipmentDueAtUtc) }}
              <span
                v-if="deadlineLabel(item)"
                class="admin-returns__attention-badge"
              >{{ deadlineLabel(item) }}</span>
            </td>
            <td>
              <RouterLink :to="`/returns/${item.publicId}`">
                查看
              </RouterLink>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <PagePager
      v-if="data"
      v-model:page="page"
      :page-size="filtersState.pageSize"
      :total-records="Number(data.totalCount)"
      :busy="isFetching"
      aria-label="退貨案件分頁"
    />
    <p
      v-if="data"
      class="admin-returns__count"
    >
      共 {{ data.totalCount }} 筆
    </p>
  </section>
</template>

<style scoped>
.admin-returns__filters { display: flex; flex-wrap: wrap; align-items: end; gap: 1rem; margin: 1rem 0; padding: 1rem; }
.admin-returns__filters label { display: grid; gap: .4rem; }
.admin-returns__date-range { display: flex; gap: .75rem; margin: 0; padding: 0; border: 0; }
.admin-returns__date-range legend { margin-bottom: .4rem; color: var(--color-text-muted); font-size: .875rem; }
.admin-returns__table {
  width: 100%;
  border-collapse: collapse;
  margin-top: 1rem;
}

.admin-returns__table th,
.admin-returns__table td {
  padding: 0.625rem 0.75rem;
  border-bottom: 1px solid var(--color-border);
  text-align: left;
}

.admin-returns__row--attention {
  background: var(--color-warning-bg);
}

.admin-returns__attention-badge {
  margin-left: 0.5rem;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  background: var(--color-warning);
  color: var(--color-on-primary);
  font-size: 0.75rem;
  font-weight: 700;
}

.admin-returns__count {
  margin-top: 0.75rem;
  color: var(--color-text-muted);
  font-size: 0.875rem;
}
</style>

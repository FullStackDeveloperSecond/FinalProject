<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { defaultCaseWorkbenchPageSize, useCaseWorkbenchQuery } from '../../features/case-workbench/queries'
import type {
  CasePriority,
  CaseWorkbenchAssigneeFilter,
  CaseWorkbenchCaseType,
  CaseWorkbenchSortOrder,
} from '../../features/case-workbench/types'
import type { SupportTicketStatus } from '../../features/support/types'
import { formatDateTime, priorityLabels, statusLabels } from '../../features/support/labels'

// A-24 案件工作台：讀取既有 GET /api/v1/admin/case-workbench，欄位固定 12 欄（不自行擴張 DTO）。
// This slice is authorized for Support only. Return/Report filters stay hidden until their
// actor scope and detail routes are both available.
const caseTypeOptions: { value: CaseWorkbenchCaseType, label: string }[] = [
  { value: 'support', label: '客服案件' },
]
const priorityOptions: CasePriority[] = ['low', 'normal', 'high', 'urgent']
const statusOptions = Object.entries(statusLabels) as [SupportTicketStatus, string][]
const assigneeOptions: { value: CaseWorkbenchAssigneeFilter, label: string }[] = [
  { value: 'any', label: '全部承辦狀態' },
  { value: 'mine', label: '我的案件' },
  { value: 'unassigned', label: '未指派' },
  { value: 'assigned', label: '已指派' },
]

const route = useRoute()
const router = useRouter()

function queryValue(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

const initialCaseType = queryValue(route.query.caseType)
const initialPriority = queryValue(route.query.priority)
const initialStatus = queryValue(route.query.status)
const initialAssignee = queryValue(route.query.assignee)

const filters = reactive({
  caseType: (caseTypeOptions.some(option => option.value === initialCaseType)
    ? initialCaseType
    : '') as CaseWorkbenchCaseType | '',
  priority: (priorityOptions.includes(initialPriority as CasePriority)
    ? initialPriority
    : '') as CasePriority | '',
  status: (statusOptions.some(option => option[0] === initialStatus)
    ? initialStatus
    : '') as SupportTicketStatus | '',
  assignee: (assigneeOptions.some(option => option.value === initialAssignee)
    ? initialAssignee
    : 'any') as CaseWorkbenchAssigneeFilter,
  createdFrom: queryValue(route.query.createdFrom),
  createdTo: queryValue(route.query.createdTo),
  lastActivityFrom: queryValue(route.query.lastActivityFrom),
  lastActivityTo: queryValue(route.query.lastActivityTo),
  sort: (queryValue(route.query.sort) === 'oldest' ? 'oldest' : 'latest') as CaseWorkbenchSortOrder,
  overdueOnly: queryValue(route.query.overdue) === 'true',
  keyword: queryValue(route.query.keyword),
})

// Keyset (cursor) pagination has no "page N" concept — a stack of visited cursors is the
// simplest way to support "上一頁" without asking the backend for a total count. Mirrors
// SupportSlaQueuePage.vue's own pagination pattern exactly.
const cursorStack = ref<(string | undefined)[]>([undefined])
const filterFingerprint = computed(() => JSON.stringify({
  caseType: filters.caseType,
  priority: filters.priority,
  status: filters.status,
  assignee: filters.assignee,
  createdFrom: filters.createdFrom,
  createdTo: filters.createdTo,
  lastActivityFrom: filters.lastActivityFrom,
  lastActivityTo: filters.lastActivityTo,
  sort: filters.sort,
  overdueOnly: filters.overdueOnly,
  keyword: filters.keyword.trim(),
}))
const cursorFilterFingerprint = ref(filterFingerprint.value)
const currentCursor = computed(() =>
  cursorFilterFingerprint.value === filterFingerprint.value
    ? cursorStack.value[cursorStack.value.length - 1]
    : undefined)

function resetPagination() {
  cursorStack.value = [undefined]
  cursorFilterFingerprint.value = filterFingerprint.value
}

function syncUrl() {
  const query: Record<string, string> = {}
  if (filters.caseType) query.caseType = filters.caseType
  if (filters.status) query.status = filters.status
  if (filters.priority) query.priority = filters.priority
  if (filters.assignee !== 'any') query.assignee = filters.assignee
  if (filters.createdFrom) query.createdFrom = filters.createdFrom
  if (filters.createdTo) query.createdTo = filters.createdTo
  if (filters.lastActivityFrom) query.lastActivityFrom = filters.lastActivityFrom
  if (filters.lastActivityTo) query.lastActivityTo = filters.lastActivityTo
  if (filters.sort !== 'latest') query.sort = filters.sort
  if (filters.overdueOnly) query.overdue = 'true'
  if (filters.keyword.trim()) query.keyword = filters.keyword.trim()
  void router.replace({ query })
}

// flush:sync plus the fingerprint guard above guarantees a changed filter can never be paired
// with a cursor issued for the previous filter set, even during the same input event.
watch(filterFingerprint, () => {
  resetPagination()
  syncUrl()
}, { flush: 'sync' })

const queryFilters = computed(() => ({
  caseTypes: filters.caseType ? [filters.caseType] : undefined,
  statuses: filters.status ? [filters.status] : undefined,
  priorities: filters.priority ? [filters.priority] : undefined,
  assignee: filters.assignee,
  createdFrom: filters.createdFrom || undefined,
  createdTo: filters.createdTo || undefined,
  lastActivityFrom: filters.lastActivityFrom || undefined,
  lastActivityTo: filters.lastActivityTo || undefined,
  sort: filters.sort,
  overdueOnly: filters.overdueOnly || undefined,
  keyword: filters.keyword.trim() || undefined,
  cursor: currentCursor.value,
  pageSize: defaultCaseWorkbenchPageSize,
}))

const { data, isPending, isError, error, refetch } = useCaseWorkbenchQuery(queryFilters)

const canGoPrevious = computed(() => cursorStack.value.length > 1)
const canGoNext = computed(() => Boolean(data.value?.hasMore))

function goToNextPage() {
  const nextCursor = data.value?.nextCursor
  if (nextCursor) {
    cursorFilterFingerprint.value = filterFingerprint.value
    cursorStack.value = [...cursorStack.value, nextCursor]
  }
}

function goToPreviousPage() {
  if (canGoPrevious.value) {
    cursorStack.value = cursorStack.value.slice(0, -1)
  }
}

function setSort(sort: CaseWorkbenchSortOrder) {
  filters.sort = sort
}

// CaseWorkbenchItemDto.caseType is a plain string sourced straight from vw_CaseWorkbench's SQL
// literals ('Support'/'Return'/'Report' — PascalCase), unlike the CaseWorkbenchCaseType query
// parameter enum (camelCase-serialized: 'support'/'return'/'report'). The two must not be
// compared directly — normalize before matching either the type badge label or the detail route.
function normalizeCaseType(caseType: string): CaseWorkbenchCaseType | null {
  const normalized = caseType.toLowerCase()
  return caseTypeOptions.some(option => option.value === normalized)
    ? (normalized as CaseWorkbenchCaseType)
    : null
}

function caseTypeLabel(caseType: string): string {
  const normalized = normalizeCaseType(caseType)
  return caseTypeOptions.find(option => option.value === normalized)?.label ?? caseType
}

function statusLabel(status: string): string {
  return statusOptions.find(([value]) => value.toLowerCase() === status.toLowerCase())?.[1] ?? status
}

// A caseType this app can navigate to a real detail page for — Return/Report have no frontend
// route yet (only the backend Case Workbench read model already includes them). Never build a
// route string for those; show a disabled hint instead so a viewer never lands on a fake page.
function detailRouteFor(caseType: string): string | null {
  return normalizeCaseType(caseType) === 'support' ? 'support-ticket-detail' : null
}

const errorTitle = computed(() => {
  if (!isApiError(error.value)) {
    return '無法載入案件工作台'
  }

  switch (error.value.status) {
    case 401:
      return '需要登入'
    case 403:
      return '沒有權限查看案件工作台'
    default:
      return '無法載入案件工作台'
  }
})
</script>

<template>
  <section aria-labelledby="case-workbench-title">
    <h1 id="case-workbench-title">
      案件工作台
    </h1>
    <p class="view-lede">
      集中查看與追蹤目前可處理的客服案件。
    </p>

    <form
      class="case-workbench__filters card"
      aria-label="案件篩選"
      @submit.prevent="resetPagination"
    >
      <label class="case-workbench__filter-field">
        案件編號或關鍵字
        <input
          id="case-search"
          v-model.trim="filters.keyword"
          type="search"
          maxlength="100"
          placeholder="輸入案件編號或標題"
        >
      </label>

      <label class="case-workbench__filter-field">
        案件類型
        <select
          id="case-type-filter"
          v-model="filters.caseType"
        >
          <option value="">
            全部可見類型
          </option>
          <option
            v-for="option in caseTypeOptions"
            :key="option.value"
            :value="option.value"
          >
            {{ option.label }}
          </option>
        </select>
      </label>

      <label class="case-workbench__filter-field">
        狀態
        <select
          id="case-status-filter"
          v-model="filters.status"
        >
          <option value="">
            全部狀態
          </option>
          <option
            v-for="([value, label]) in statusOptions"
            :key="value"
            :value="value"
          >
            {{ label }}
          </option>
        </select>
      </label>

      <label class="case-workbench__filter-field">
        優先度
        <select
          id="case-priority-filter"
          v-model="filters.priority"
        >
          <option value="">
            全部優先度
          </option>
          <option
            v-for="option in priorityOptions"
            :key="option"
            :value="option"
          >
            {{ priorityLabels[option] }}
          </option>
        </select>
      </label>

      <label class="case-workbench__filter-field">
        承辦人
        <select
          id="case-assignee-filter"
          v-model="filters.assignee"
        >
          <option
            v-for="option in assigneeOptions"
            :key="option.value"
            :value="option.value"
          >
            {{ option.label }}
          </option>
        </select>
      </label>

      <fieldset class="case-workbench__date-range">
        <legend>建立日期</legend>
        <label>
          從
          <input
            id="case-created-from"
            v-model="filters.createdFrom"
            type="date"
            :max="filters.createdTo || undefined"
          >
        </label>
        <label>
          到
          <input
            id="case-created-to"
            v-model="filters.createdTo"
            type="date"
            :min="filters.createdFrom || undefined"
          >
        </label>
      </fieldset>

      <fieldset class="case-workbench__date-range">
        <legend>最後活動日期</legend>
        <label>
          從
          <input
            id="case-activity-from"
            v-model="filters.lastActivityFrom"
            type="date"
            :max="filters.lastActivityTo || undefined"
          >
        </label>
        <label>
          到
          <input
            id="case-activity-to"
            v-model="filters.lastActivityTo"
            type="date"
            :min="filters.lastActivityFrom || undefined"
          >
        </label>
      </fieldset>

      <fieldset class="case-workbench__sort">
        <legend>最後活動排序</legend>
        <button
          type="button"
          :aria-pressed="filters.sort === 'latest'"
          @click="setSort('latest')"
        >
          最新優先
        </button>
        <button
          type="button"
          :aria-pressed="filters.sort === 'oldest'"
          @click="setSort('oldest')"
        >
          最舊優先
        </button>
      </fieldset>

      <label class="case-workbench__filter-checkbox">
        <input
          v-model="filters.overdueOnly"
          type="checkbox"
        >
        只顯示已逾時
      </label>
    </form>

    <LoadingState v-if="isPending" />
    <ErrorState
      v-else-if="isError"
      :title="errorTitle"
      :description="isApiError(error) ? error.message : '請稍後再試一次。'"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      :trace-id="isApiError(error) ? error.traceId : undefined"
      @retry="refetch()"
    />
    <EmptyState
      v-else-if="data && data.items.length === 0"
      title="目前沒有符合條件的案件"
      description="調整篩選條件，或稍後再回來查看。"
    />
    <template v-else-if="data">
      <p
        class="case-workbench__result-count"
        aria-live="polite"
      >
        共 {{ data.totalCount }} 筆符合條件的案件
      </p>
      <div class="case-workbench__table-wrap card">
        <table class="case-workbench__table">
          <thead>
            <tr>
              <th scope="col">
                類型
              </th>
              <th scope="col">
                案件編號
              </th>
              <th scope="col">
                標題
              </th>
              <th scope="col">
                狀態
              </th>
              <th scope="col">
                優先度
              </th>
              <th scope="col">
                申請人
              </th>
              <th scope="col">
                承辦人
              </th>
              <th scope="col">
                建立時間
              </th>
              <th scope="col">
                最後活動時間
              </th>
              <th scope="col">
                SLA 到期時間
              </th>
              <th scope="col">
                逾時
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="item in data.items"
              :key="item.casePublicId"
              :class="{ 'case-workbench__row--overdue': item.isOverdue }"
            >
              <td data-label="類型">
                <span class="tag">{{ caseTypeLabel(item.caseType) }}</span>
              </td>
              <td data-label="案件編號">
                <RouterLink
                  v-if="detailRouteFor(item.caseType)"
                  :to="{ name: detailRouteFor(item.caseType)!, params: { ticketId: item.casePublicId } }"
                >
                  {{ item.caseNumber }}
                </RouterLink>
                <span
                  v-else
                  class="case-workbench__no-detail"
                  :title="`${caseTypeLabel(item.caseType)}目前無可用明細`"
                >
                  {{ item.caseNumber }}
                </span>
              </td>
              <td data-label="標題">
                {{ item.title }}
              </td>
              <td data-label="狀態">
                <span class="status-pill">{{ statusLabel(item.status) }}</span>
              </td>
              <td data-label="優先度">
                {{ priorityLabels[item.priority] }}
              </td>
              <td data-label="申請人">
                {{ item.requesterDisplay }}
              </td>
              <td data-label="承辦人">
                {{ item.assigneePublicId ?? '未指派' }}
              </td>
              <td data-label="建立時間">
                {{ formatDateTime(item.createdAtUtc) }}
              </td>
              <td data-label="最後活動時間">
                {{ formatDateTime(item.lastActivityAtUtc) }}
              </td>
              <td data-label="SLA 到期時間">
                {{ formatDateTime(item.slaDueAtUtc) }}
              </td>
              <td data-label="逾時">
                <span
                  class="status-pill"
                  :class="item.isOverdue ? 'status-pill--overdue' : 'status-pill--muted'"
                >
                  {{ item.isOverdue ? '已逾時' : '未逾時' }}
                </span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="case-workbench__pagination">
        <button
          type="button"
          :disabled="!canGoPrevious"
          @click="goToPreviousPage"
        >
          上一頁
        </button>
        <button
          type="button"
          :disabled="!canGoNext"
          @click="goToNextPage"
        >
          下一頁
        </button>
      </div>
    </template>
  </section>
</template>

<style scoped>
.case-workbench__filters {
  display: flex;
  flex-wrap: wrap;
  gap: 1.25rem;
  margin: 1.25rem 0;
  padding: 1rem;
}

.case-workbench__filters fieldset {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 0.75rem;
  border: none;
  padding: 0;
  margin: 0;
}

.case-workbench__filters legend {
  width: 100%;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
  margin-bottom: 0.25rem;
}

.case-workbench__filters label {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.875rem;
}

.case-workbench__filter-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.case-workbench__filter-checkbox {
  align-self: flex-end;
}

.case-workbench__date-range label {
  flex-direction: column;
  align-items: stretch;
}

.case-workbench__sort button[aria-pressed="true"] {
  border-color: var(--color-primary);
  background: var(--color-primary);
  color: var(--color-on-primary);
}

.case-workbench__result-count {
  color: var(--color-text-muted);
}

.case-workbench__table-wrap {
  padding: 0;
  overflow-x: auto;
  margin-top: 1.5rem;
}

.case-workbench__table {
  width: 100%;
  border-collapse: collapse;
}

.case-workbench__table th,
.case-workbench__table td {
  padding: 0.75rem 1.25rem;
  border-bottom: 1px solid var(--color-border-soft);
  text-align: left;
  white-space: nowrap;
}

.case-workbench__table thead th {
  color: var(--color-text-muted);
  font-size: 0.8125rem;
  font-weight: 600;
}

.case-workbench__table tbody tr:last-child td {
  border-bottom: none;
}

.case-workbench__row--overdue {
  background: var(--color-danger-bg);
}

.case-workbench__no-detail {
  color: var(--color-text-muted);
  cursor: help;
  text-decoration: underline dotted;
}

.status-pill--overdue {
  background: var(--color-danger-bg);
  color: var(--color-danger);
}

.case-workbench__pagination {
  display: flex;
  gap: 0.75rem;
  margin-top: 1.25rem;
}

</style>

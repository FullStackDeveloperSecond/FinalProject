<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref, watch } from 'vue'
import { defaultCaseWorkbenchPageSize, useCaseWorkbenchQuery } from '../../features/case-workbench/queries'
import type { CasePriority, CaseWorkbenchCaseType } from '../../features/case-workbench/types'
import { formatDateTime, priorityLabels } from '../../features/support/labels'

// A-24 案件工作台：讀取既有 GET /api/v1/admin/case-workbench，欄位固定 12 欄（不自行擴張 DTO）。
// This slice is authorized for Support only. Return/Report filters stay hidden until their
// actor scope and detail routes are both available.
const caseTypeOptions: { value: CaseWorkbenchCaseType, label: string }[] = [
  { value: 'support', label: '客服案件' },
]
const priorityOptions: CasePriority[] = ['low', 'normal', 'high', 'urgent']

const filters = reactive({
  caseTypes: [] as CaseWorkbenchCaseType[],
  priorities: [] as CasePriority[],
  statusesInput: '',
  assigneePublicId: '',
  overdueOnly: false,
  keyword: '',
})

// Keyset (cursor) pagination has no "page N" concept — a stack of visited cursors is the
// simplest way to support "上一頁" without asking the backend for a total count. Mirrors
// SupportSlaQueuePage.vue's own pagination pattern exactly.
const cursorStack = ref<(string | undefined)[]>([undefined])
const filterFingerprint = computed(() => JSON.stringify({
  caseTypes: [...filters.caseTypes].sort(),
  priorities: [...filters.priorities].sort(),
  statusesInput: filters.statusesInput,
  assigneePublicId: filters.assigneePublicId,
  overdueOnly: filters.overdueOnly,
  keyword: filters.keyword,
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

// flush:sync plus the fingerprint guard above guarantees a changed filter can never be paired
// with a cursor issued for the previous filter set, even during the same input event.
watch(filterFingerprint, resetPagination, { flush: 'sync' })

const queryFilters = computed(() => ({
  caseTypes: filters.caseTypes.length > 0 ? filters.caseTypes : undefined,
  statuses: filters.statusesInput
    ? filters.statusesInput.split(',').map(value => value.trim()).filter(Boolean)
    : undefined,
  priorities: filters.priorities.length > 0 ? filters.priorities : undefined,
  assigneePublicId: filters.assigneePublicId || undefined,
  overdueOnly: filters.overdueOnly || undefined,
  keyword: filters.keyword || undefined,
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

function toggleCaseType(value: CaseWorkbenchCaseType) {
  const index = filters.caseTypes.indexOf(value)
  if (index === -1) {
    filters.caseTypes.push(value)
  }
  else {
    filters.caseTypes.splice(index, 1)
  }
  resetPagination()
}

function togglePriority(value: CasePriority) {
  const index = filters.priorities.indexOf(value)
  if (index === -1) {
    filters.priorities.push(value)
  }
  else {
    filters.priorities.splice(index, 1)
  }
  resetPagination()
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
      客服案件清單依最後活動時間排序。後端已依角色與 Actor Scope 過濾；退貨與檢舉將在各自授權範圍與明細頁完成後開放。
    </p>

    <form
      class="case-workbench__filters card"
      aria-label="案件篩選"
      @submit.prevent="resetPagination"
    >
      <fieldset>
        <legend>案件類型</legend>
        <label
          v-for="option in caseTypeOptions"
          :key="option.value"
        >
          <input
            type="checkbox"
            :checked="filters.caseTypes.includes(option.value)"
            @change="toggleCaseType(option.value)"
          >
          {{ option.label }}
        </label>
      </fieldset>

      <fieldset>
        <legend>優先度</legend>
        <label
          v-for="option in priorityOptions"
          :key="option"
        >
          <input
            type="checkbox"
            :checked="filters.priorities.includes(option)"
            @change="togglePriority(option)"
          >
          {{ priorityLabels[option] }}
        </label>
      </fieldset>

      <label class="case-workbench__filter-field">
        狀態代碼（以逗號分隔）
        <input
          v-model="filters.statusesInput"
          type="text"
          placeholder="open,inProgress"
          @change="resetPagination"
        >
      </label>

      <label class="case-workbench__filter-field">
        承辦人 PublicId
        <input
          v-model="filters.assigneePublicId"
          type="text"
          placeholder="guid"
          @change="resetPagination"
        >
      </label>

      <label class="case-workbench__filter-field">
        關鍵字
        <input
          v-model="filters.keyword"
          type="text"
          @change="resetPagination"
        >
      </label>

      <label class="case-workbench__filter-checkbox">
        <input
          v-model="filters.overdueOnly"
          type="checkbox"
          @change="resetPagination"
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
                  :title="`${caseTypeLabel(item.caseType)}明細頁面尚未上線`"
                >
                  {{ item.caseNumber }}
                </span>
              </td>
              <td data-label="標題">
                {{ item.title }}
              </td>
              <td data-label="狀態">
                <span class="status-pill">{{ item.status }}</span>
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

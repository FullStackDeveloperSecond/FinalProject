<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref } from 'vue'
import { defaultSlaPageSize, useSupportSlaQueueQuery } from '../../features/support/queries'
import { formatDateTime, formatSlaUsage, priorityLabels, statusLabels } from '../../features/support/labels'

// Keyset (cursor) pagination has no "page N" concept — a stack of visited cursors is the
// simplest way to support "上一頁" without asking the backend for a total count.
const cursorStack = ref<(string | undefined)[]>([undefined])
const currentCursor = computed(() => cursorStack.value[cursorStack.value.length - 1])

const { data, isPending, isError, error, refetch } = useSupportSlaQueueQuery(() => ({
  pageSize: defaultSlaPageSize,
  cursor: currentCursor.value,
}))

const canGoPrevious = computed(() => cursorStack.value.length > 1)
const canGoNext = computed(() => Boolean(data.value?.hasMore))

function goToNextPage() {
  const nextCursor = data.value?.nextCursor
  if (nextCursor) {
    cursorStack.value = [...cursorStack.value, nextCursor]
  }
}

function goToPreviousPage() {
  if (canGoPrevious.value) {
    cursorStack.value = cursorStack.value.slice(0, -1)
  }
}

const errorTitle = computed(() => {
  if (!isApiError(error.value)) {
    return '無法載入 SLA 佇列'
  }

  switch (error.value.status) {
    case 401:
      return '需要登入'
    case 403:
      return '沒有權限查看 SLA 佇列'
    default:
      return '無法載入 SLA 佇列'
  }
})
</script>

<template>
  <section aria-labelledby="support-sla-queue-title">
    <h1 id="support-sla-queue-title">
      客服 SLA 佇列
    </h1>
    <p class="view-lede">
      依到期時間排序的待處理案件，逾時案件會優先顯示。
    </p>

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
      title="目前沒有待處理案件"
      description="SLA 佇列目前沒有案件，稍後再回來查看。"
    />
    <template v-else-if="data">
      <div class="sla-queue__table-wrap card">
        <table class="sla-queue__table">
          <thead>
            <tr>
              <th scope="col">
                案件編號
              </th>
              <th scope="col">
                優先度
              </th>
              <th scope="col">
                狀態
              </th>
              <th scope="col">
                受理人
              </th>
              <th scope="col">
                SLA 到期時間
              </th>
              <th scope="col">
                SLA 使用率
              </th>
              <th scope="col">
                最後活動時間
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="item in data.items"
              :key="item.ticketPublicId"
              :class="{ 'sla-queue__row--overdue': item.isOverdue }"
            >
              <td data-label="案件編號">
                <RouterLink :to="`/support/tickets/${item.ticketPublicId}`">
                  {{ item.ticketNumber }}
                </RouterLink>
              </td>
              <td data-label="優先度">
                {{ priorityLabels[item.priority] }}
              </td>
              <td data-label="狀態">
                <span class="status-pill">{{ statusLabels[item.status] }}</span>
              </td>
              <td data-label="受理人">
                {{ item.assignee?.displayName ?? '未指派' }}
              </td>
              <td data-label="SLA 到期時間">
                <span
                  class="status-pill"
                  :class="item.isOverdue ? 'status-pill--overdue' : 'status-pill--muted'"
                >
                  {{ item.isOverdue ? '已逾時' : '未逾時' }}
                </span>
                {{ formatDateTime(item.effectiveDueAtUtc) }}
              </td>
              <td data-label="SLA 使用率">
                {{ formatSlaUsage(Number(item.usageRatio)) }}
              </td>
              <td data-label="最後活動時間">
                {{ formatDateTime(item.lastActivityAtUtc) }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="sla-queue__pagination">
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
.sla-queue__table-wrap {
  padding: 0;
  overflow-x: auto;
  margin-top: 1.5rem;
}

.sla-queue__table {
  width: 100%;
  border-collapse: collapse;
}

.sla-queue__table th,
.sla-queue__table td {
  padding: 0.75rem 1.25rem;
  border-bottom: 1px solid var(--color-border-soft);
  text-align: left;
  white-space: nowrap;
}

.sla-queue__table thead th {
  color: var(--color-text-muted);
  font-size: 0.8125rem;
  font-weight: 600;
}

.sla-queue__table tbody tr:last-child td {
  border-bottom: none;
}

.sla-queue__row--overdue {
  background: var(--color-danger-bg);
}

.status-pill--overdue {
  background: var(--color-danger-bg);
  color: var(--color-danger);
  margin-inline-end: 0.5rem;
}

.status-pill--muted {
  margin-inline-end: 0.5rem;
}

.sla-queue__pagination {
  display: flex;
  gap: 0.75rem;
  margin-top: 1.25rem;
}
</style>

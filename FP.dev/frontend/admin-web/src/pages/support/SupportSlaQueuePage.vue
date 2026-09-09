<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState, PagePager } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref, watch } from 'vue'
import type { CasePriority, SupportTicketStatus } from '../../features/support/types'
import { defaultSlaPageSize, useSupportSlaQueueQuery } from '../../features/support/queries'
import { formatDateTime, formatSlaUsage, priorityLabels, statusLabels } from '../../features/support/labels'

// Request server-counted pages so filters and page numbers describe the entire authorized queue.
const page = ref(1)
const search = ref('')
const appliedSearch = ref('')
const filters = reactive({ status: '' as SupportTicketStatus | '', priority: '' as CasePriority | '', onlyOverdue: false, assignee: 'all', sort: 'deadline' })
watch(search, (value, _old, cleanup) => { const timer = setTimeout(() => { appliedSearch.value = value.trim(); page.value = 1 }, 300); cleanup(() => clearTimeout(timer)) })
watch(filters, () => { page.value = 1 })

const { data, isPending, isError, error, refetch } = useSupportSlaQueueQuery(() => ({
  pageSize: defaultSlaPageSize,
  pageNumber: page.value, search: appliedSearch.value || undefined, status: filters.status || undefined, priority: filters.priority || undefined, onlyOverdue: filters.onlyOverdue, assignee: filters.assignee, sort: filters.sort,
}))


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

    <div class="sla-queue__filters">
      <label>案件編號<input
        v-model="search"
        type="search"
        maxlength="100"
      ></label>
      <label>狀態<select v-model="filters.status"><option value="">全部待處理狀態</option><option
        v-for="value in (['open', 'assigned', 'inProgress', 'waitingForCustomer', 'waitingForInternal'] as const)"
        :key="value"
        :value="value"
      >{{ statusLabels[value] }}</option></select></label>
      <label>優先度<select v-model="filters.priority"><option value="">全部</option><option
        v-for="(label, value) in priorityLabels"
        :key="value"
        :value="value"
      >{{ label }}</option></select></label>
      <label>承辦人<select v-model="filters.assignee"><option value="all">全部可見案件</option><option value="mine">由我承辦</option><option value="unassigned">尚未指派</option></select></label>
      <label>排序<select v-model="filters.sort"><option value="deadline">逾時優先／到期時間</option><option value="recent">最近活動優先</option></select></label>
      <label><input
        v-model="filters.onlyOverdue"
        type="checkbox"
      >只顯示已逾時</label>
    </div>

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

      <PagePager
        v-model:page="page"
        :page-size="defaultSlaPageSize"
        :total-records="Number(data?.totalCount ?? 0)"
        aria-label="客服 SLA 分頁"
      />
    </template>
  </section>
</template>

<style scoped>
.sla-queue__filters { display: flex; flex-wrap: wrap; gap: 1rem; align-items: end; }
.sla-queue__filters label { display: grid; gap: .35rem; }
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
  /* The horizontal scroll wrapper is the sticky containing block, not the page. */
  top: 0;
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

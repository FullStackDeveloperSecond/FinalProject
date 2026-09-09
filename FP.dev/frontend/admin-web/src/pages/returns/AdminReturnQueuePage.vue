<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState, PagePager } from '@doselect/web-shared/components'
import { computed, ref, watch } from 'vue'
import { isApiError } from '@doselect/web-shared/api'
import { useAdminReturnListQuery } from '../../features/returns/queries'
import { formatDateTime, priorityLabels, statusLabels } from '../../features/returns/labels'
import type { AdminReturnSummaryDto } from '../../features/returns/types'

const page = ref(1)
const status = ref<AdminReturnSummaryDto['status'] | ''>('')
const search = ref('')
const appliedSearch = ref('')
watch(search, (value, _previous, cleanup) => {
  const timer = setTimeout(() => { appliedSearch.value = value.trim(); page.value = 1 }, 300)
  cleanup(() => clearTimeout(timer))
})
watch(status, () => { page.value = 1 })
const filters = computed(() => ({
  PageNumber: page.value, PageSize: 20,
  Statuses: status.value ? [status.value] : undefined,
  Q: appliedSearch.value || undefined,
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

    <div
      class="admin-returns__filters"
      role="search"
      aria-label="退貨案件篩選"
    >
      <label>
        退貨編號
        <input
          v-model="search"
          type="search"
          maxlength="100"
          aria-label="退貨編號"
          placeholder="輸入退貨編號"
        >
      </label>
      <label>
        退貨狀態
        <select
          v-model="status"
          aria-label="退貨狀態"
        >
          <option value="">全部狀態</option>
          <option
            v-for="(label, value) in statusLabels"
            :key="value"
            :value="value"
          >{{ label }}</option>
        </select>
      </label>
    </div>

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
      description="有新的退貨申請時會顯示在這裡。"
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
      :page-size="20"
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
.admin-returns__filters { display: flex; flex-wrap: wrap; gap: 1rem; margin: 1rem 0; }
.admin-returns__filters label { display: grid; gap: .4rem; }
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

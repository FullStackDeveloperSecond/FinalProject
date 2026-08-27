<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { useAdminReturnListQuery } from '../../features/returns/queries'
import { formatDateTime, priorityLabels, statusLabels } from '../../features/returns/labels'

const { data, isPending, isError, error, refetch } = useAdminReturnListQuery()
</script>

<template>
  <section aria-labelledby="admin-returns-title">
    <h1 id="admin-returns-title">
      退貨案件
    </h1>

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
    <table
      v-else-if="data"
      class="admin-returns__table"
    >
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
              v-if="item.needsAttention"
              class="admin-returns__attention-badge"
            >即將逾期</span>
          </td>
          <td>
            <RouterLink :to="`/returns/${item.publicId}`">
              查看
            </RouterLink>
          </td>
        </tr>
      </tbody>
    </table>
    <p
      v-if="data"
      class="admin-returns__count"
    >
      共 {{ data.totalCount }} 筆
    </p>
  </section>
</template>

<style scoped>
.admin-returns__table {
  width: 100%;
  border-collapse: collapse;
  margin-top: 1rem;
}

.admin-returns__table th,
.admin-returns__table td {
  padding: 0.625rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.admin-returns__row--attention {
  background: #fff7ed;
}

.admin-returns__attention-badge {
  margin-left: 0.5rem;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  background: #fed7aa;
  color: #9a3412;
  font-size: 0.75rem;
  font-weight: 700;
}

.admin-returns__count {
  margin-top: 0.75rem;
  color: #4b5563;
  font-size: 0.875rem;
}
</style>

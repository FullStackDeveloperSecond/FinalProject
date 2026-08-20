<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { ref } from 'vue'
import { useSupportTicketsQuery } from '../../features/support/queries'
import { categoryLabels, formatDateTime, statusLabels } from '../../features/support/labels'
import type { SupportTicketCategory, SupportTicketStatus } from '../../features/support/types'

const statusFilter = ref<SupportTicketStatus | ''>('')
const categoryFilter = ref<SupportTicketCategory | ''>('')

const { data, isPending, isError, error, refetch } = useSupportTicketsQuery(() => ({
  status: statusFilter.value || undefined,
  category: categoryFilter.value || undefined,
}))
</script>

<template>
  <section aria-labelledby="support-tickets-title">
    <div class="support-tickets__header">
      <h1 id="support-tickets-title">
        我的客服案件
      </h1>
      <RouterLink
        class="support-tickets__new-link"
        to="/support/tickets/new"
      >
        ＋ 建立新案件
      </RouterLink>
    </div>

    <div class="support-tickets__filters">
      <label>
        狀態
        <select v-model="statusFilter">
          <option value="">
            全部
          </option>
          <option
            v-for="(label, value) in statusLabels"
            :key="value"
            :value="value"
          >
            {{ label }}
          </option>
        </select>
      </label>
      <label>
        分類
        <select v-model="categoryFilter">
          <option value="">
            全部
          </option>
          <option
            v-for="(label, value) in categoryLabels"
            :key="value"
            :value="value"
          >
            {{ label }}
          </option>
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
      title="目前沒有客服案件"
      description="建立新案件後，會顯示在這裡。"
    />
    <table
      v-else-if="data"
      class="support-tickets__table"
    >
      <thead>
        <tr>
          <th scope="col">
            案件編號
          </th>
          <th scope="col">
            分類
          </th>
          <th scope="col">
            主旨
          </th>
          <th scope="col">
            狀態
          </th>
          <th scope="col">
            最後活動時間
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="ticket in data.items"
          :key="ticket.publicId"
        >
          <td>
            <RouterLink :to="`/support/tickets/${ticket.publicId}`">
              {{ ticket.ticketNumber }}
            </RouterLink>
          </td>
          <td>{{ categoryLabels[ticket.category] }}</td>
          <td>{{ ticket.subject }}</td>
          <td>
            <span class="support-tickets__status">{{ statusLabels[ticket.status] }}</span>
          </td>
          <td>{{ formatDateTime(ticket.lastActivityAtUtc) }}</td>
        </tr>
      </tbody>
    </table>
    <p class="support-tickets__count">
      共 {{ data?.totalCount ?? 0 }} 筆
    </p>
  </section>
</template>

<style scoped>
.support-tickets__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.support-tickets__new-link {
  display: inline-block;
  min-height: 2.75rem;
  padding: 0.75rem 1rem;
  border: 1px solid #1d4ed8;
  border-radius: 0.5rem;
  background: #2563eb;
  color: #fff;
  text-decoration: none;
}

.support-tickets__filters {
  display: flex;
  gap: 1rem;
  margin-block: 1rem;
  flex-wrap: wrap;
}

.support-tickets__filters label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.875rem;
}

.support-tickets__filters select {
  min-height: 2.5rem;
  padding: 0.375rem 0.5rem;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
}

.support-tickets__table {
  width: 100%;
  border-collapse: collapse;
}

.support-tickets__table th,
.support-tickets__table td {
  padding: 0.625rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.support-tickets__status {
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  background: #eff6ff;
  color: #1d4ed8;
  font-size: 0.8125rem;
  font-weight: 700;
}

.support-tickets__count {
  color: #4b5563;
  font-size: 0.875rem;
}
</style>

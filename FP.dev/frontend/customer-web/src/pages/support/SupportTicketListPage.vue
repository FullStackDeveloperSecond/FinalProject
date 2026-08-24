<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref, watch } from 'vue'
import { useSupportTicketsQuery } from '../../features/support/queries'
import { categoryLabels, formatDateTime, statusLabels } from '../../features/support/labels'
import type { SupportTicketCategory, SupportTicketStatus } from '../../features/support/types'

const statusFilter = ref<SupportTicketStatus | ''>('')
const categoryFilter = ref<SupportTicketCategory | ''>('')
const pageNumber = ref(1)
const pageSize = 20

const { data, isPending, isError, error, refetch } = useSupportTicketsQuery(() => ({
  status: statusFilter.value || undefined,
  category: categoryFilter.value || undefined,
  pageNumber: pageNumber.value,
  pageSize,
}))

watch([statusFilter, categoryFilter], () => {
  pageNumber.value = 1
})

const totalPages = computed(() => Math.max(1, Math.ceil(Number(data.value?.totalCount ?? 0) / pageSize)))
</script>

<template>
  <section aria-labelledby="support-tickets-title">
    <div class="support-tickets__header">
      <h1 id="support-tickets-title">
        我的客服案件
      </h1>
      <RouterLink
        class="btn-link"
        to="/support/tickets/new"
      >
        ＋ 建立新案件
      </RouterLink>
    </div>

    <div class="support-tickets__filters">
      <label class="form-field">
        <span>狀態</span>
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
      <label class="form-field">
        <span>分類</span>
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
    <template v-else-if="data">
      <div class="support-tickets__table-wrap card">
        <table class="support-tickets__table">
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
              <td data-label="案件編號">
                <RouterLink :to="`/support/tickets/${ticket.publicId}`">
                  {{ ticket.ticketNumber }}
                </RouterLink>
              </td>
              <td data-label="分類">
                {{ categoryLabels[ticket.category] }}
              </td>
              <td data-label="主旨">
                {{ ticket.subject }}
              </td>
              <td data-label="狀態">
                <span class="status-pill">{{ statusLabels[ticket.status] }}</span>
              </td>
              <td data-label="最後活動時間">
                {{ formatDateTime(ticket.lastActivityAtUtc) }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="support-tickets__count">
        共 {{ data.totalCount }} 筆
      </p>
      <nav
        class="support-tickets__pagination"
        aria-label="客服案件分頁"
      >
        <button
          type="button"
          :disabled="pageNumber <= 1"
          @click="pageNumber--"
        >
          上一頁
        </button>
        <span>第 {{ pageNumber }} / {{ totalPages }} 頁</span>
        <button
          type="button"
          :disabled="pageNumber >= totalPages"
          @click="pageNumber++"
        >
          下一頁
        </button>
      </nav>
    </template>
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

.support-tickets__filters {
  display: flex;
  gap: 1.25rem;
  margin: 1rem 0 1.5rem;
  flex-wrap: wrap;
}

.support-tickets__filters .form-field {
  min-width: 10rem;
  margin-bottom: 0;
}

.support-tickets__table-wrap {
  padding: 0;
  overflow-x: auto;
}

.support-tickets__table {
  width: 100%;
  border-collapse: collapse;
}

.support-tickets__table th,
.support-tickets__table td {
  padding: 0.75rem 1.25rem;
  border-bottom: 1px solid var(--color-border-soft);
  text-align: left;
}

.support-tickets__table thead th {
  color: var(--color-text-muted);
  font-size: 0.8125rem;
  font-weight: 600;
}

.support-tickets__table tbody tr:last-child td {
  border-bottom: none;
}

.support-tickets__count {
  margin-top: 1rem;
  color: var(--color-text-muted);
  font-size: 0.875rem;
}

.support-tickets__pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-top: 1rem;
}

@media (max-width: 640px) {
  .support-tickets__table thead {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
  }

  .support-tickets__table,
  .support-tickets__table tbody,
  .support-tickets__table tr,
  .support-tickets__table td {
    display: block;
    width: 100%;
  }

  .support-tickets__table-wrap {
    background: transparent;
    border: none;
    box-shadow: none;
  }

  .support-tickets__table tr {
    margin-bottom: 0.75rem;
    background: #fff;
    border: 1px solid var(--color-border-soft);
    border-radius: var(--radius-md);
    box-shadow: var(--shadow-sm);
    padding: 0.5rem 1rem;
  }

  .support-tickets__table td {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 0.5rem 0;
    border-bottom: 1px solid var(--color-border-soft);
    text-align: right;
  }

  .support-tickets__table td:last-child {
    border-bottom: none;
  }

  .support-tickets__table td::before {
    content: attr(data-label);
    font-weight: 600;
    color: var(--color-text-muted);
    text-align: left;
  }
}
</style>

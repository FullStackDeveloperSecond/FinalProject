<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { useRefundList } from '../../features/refunds/useRefunds'
import {
  formatRefundDate,
  formatRefundMoney,
  refundStatusLabels,
} from '../../features/refunds/labels'
import type { RefundStatus } from '../../features/refunds/types'
import { useSearchFilters } from '../../features/shared/useSearchFilters'

const { filters, listParams, search, goToPage } = useSearchFilters(20)
const selectedStatus = ref<RefundStatus | ''>('')
const statusOptions = Object.keys(refundStatusLabels) as RefundStatus[]

const query = computed(() => ({
  ...listParams.value,
  statuses: selectedStatus.value ? [selectedStatus.value] : undefined,
}))

const { data: result, isPending, isError, error, refetch } = useRefundList(query)
const apiError = computed(() => isApiError(error.value) ? error.value : undefined)
const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

function changeStatus() {
  filters.pageNumber = 1
}
</script>

<template>
  <section aria-labelledby="refund-list-title">
    <header>
      <h1 id="refund-list-title">
        退款管理
      </h1>
      <p>依退款編號與狀態查詢；執行退款請進入明細確認可信分攤與核准上限。</p>
    </header>

    <form
      aria-label="退款搜尋"
      @submit.prevent="search"
    >
      <label for="refund-query">退款編號</label>
      <input
        id="refund-query"
        v-model="filters.q"
        type="search"
        placeholder="例如 RF-202609"
      >

      <label for="refund-status">狀態</label>
      <select
        id="refund-status"
        v-model="selectedStatus"
        aria-label="退款狀態"
        @change="changeStatus"
      >
        <option value="">
          全部狀態
        </option>
        <option
          v-for="status in statusOptions"
          :key="status"
          :value="status"
        >
          {{ refundStatusLabels[status] }}
        </option>
      </select>

      <button type="submit">
        搜尋
      </button>
    </form>

    <LoadingState
      v-if="isPending"
      label="退款清單載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="apiError?.correlationId"
      :trace-id="apiError?.traceId"
      @retry="() => refetch()"
    />
    <EmptyState
      v-else-if="!result?.items.length"
      title="沒有符合條件的退款"
    />
    <template v-else>
      <table>
        <caption class="sr-only">
          退款清單
        </caption>
        <thead>
          <tr>
            <th scope="col">
              退款編號
            </th>
            <th scope="col">
              狀態
            </th>
            <th scope="col">
              申請金額
            </th>
            <th scope="col">
              核准上限
            </th>
            <th scope="col">
              成功退款
            </th>
            <th scope="col">
              建立時間
            </th>
            <th scope="col">
              操作
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="refund in result.items"
            :key="refund.publicId"
          >
            <td>{{ refund.refundNumber }}</td>
            <td>{{ refundStatusLabels[refund.status] }}</td>
            <td>{{ formatRefundMoney(refund.requestedAmount) }}</td>
            <td>{{ formatRefundMoney(refund.approvedAmount) }}</td>
            <td>{{ formatRefundMoney(refund.succeededAmount) }}</td>
            <td>{{ formatRefundDate(refund.createdAtUtc) }}</td>
            <td>
              <RouterLink :to="`/refunds/${refund.publicId}`">
                查看明細
              </RouterLink>
            </td>
          </tr>
        </tbody>
      </table>

      <nav aria-label="退款分頁">
        <button
          type="button"
          :disabled="filters.pageNumber <= 1"
          @click="goToPage(filters.pageNumber - 1)"
        >
          上一頁
        </button>
        <span>第 {{ filters.pageNumber }} / {{ totalPages }} 頁</span>
        <button
          type="button"
          :disabled="filters.pageNumber >= totalPages"
          @click="goToPage(filters.pageNumber + 1)"
        >
          下一頁
        </button>
      </nav>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState, ErrorState, LoadingState, PagePager } from '@doselect/web-shared/components'
import { useRefundList } from '../../features/refunds/useRefunds'
import {
  formatRefundDate,
  formatRefundMoney,
  refundStatusLabels,
} from '../../features/refunds/labels'
import type { RefundStatus } from '../../features/refunds/types'
import type { RefundSortOption } from '../../features/refunds/api'
import { useSearchFilters } from '../../features/shared/useSearchFilters'

const { filters, listParams, search, goToPage } = useSearchFilters(20)
const selectedStatus = ref<RefundStatus | ''>('')
const statusOptions = Object.keys(refundStatusLabels) as RefundStatus[]
type RefundSortField = 'refundNumber' | 'status' | 'createdAt'
type SortDirection = 'Asc' | 'Desc'
const sortField = ref<RefundSortField>('createdAt')
const sortDirection = ref<SortDirection>('Desc')
const sort = computed(() => `${sortField.value}${sortDirection.value}` as RefundSortOption)

const query = computed(() => ({
  ...listParams.value,
  statuses: selectedStatus.value ? [selectedStatus.value] : undefined,
  sort: sort.value,
}))

const { data: result, isPending, isError, error, refetch } = useRefundList(query)
const apiError = computed(() => isApiError(error.value) ? error.value : undefined)
const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

function changeStatus() {
  filters.pageNumber = 1
}

function setSort(field: RefundSortField) {
  if (sortField.value === field) {
    sortDirection.value = sortDirection.value === 'Asc' ? 'Desc' : 'Asc'
  } else {
    sortField.value = field
    sortDirection.value = 'Asc'
  }
  filters.pageNumber = 1
}

function ariaSort(field: RefundSortField): 'ascending' | 'descending' | 'none' {
  if (sortField.value !== field) return 'none'
  return sortDirection.value === 'Asc' ? 'ascending' : 'descending'
}

function sortIndicator(field: RefundSortField): string {
  if (sortField.value !== field) return '↕'
  return sortDirection.value === 'Asc' ? '↑' : '↓'
}
</script>

<template>
  <section
    class="finance-page"
    aria-labelledby="refund-list-title"
  >
    <header>
      <h1 id="refund-list-title">
        退款管理
      </h1>
      <p>依退款編號與狀態查詢；執行退款請進入明細確認可信分攤與核准上限。</p>
    </header>

    <form
      class="finance-filter finance-panel"
      aria-label="退款搜尋"
      @submit.prevent="search"
    >
      <div class="finance-field">
        <label for="refund-query">關鍵字</label>
        <input
          id="refund-query"
          v-model="filters.q"
          type="search"
        >
      </div>
      <div class="finance-field">
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
      </div>

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
      <div
        class="table-scroll"
        role="region"
        aria-label="退款清單表格"
        tabindex="0"
      >
        <table>
          <caption class="sr-only">
            退款清單
          </caption>
          <thead>
            <tr>
              <th
                scope="col"
                data-sort="refundNumber"
                :aria-sort="ariaSort('refundNumber')"
              >
                <button
                  type="button"
                  class="finance-sort-button"
                  aria-label="依退款編號排序"
                  @click="setSort('refundNumber')"
                >
                  退款編號 <span aria-hidden="true">{{ sortIndicator('refundNumber') }}</span>
                </button>
              </th>
              <th
                scope="col"
                data-sort="status"
                :aria-sort="ariaSort('status')"
              >
                <button
                  type="button"
                  class="finance-sort-button"
                  aria-label="依退款狀態排序"
                  @click="setSort('status')"
                >
                  狀態 <span aria-hidden="true">{{ sortIndicator('status') }}</span>
                </button>
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
              <th
                scope="col"
                data-sort="createdAt"
                :aria-sort="ariaSort('createdAt')"
              >
                <button
                  type="button"
                  class="finance-sort-button"
                  aria-label="依建立時間排序"
                  @click="setSort('createdAt')"
                >
                  建立時間 <span aria-hidden="true">{{ sortIndicator('createdAt') }}</span>
                </button>
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
      </div>

      <PagePager
        :page="filters.pageNumber"
        :total-records="totalPages"
        :page-size="1"
        aria-label="退款分頁"
        @update:page="goToPage"
      />
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive } from 'vue'
import { RouterLink } from 'vue-router'
import { EmptyState, ErrorState, HttpStatusPage, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import {
  BADGE_OPTIONS,
  SUMMARY_STATUS_OPTIONS,
  badgeLabel,
  orderStatusLabel,
  summaryStatusLabel,
  type AdminOrderListFilters,
  type OrderBadge,
  type SummaryStatus,
} from '../api'
import { useAdminOrderListQuery } from '../queries/useAdminOrders'

const filters = reactive<AdminOrderListFilters>({
  summaryStatus: [],
  badge: [],
  cursor: undefined,
  pageSize: 20,
})

const { data, isPending, isError, error, refetch } = useAdminOrderListQuery(computed(() => filters))

const apiError = computed(() => (isApiError(error.value) ? error.value : undefined))

function toggle<T extends string>(list: T[], value: T): void {
  const index = list.indexOf(value)
  if (index === -1) {
    list.push(value)
  }
  else {
    list.splice(index, 1)
  }
  filters.cursor = undefined
}

function toggleSummaryStatus(value: SummaryStatus): void {
  toggle(filters.summaryStatus, value)
}

function toggleBadge(value: OrderBadge): void {
  toggle(filters.badge, value)
}

// Cursor 分頁採「換頁」而非累加式無限捲動（不用 useInfiniteQuery）——換頁時取代目前顯示的
// items，不保留前一頁。範圍夠用即可，之後要做無限捲動再換 useInfiniteQuery。
function loadMore(): void {
  if (data.value?.nextCursor) {
    filters.cursor = data.value.nextCursor
  }
}

function formatDateTime(value?: string | null): string {
  if (!value) {
    return '—'
  }
  return new Date(value).toLocaleString('zh-TW')
}
</script>

<template>
  <section aria-labelledby="page-title">
    <h1 id="page-title">
      訂單管理
    </h1>

    <fieldset>
      <legend>摘要狀態</legend>
      <label
        v-for="option in SUMMARY_STATUS_OPTIONS"
        :key="option.value"
      >
        <input
          type="checkbox"
          :checked="filters.summaryStatus.includes(option.value)"
          @change="toggleSummaryStatus(option.value)"
        >
        {{ option.label }}
      </label>
    </fieldset>

    <fieldset>
      <legend>徽章</legend>
      <label
        v-for="option in BADGE_OPTIONS"
        :key="option.value"
      >
        <input
          type="checkbox"
          :checked="filters.badge.includes(option.value)"
          @change="toggleBadge(option.value)"
        >
        {{ option.label }}
      </label>
    </fieldset>

    <LoadingState
      v-if="isPending"
      label="訂單列表載入中"
    />

    <HttpStatusPage
      v-else-if="apiError?.status === 401"
      :status="401"
      home-href="/"
    />

    <HttpStatusPage
      v-else-if="apiError?.status === 403"
      :status="403"
      home-href="/"
    />

    <ErrorState
      v-else-if="isError"
      :correlation-id="apiError?.correlationId"
      :trace-id="apiError?.traceId"
      @retry="() => refetch()"
    />

    <EmptyState
      v-else-if="data && data.items.length === 0"
      title="沒有符合條件的訂單"
      description="調整篩選條件後再試一次。"
    />

    <template v-else-if="data">
      <table>
        <thead>
          <tr>
            <th scope="col">
              訂單編號
            </th>
            <th scope="col">
              買家
            </th>
            <th scope="col">
              狀態
            </th>
            <th scope="col">
              金額
            </th>
            <th scope="col">
              建立時間
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="order in data.items"
            :key="order.publicId"
          >
            <td>
              <RouterLink :to="{ name: 'admin-order-detail', params: { publicId: order.publicId } }">
                {{ order.orderNumber }}
              </RouterLink>
            </td>
            <td>{{ order.buyerType === 'Member' ? '會員' : '訪客' }}／{{ order.maskedBuyerEmail }}</td>
            <td>
              {{ summaryStatusLabel(order.summaryStatus) }}
              <span
                v-for="badge in order.badges"
                :key="badge"
              >（{{ badgeLabel(badge) }}）</span>
              <span :title="order.orderStatus">（{{ orderStatusLabel[order.orderStatus] ?? order.orderStatus }}）</span>
            </td>
            <td>NT$ {{ order.grandTotal }}</td>
            <td>{{ formatDateTime(order.createdAtUtc) }}</td>
          </tr>
        </tbody>
      </table>

      <button
        v-if="data.hasMore"
        type="button"
        @click="loadMore"
      >
        載入更多
      </button>
    </template>
  </section>
</template>

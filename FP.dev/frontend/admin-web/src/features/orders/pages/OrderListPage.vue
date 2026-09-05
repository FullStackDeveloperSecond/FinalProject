<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
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
import { useAdminAuthStore } from '../../auth/stores/useAdminAuthStore'
import { setBatchShipmentSelection } from '../../shipping/batchSelection'
import { MAX_BATCH_SHIPMENT_ORDERS } from '../../shipping/types'

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

// A-14「勾選批次出貨」的入口，導到 A-16。寫入權限與 router 的 Shipping.Manage 同一份清單。
const router = useRouter()
const auth = useAdminAuthStore()
const canShipBatch = computed(() =>
  ['OrderManager', 'SuperAdmin'].some(role => auth.currentUser?.roles?.includes(role) ?? false))

/**
 * 勾起來的訂單連同 RowVersion 一起記著——批次出貨要靠它做併發仲裁，等到送出時才回頭找就已經
 * 沒有當初那份資料了。用 Map 而不是只存 publicId 陣列，是為了換頁後仍然能組出完整的請求。
 */
const selected = ref(new Map<string, { orderNumber: string, rowVersion: string, summaryStatus: string, fulfillmentStatus: string }>())

// 只有待出貨的訂單能勾。已出貨或已取消的訂單送過去只會逐筆失敗，讓管理員先看到才不會白跑。
function isSelectable(summaryStatus: string): boolean {
  return summaryStatus === 'awaitingShipment'
}

function isSelected(publicId: string): boolean {
  return selected.value.has(publicId)
}

function toggleSelected(order: {
  publicId: string
  orderNumber: string
  rowVersion: string
  summaryStatus: string
  fulfillmentStatus: string
}): void {
  const next = new Map(selected.value)
  if (next.has(order.publicId)) {
    next.delete(order.publicId)
  }
  else {
    next.set(order.publicId, {
      orderNumber: order.orderNumber,
      rowVersion: order.rowVersion,
      summaryStatus: order.summaryStatus,
      fulfillmentStatus: order.fulfillmentStatus,
    })
  }
  selected.value = next
}

/**
 * 換篩選條件或換頁時清掉勾選。留著的話，管理員會在一份看不見的清單上按下「批次出貨」——畫面上
 * 是這一頁的訂單，送出去的卻是上一頁勾的那些。這與 placeholderData 那組 review 是同一個問題：
 * 身分變了，就不能讓上一個身分的資料繼續是可操作的。
 */
watch(() => data.value, () => {
  if (selected.value.size > 0) {
    selected.value = new Map()
  }
})

const selectionCount = computed(() => selected.value.size)
const isOverBatchLimit = computed(() => selectionCount.value > MAX_BATCH_SHIPMENT_ORDERS)

async function goToBatchShipment(): Promise<void> {
  if (selectionCount.value === 0 || isOverBatchLimit.value) {
    return
  }

  setBatchShipmentSelection([...selected.value.entries()].map(([publicId, order]) => ({
    publicId,
    ...order,
  })))
  await router.push('/shipping/batches')
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
            <th
              v-if="canShipBatch"
              scope="col"
            >
              選取
            </th>
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
            <td v-if="canShipBatch">
              <input
                type="checkbox"
                :checked="isSelected(order.publicId)"
                :disabled="!isSelectable(order.summaryStatus)"
                :aria-label="`勾選訂單 ${order.orderNumber} 進行批次出貨`"
                @change="toggleSelected(order)"
              >
            </td>
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

      <p v-if="canShipBatch && selectionCount > 0">
        已選取 {{ selectionCount }} 筆
        <span v-if="isOverBatchLimit">（單批上限 {{ MAX_BATCH_SHIPMENT_ORDERS }} 筆，請取消部分勾選）</span>
        <button
          type="button"
          :disabled="isOverBatchLimit"
          @click="goToBatchShipment"
        >
          批次出貨
        </button>
      </p>
    </template>
  </section>
</template>

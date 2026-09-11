<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { EmptyState, ErrorState, LoadingState, PagePager } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { formatTaipeiDate } from '@doselect/web-shared/datetime'
import { fetchOrders, type OrderSummaryDto } from './api'

const pageSize = 10
const orders = ref<OrderSummaryDto[]>([])
const pageNumber = ref(0)
const totalCount = ref(0)
const isLoading = ref(true)
const isLoadingMore = ref(false)
const loadError = ref<unknown>()
const loadMoreError = ref(false)

async function loadFirstPage(): Promise<void> {
  isLoading.value = true
  loadError.value = undefined
  try {
    const page = await fetchOrders(1, pageSize)
    orders.value = page.items
    pageNumber.value = page.pageNumber
    totalCount.value = page.totalCount
  }
  catch (error) {
    loadError.value = error
  }
  finally {
    isLoading.value = false
  }
}

async function goToPage(nextPage: number): Promise<void> {
  if (isLoadingMore.value || nextPage === pageNumber.value) return

  isLoadingMore.value = true
  loadMoreError.value = false
  try {
    const page = await fetchOrders(nextPage, pageSize)
    orders.value = page.items
    pageNumber.value = page.pageNumber
    totalCount.value = page.totalCount
  }
  catch {
    loadMoreError.value = true
  }
  finally {
    isLoadingMore.value = false
  }
}

onMounted(loadFirstPage)

const orderStatusLabels: Record<string, string> = {
  pendingPayment: '等待付款',
  confirmed: '已確認',
  processing: '處理中',
  completed: '已結單',
  cancelled: '已取消',
}

const paymentStatusLabels: Record<string, string> = {
  pending: '等待建立付款',
  awaitingPayment: '等待付款',
  processing: '付款處理中',
  paid: '已付款',
  failed: '付款失敗',
  cancelled: '付款已取消',
  expired: '付款已逾期',
}

const fulfillmentStatusLabels: Record<string, string> = {
  pending: '待處理',
  preparing: '備貨中',
  shipped: '已出貨',
  inTransit: '配送中',
  pickupReady: '可取貨',
  pickedUp: '已取貨',
  delivered: '已送達',
  deliveryFailed: '配送失敗',
  returned: '已退回',
}

function shippingLabel(status: string): string {
  return ['pending', 'preparing'].includes(status) ? '尚未出貨' : '已出貨'
}

function closureLabel(status: string): string {
  return ['completed', 'cancelled'].includes(status)
    ? (orderStatusLabels[status] ?? status)
    : '尚未結單'
}

function formatDate(value: string): string {
  return formatTaipeiDate(value)
}

function formatAmount(amount: number, currency: string): string {
  return new Intl.NumberFormat('zh-TW', {
    style: 'currency',
    currency,
    maximumFractionDigits: 0,
  }).format(amount)
}
</script>

<template>
  <section
    class="order-list-page"
    aria-labelledby="order-list-title"
  >
    <header>
      <p class="order-list-page__eyebrow">
        會員中心
      </p>
      <h1 id="order-list-title">
        我的訂單
      </h1>
      <p>查看付款、配送與結單進度，點選訂單即可查看明細與可用的退貨操作。</p>
    </header>

    <LoadingState
      v-if="isLoading"
      label="訂單載入中"
    />
    <ErrorState
      v-else-if="loadError"
      title="無法載入訂單"
      :description="isApiError(loadError) ? loadError.message : '請稍後再試一次。'"
      :correlation-id="isApiError(loadError) ? loadError.correlationId : undefined"
      :trace-id="isApiError(loadError) ? loadError.traceId : undefined"
      @retry="loadFirstPage"
    />
    <EmptyState
      v-else-if="orders.length === 0"
      title="目前沒有訂單"
      description="完成購買後，訂單會顯示在這裡。"
    />

    <template v-else>
      <p class="order-list-page__count">
        共 {{ totalCount }} 筆訂單
      </p>
      <div class="order-list">
        <article
          v-for="order in orders"
          :key="order.publicId"
          class="order-card"
        >
          <div class="order-card__heading">
            <div>
              <p class="order-card__number">
                {{ order.orderNumber }}
              </p>
              <time :datetime="order.createdAtUtc">{{ formatDate(order.createdAtUtc) }}</time>
            </div>
            <strong>{{ formatAmount(order.grandTotal, order.currency) }}</strong>
          </div>

          <dl class="order-card__statuses">
            <div>
              <dt>付款</dt>
              <dd>{{ paymentStatusLabels[order.paymentStatus] ?? order.paymentStatus }}</dd>
            </div>
            <div>
              <dt>出貨</dt>
              <dd>{{ shippingLabel(order.fulfillmentStatus) }}</dd>
            </div>
            <div>
              <dt>送達</dt>
              <dd>{{ fulfillmentStatusLabels[order.fulfillmentStatus] ?? order.fulfillmentStatus }}</dd>
            </div>
            <div>
              <dt>結單</dt>
              <dd>{{ closureLabel(order.orderStatus) }}</dd>
            </div>
          </dl>

          <div class="order-card__footer">
            <span>{{ order.itemCount }} 項商品</span>
            <RouterLink :to="{ name: 'order-detail', params: { orderId: order.publicId } }">
              查看訂單明細
            </RouterLink>
          </div>
        </article>
      </div>

      <p
        v-if="loadMoreError"
        class="order-list-page__more-error"
        role="alert"
      >
        無法載入指定頁面，目前保留原頁資料；請再次點選頁碼重試。
      </p>
      <PagePager
        :page="pageNumber"
        :page-size="pageSize"
        :total-records="totalCount"
        :busy="isLoadingMore"
        aria-label="會員訂單分頁"
        @update:page="goToPage"
      />
    </template>
  </section>
</template>

<style scoped>
.order-list-page { display: grid; gap: 1.5rem; max-width: 70rem; margin-inline: auto; }
.order-list-page header > * { margin-block: 0 .5rem; }
.order-list-page__eyebrow { color: var(--color-text-muted); font-weight: 700; }
.order-list-page__count { margin: 0; color: var(--color-text-muted); }
.order-list { display: grid; gap: 1rem; }
.order-card { display: grid; gap: 1rem; padding: 1.25rem; border: 1px solid var(--color-border); border-radius: 1rem; background: var(--color-surface); }
.order-card__heading, .order-card__footer { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
.order-card__number { margin: 0 0 .25rem; font-weight: 800; }
.order-card time, .order-card__footer, .order-list-page__more-error { color: var(--color-text-muted); }
.order-card__statuses { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: .75rem; margin: 0; }
.order-card__statuses div { padding: .75rem; border-radius: .75rem; background: var(--color-surface-muted); }
.order-card__statuses dt { color: var(--color-text-muted); font-size: .8rem; }
.order-card__statuses dd { margin: .25rem 0 0; font-weight: 700; }
.order-list-page__more { justify-self: center; }
@media (max-width: 42rem) {
  .order-card__heading, .order-card__footer { align-items: flex-start; flex-direction: column; }
  .order-card__statuses { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>

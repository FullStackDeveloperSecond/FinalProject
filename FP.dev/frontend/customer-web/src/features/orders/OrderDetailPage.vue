<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { EmptyState, ErrorState, HttpStatusPage, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import {
  CANCELLATION_REASON_OPTIONS,
  cancelOrder,
  fetchOrder,
  type OrderDto,
} from './api'
import { fetchOrderInvoice, type SimulatedInvoiceDto } from '../payments/api'

const route = useRoute()
const router = useRouter()
const orderPublicId = computed(() => String(route.params.orderId))

type PageState = 'loading' | 'ready' | 'unauthenticated' | 'not-found' | 'error'

const pageState = ref<PageState>('loading')
const order = ref<OrderDto | undefined>(undefined)
const loadErrorProps = ref<{ correlationId?: string; traceId?: string }>({})

const cancelForm = reactive({
  reasonCode: '',
  note: '',
})
const isCancelling = ref(false)
const cancelErrorMessage = ref<string | undefined>(undefined)
const showCancelForm = ref(false)
type InvoiceState = 'idle' | 'loading' | 'ready' | 'missing' | 'error'
const invoiceState = ref<InvoiceState>('idle')
const invoice = ref<SimulatedInvoiceDto>()

async function loadOrder(): Promise<void> {
  pageState.value = 'loading'
  try {
    order.value = await fetchOrder(orderPublicId.value)
    pageState.value = 'ready'
    if (order.value.paymentStatus === 'paid' || order.value.amounts.refundedAmount > 0) {
      await loadInvoice()
    }
  }
  catch (error) {
    if (isApiError(error)) {
      if (error.status === 401) {
        pageState.value = 'unauthenticated'
        return
      }
      if (error.status === 404) {
        pageState.value = 'not-found'
        return
      }
      loadErrorProps.value = { correlationId: error.correlationId, traceId: error.traceId }
    }
    pageState.value = 'error'
  }
}

async function loadInvoice(): Promise<void> {
  invoiceState.value = 'loading'
  try {
    invoice.value = await fetchOrderInvoice(orderPublicId.value)
    invoiceState.value = 'ready'
  }
  catch (error) {
    invoiceState.value = isApiError(error) && error.status === 404 ? 'missing' : 'error'
  }
}

onMounted(loadOrder)

const canCancel = computed(() => order.value?.availableActions.includes('cancel') ?? false)
const canRequestReturn = computed(() => order.value?.availableActions.includes('requestReturn') ?? false)
const returnableItems = computed(
  () => order.value?.items.filter(item => item.returnableQuantity > item.returnedQuantity) ?? [],
)
const canPay = computed(() =>
  order.value?.orderStatus === 'pendingPayment' && order.value.paymentStatus !== 'paid',
)

async function submitCancel(): Promise<void> {
  if (!order.value || !cancelForm.reasonCode) {
    return
  }

  isCancelling.value = true
  cancelErrorMessage.value = undefined
  try {
    order.value = await cancelOrder(order.value.publicId, {
      reasonCode: cancelForm.reasonCode,
      note: cancelForm.note || undefined,
      orderRowVersion: order.value.rowVersion,
    })
    showCancelForm.value = false
  }
  catch (error) {
    cancelErrorMessage.value = isApiError(error)
      ? describeCancelError(error.code)
      : '取消訂單時發生問題，請稍後再試。'
  }
  finally {
    isCancelling.value = false
  }
}

function describeCancelError(code: string): string {
  switch (code) {
    case 'order_cancellation_not_allowed':
      return '這筆訂單目前的狀態已無法自助取消。'
    case 'concurrency_conflict':
      return '訂單資料已被更新，請重新整理後再試一次。'
    case 'order_state_conflict':
      return '訂單狀態已變更，請重新整理後再試一次。'
    default:
      return '取消訂單時發生問題，請稍後再試。'
  }
}

async function startReturn(): Promise<void> {
  if (!order.value || !canRequestReturn.value || returnableItems.value.length === 0) {
    return
  }

  await router.push({
    name: 'return-new',
    params: { orderId: order.value.publicId },
    query: {
      orderRowVersion: order.value.rowVersion,
      items: JSON.stringify(returnableItems.value.map(item => ({
        orderItemPublicId: item.publicId,
        skuName: item.skuNameSnapshot,
        maxQuantity: item.returnableQuantity - item.returnedQuantity,
      }))),
    },
  })
}

function formatDateTime(value?: string | null): string {
  if (!value) {
    return '—'
  }
  return new Date(value).toLocaleString('zh-TW')
}

const orderStatusLabel: Record<string, string> = {
  pendingPayment: '等待付款',
  confirmed: '已確認',
  processing: '處理中',
  completed: '已完成',
  cancelled: '已取消',
}

const paymentStatusLabel: Record<string, string> = {
  pending: '等待建立付款',
  awaitingPayment: '等待付款',
  processing: '付款處理中',
  paid: '已付款',
  failed: '付款失敗',
  cancelled: '付款已取消',
  expired: '付款已逾期',
}

const fulfillmentStatusLabel: Record<string, string> = {
  pending: '待處理',
  preparing: '備貨中',
  shipped: '已出貨',
  inTransit: '配送中',
  pickupReady: '超商到店，請於期限內取貨',
  pickedUp: '已取貨',
  delivered: '已送達',
  deliveryFailed: '配送失敗',
  returned: '已退回',
}

const refundStatusLabel: Record<string, string> = {
  none: '尚無退款',
  pending: '退款處理中',
  partiallyRefunded: '部分退款',
  refunded: '已全額退款',
}

const invoiceStatusLabel: Record<string, string> = {
  pending: '待開立',
  issued: '已開立',
  voided: '已作廢',
  partiallyAllowed: '部分折讓',
  fullyAllowed: '全額折讓',
}
</script>

<template>
  <section aria-labelledby="page-title">
    <LoadingState
      v-if="pageState === 'loading'"
      label="訂單資料載入中"
    />

    <HttpStatusPage
      v-else-if="pageState === 'unauthenticated'"
      :status="401"
      home-href="/"
    />

    <HttpStatusPage
      v-else-if="pageState === 'not-found'"
      :status="404"
      home-href="/"
    />

    <ErrorState
      v-else-if="pageState === 'error'"
      :correlation-id="loadErrorProps.correlationId"
      :trace-id="loadErrorProps.traceId"
      @retry="loadOrder"
    />

    <template v-else-if="order">
      <h1 id="page-title">
        訂單 {{ order.orderNumber }}
      </h1>
      <p>
        狀態：{{ orderStatusLabel[order.orderStatus] ?? order.orderStatus }}
      </p>

      <section aria-labelledby="items-title">
        <h2 id="items-title">
          商品明細
        </h2>
        <ul>
          <li
            v-for="item in order.items"
            :key="item.publicId"
          >
            {{ item.productNameSnapshot }}（{{ item.skuNameSnapshot }}）× {{ item.quantity }}
            — NT$ {{ item.lineTotal }}
          </li>
        </ul>
        <p>應付總額：NT$ {{ order.amounts.grandTotal }}</p>
      </section>

      <section aria-labelledby="payment-summary-title">
        <h2 id="payment-summary-title">
          付款與退款
        </h2>
        <p>付款狀態：{{ paymentStatusLabel[order.paymentStatus] ?? order.paymentStatus }}</p>
        <p>已付款：NT$ {{ order.amounts.paidAmount }}</p>
        <p>退款狀態：{{ refundStatusLabel[order.orderRefundStatus] ?? order.orderRefundStatus }}</p>
        <p>已退款：NT$ {{ order.amounts.refundedAmount }}</p>
        <a
          v-if="canPay"
          :href="`/orders/${order.publicId}/payment`"
        >
          {{ order.paymentStatus === 'failed' || order.paymentStatus === 'expired' ? '重新付款' : '前往付款' }}
        </a>
      </section>

      <section
        v-if="order.paymentStatus === 'paid' || order.amounts.refundedAmount > 0"
        aria-labelledby="invoice-title"
      >
        <h2 id="invoice-title">
          模擬發票
        </h2>
        <LoadingState
          v-if="invoiceState === 'loading'"
          label="發票資料載入中"
        />
        <template v-else-if="invoiceState === 'ready' && invoice">
          <p>{{ invoice.demoMarker }}｜{{ invoice.invoiceNumber }}</p>
          <p>狀態：{{ invoiceStatusLabel[invoice.status] ?? invoice.status }}</p>
          <p>含稅總額：NT$ {{ invoice.grossAmount }} {{ invoice.currency }}</p>
          <p v-if="invoice.buyerEmailMasked">
            買受人 Email：{{ invoice.buyerEmailMasked }}
          </p>
          <p v-if="invoice.allowances.length > 0">
            折讓筆數：{{ invoice.allowances.length }}
          </p>
        </template>
        <EmptyState
          v-else-if="invoiceState === 'missing'"
          title="發票尚未建立"
          description="付款完成後發票會由背景流程建立，請稍後重新整理。"
        />
        <ErrorState
          v-else-if="invoiceState === 'error'"
          title="無法載入發票"
          description="訂單資料仍可正常查看，請稍後重試發票查詢。"
          @retry="loadInvoice"
        />
      </section>

      <section aria-labelledby="shipment-title">
        <h2 id="shipment-title">
          配送資訊
        </h2>
        <p>
          配送方式：{{ order.recipient.shippingMethodCode }}<template v-if="order.recipient.storeName">
            （{{ order.recipient.storeName }}）
          </template>
        </p>
        <p v-if="!order.shipment">
          尚未出貨。
        </p>
        <template v-else>
          <p>物流單號：{{ order.shipment.shipmentNumber }}</p>
          <p>追蹤號碼：{{ order.shipment.trackingNumber ?? '—' }}</p>
          <p>物流狀態：{{ fulfillmentStatusLabel[order.shipment.status] ?? order.shipment.status }}</p>
          <p v-if="order.shipment.deliveredAtUtc">
            送達／取貨時間：{{ formatDateTime(order.shipment.deliveredAtUtc) }}
          </p>
          <ul
            v-if="order.shipment.history.length > 0"
            aria-label="物流歷程"
          >
            <li
              v-for="(entry, index) in order.shipment.history"
              :key="index"
            >
              {{ formatDateTime(entry.occurredAtUtc) }} — {{ fulfillmentStatusLabel[entry.toStatus] ?? entry.toStatus }}
            </li>
          </ul>
        </template>
      </section>

      <!-- 同一頁支援會員與已完成查單驗證的訪客；後端會把 GuestOrderAccess Cookie
           限定在驗證時綁定的那一筆訂單，前端顯示條件不是授權邊界。 -->
      <section aria-labelledby="cancel-title">
        <h2 id="cancel-title">
          取消訂單
        </h2>
        <p v-if="!canCancel">
          這筆訂單目前的狀態無法自助取消。
        </p>
        <template v-else>
          <button
            v-if="!showCancelForm"
            type="button"
            @click="showCancelForm = true"
          >
            申請取消訂單
          </button>
          <form
            v-else
            @submit.prevent="submitCancel"
          >
            <label for="cancel-reason">取消原因</label>
            <select
              id="cancel-reason"
              v-model="cancelForm.reasonCode"
              required
            >
              <option
                value=""
                disabled
              >
                請選擇原因
              </option>
              <option
                v-for="option in CANCELLATION_REASON_OPTIONS"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </option>
            </select>

            <label for="cancel-note">補充說明（選填）</label>
            <textarea
              id="cancel-note"
              v-model="cancelForm.note"
              maxlength="500"
            />

            <p
              v-if="cancelErrorMessage"
              role="alert"
            >
              {{ cancelErrorMessage }}
            </p>

            <button
              type="submit"
              :disabled="isCancelling || !cancelForm.reasonCode"
            >
              {{ isCancelling ? '取消中…' : '確認取消訂單' }}
            </button>
            <button
              type="button"
              :disabled="isCancelling"
              @click="showCancelForm = false"
            >
              返回
            </button>
          </form>
        </template>
      </section>

      <section aria-labelledby="return-title">
        <h2 id="return-title">
          退貨
        </h2>
        <template v-if="canRequestReturn">
          <p v-if="order.returnRequestDeadlineUtc">
            一般猶豫期退貨申請期限：{{ formatDateTime(order.returnRequestDeadlineUtc) }}
            （商品瑕疵、寄錯、運送損壞或保固不受此期限限制）
          </p>
          <ul>
            <li
              v-for="item in returnableItems"
              :key="item.publicId"
            >
              {{ item.productNameSnapshot }}（{{ item.skuNameSnapshot }}）
              可退 {{ item.returnableQuantity - item.returnedQuantity }} 件
            </li>
          </ul>
          <button
            type="button"
            @click="startReturn"
          >
            申請退貨
          </button>
        </template>
        <EmptyState
          v-else
          title="目前沒有可退貨商品"
          description="商品送達後才能申請退貨；若商品有瑕疵或寄錯，送達後一樣可以在這裡申請。"
        />
      </section>
    </template>
  </section>
</template>

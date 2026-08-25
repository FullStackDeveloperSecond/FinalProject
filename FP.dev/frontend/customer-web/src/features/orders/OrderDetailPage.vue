<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { EmptyState, ErrorState, HttpStatusPage, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import {
  CANCELLATION_REASON_OPTIONS,
  cancelOrder,
  fetchOrder,
  type OrderDto,
} from './api'

const route = useRoute()
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

async function loadOrder(): Promise<void> {
  pageState.value = 'loading'
  try {
    order.value = await fetchOrder(orderPublicId.value)
    pageState.value = 'ready'
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

onMounted(loadOrder)

const canCancel = computed(() => order.value?.availableActions.includes('cancel') ?? false)
const canRequestReturn = computed(() => order.value?.availableActions.includes('requestReturn') ?? false)
const returnableItems = computed(
  () => order.value?.items.filter(item => item.returnableQuantity > item.returnedQuantity) ?? [],
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

function formatDateTime(value?: string | null): string {
  if (!value) {
    return '—'
  }
  return new Date(value).toLocaleString('zh-TW')
}

const orderStatusLabel: Record<string, string> = {
  PendingPayment: '等待付款',
  Confirmed: '已確認',
  Processing: '處理中',
  Completed: '已完成',
  Cancelled: '已取消',
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

      <!-- 訪客取消／退貨入口：本切片只實作會員自助入口，訪客（GuestOrderAccessToken）驗證串接
           待 haru/feature/guest-ordertracking 合併後補上；未登入會員的請求會在 loadOrder 收到
           401，於上方以 HttpStatusPage 呈現，尚不支援訪客用單筆存取權杖開啟本頁。 -->
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
          <p>退貨受理流程即將開放，敬請期待。</p>
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

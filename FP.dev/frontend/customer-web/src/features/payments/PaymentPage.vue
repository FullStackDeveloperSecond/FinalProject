<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { EmptyState, ErrorState, HttpStatusPage, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { fetchOrder, type OrderDto } from '../orders/api'
import {
  completeSimulatedPayment,
  createPaymentAttempt,
  fetchLatestPaymentAttempt,
  type PaymentAttemptDto,
  type PaymentMethod,
  type SimulatedPaymentOutcome,
} from './api'
import { useRoute } from 'vue-router'

type PageState = 'loading' | 'ready' | 'unauthenticated' | 'not-found' | 'error'

const route = useRoute()
const orderPublicId = computed(() => String(route.params.orderId))
const pageState = ref<PageState>('loading')
const order = ref<OrderDto>()
const loadErrorProps = ref<{ correlationId?: string; traceId?: string }>({})
const selectedMethod = ref<PaymentMethod>('creditCard')
const attempt = ref<PaymentAttemptDto>()
const isCreating = ref(false)
const isCompleting = ref(false)
const actionError = ref<string>()
const restoreFailed = ref(false)
const isRestoring = ref(false)
let createIdempotencyKey = crypto.randomUUID()
let simulationKey = crypto.randomUUID()

const paymentMethods: ReadonlyArray<{ value: PaymentMethod; label: string }> = [
  { value: 'creditCard', label: '信用卡' },
  { value: 'atm', label: 'ATM 虛擬帳號' },
  { value: 'convenienceCode', label: '超商代碼' },
  { value: 'linePay', label: 'LINE Pay' },
  { value: 'applePay', label: 'Apple Pay' },
  { value: 'googlePay', label: 'Google Pay' },
  { value: 'cashOnDelivery', label: '貨到付款' },
]

/** 這筆嘗試已經結束，不會再變。 */
const attemptIsTerminal = computed(() =>
  attempt.value !== undefined
  && ['paid', 'failed', 'expired', 'cancelled'].includes(attempt.value.status),
)

const orderAcceptsPayment = computed(() =>
  order.value?.orderStatus === 'pendingPayment' && order.value.paymentStatus !== 'paid',
)

/**
 * 什麼時候顯示「建立付款方式」表單。
 *
 * 沒有付款嘗試時顯示，這是原本就有的行為。**已經有一筆但它走到終態時也要顯示** ——
 * 恢復功能會把失敗／逾期的那一筆帶回來，如果因此就把表單藏起來，使用者付款失敗
 * 後重新整理就再也沒有辦法重試（Issue #86 A1：終態要保留，但仍可重試）。
 *
 * 已付款不在此列：訂單的 paymentStatus 會是 paid，orderAcceptsPayment 就是 false。
 */
const canCreateAttempt = computed(() =>
  orderAcceptsPayment.value && (attempt.value === undefined || attemptIsTerminal.value),
)
const canCompleteDemo = computed(() =>
  attempt.value !== undefined
  && attempt.value.method !== 'cashOnDelivery'
  && ['pending', 'processing', 'awaitingPayment'].includes(attempt.value.status),
)
const paymentCompleted = computed(() => order.value?.paymentStatus === 'paid')

async function loadOrder(): Promise<void> {
  pageState.value = 'loading'
  try {
    order.value = await fetchOrder(orderPublicId.value)
    await restoreLatestAttempt()
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

/**
 * 把上一筆付款嘗試帶回來 —— 付款方式、金額、狀態與 ATM／超商的付款指示都在裡面。
 * 少了這一步，那串繳費代碼一重新整理就不見了。
 *
 * 失敗**不會**讓整頁變成錯誤畫面：訂單已經載入成功，恢復只是加分。整頁掛掉會把
 * 「看不到上一筆」升級成「什麼都不能做」。改成顯示一則可重試的提示。
 *
 * 恢復失敗時仍然顯示建立表單是安全的：後端的 PaymentAttemptPolicy 規定同時只能有
 * 一筆進行中的嘗試，真的已經有一筆的話會回 payment_state_conflict，不會多建一筆。
 */
async function restoreLatestAttempt(): Promise<void> {
  isRestoring.value = true
  restoreFailed.value = false
  try {
    attempt.value = await fetchLatestPaymentAttempt(orderPublicId.value)
  }
  catch {
    restoreFailed.value = true
  }
  finally {
    isRestoring.value = false
  }
}

onMounted(loadOrder)

async function submitAttempt(): Promise<void> {
  if (!order.value || !canCreateAttempt.value) {
    return
  }

  isCreating.value = true
  actionError.value = undefined
  try {
    attempt.value = await createPaymentAttempt(
      order.value.publicId,
      { method: selectedMethod.value, orderRowVersion: order.value.rowVersion },
      createIdempotencyKey,
    )
    createIdempotencyKey = crypto.randomUUID()
    simulationKey = crypto.randomUUID()
  }
  catch (error) {
    actionError.value = describePaymentError(error, '建立付款方式時發生問題，請稍後再試。')
  }
  finally {
    isCreating.value = false
  }
}

async function completePayment(outcome: SimulatedPaymentOutcome = 'succeeded'): Promise<void> {
  if (!attempt.value || !canCompleteDemo.value) {
    return
  }

  isCompleting.value = true
  actionError.value = undefined
  try {
    attempt.value = await completeSimulatedPayment(attempt.value.publicId, {
      outcome,
      simulationKey,
    })
    simulationKey = crypto.randomUUID()
    order.value = await fetchOrder(orderPublicId.value)
  }
  catch (error) {
    actionError.value = isApiError(error) && error.status === 404
      ? '目前環境未開放模擬付款完成；付款嘗試已保留，可回訂單詳情查看狀態。'
      : describePaymentError(error, '更新付款結果時發生問題，請稍後再試。')
  }
  finally {
    isCompleting.value = false
  }
}

function describePaymentError(error: unknown, fallback: string): string {
  if (!isApiError(error)) {
    return fallback
  }

  switch (error.code) {
    case 'payment_method_not_allowed':
      return '這筆訂單不支援所選付款方式，請改用其他方式。'
    case 'payment_cod_amount_exceeded':
      return '訂單金額超過貨到付款上限，請改用其他方式。'
    case 'payment_cod_restricted_item':
      return '訂單含需預付商品，不能使用貨到付款。'
    case 'order_payment_deadline_expired':
    case 'payment_attempt_expired':
      return '付款期限已過，請回訂單確認後續處理方式。'
    case 'concurrency_conflict':
      return '訂單資料已更新，請重新載入後再選擇付款方式。'
    case 'payment_state_conflict':
      return '付款狀態已變更，請重新載入確認結果。'
    case 'idempotency_payload_conflict':
      return '這次操作與先前送出的內容不一致，請重新載入後再試。'
    default:
      return fallback
  }
}

function formatDateTime(value?: string | null): string {
  return value ? new Date(value).toLocaleString('zh-TW') : '—'
}

const attemptStatusLabel: Record<string, string> = {
  pending: '等待處理',
  processing: '處理中',
  awaitingPayment: '等待付款',
  paid: '已付款',
  failed: '付款失敗',
  expired: '已逾期',
  cancelled: '已取消',
}
</script>

<template>
  <section aria-labelledby="payment-page-title">
    <LoadingState
      v-if="pageState === 'loading'"
      label="付款資料載入中"
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
      <a :href="`/orders/${order.publicId}`">← 回訂單詳情</a>
      <h1 id="payment-page-title">
        訂單 {{ order.orderNumber }} 付款
      </h1>
      <p>應付總額：NT$ {{ order.amounts.grandTotal }} {{ order.amounts.currency }}</p>

      <p
        v-if="restoreFailed"
        role="alert"
      >
        無法載入先前的付款狀態，畫面可能不完整。
        <button
          type="button"
          :disabled="isRestoring"
          @click="restoreLatestAttempt"
        >
          重新載入付款狀態
        </button>
      </p>

      <EmptyState
        v-if="paymentCompleted"
        title="付款已完成"
        description="訂單已重新向伺服器確認付款狀態，可返回訂單詳情查看發票。"
      />

      <form
        v-else-if="canCreateAttempt"
        @submit.prevent="submitAttempt"
      >
        <label for="payment-method">付款方式</label>
        <select
          id="payment-method"
          v-model="selectedMethod"
        >
          <option
            v-for="method in paymentMethods"
            :key="method.value"
            :value="method.value"
          >
            {{ method.label }}
          </option>
        </select>
        <button
          type="submit"
          :disabled="isCreating"
        >
          {{ isCreating ? '建立中…' : '建立付款方式' }}
        </button>
      </form>

      <section
        v-if="attempt"
        aria-labelledby="attempt-title"
      >
        <h2 id="attempt-title">
          付款嘗試
        </h2>
        <p>狀態：{{ attemptStatusLabel[attempt.status] ?? attempt.status }}</p>
        <p>金額：NT$ {{ attempt.amount }} {{ attempt.currency }}</p>
        <template v-if="attempt.instruction">
          <p v-if="attempt.instruction.maskedAccount">
            付款帳號：{{ attempt.instruction.maskedAccount }}
          </p>
          <p v-if="attempt.instruction.code">
            繳費代碼：{{ attempt.instruction.code }}
          </p>
          <p v-if="attempt.instruction.expiresAtUtc">
            付款期限：{{ formatDateTime(attempt.instruction.expiresAtUtc) }}
          </p>
        </template>

        <p v-if="attempt.method === 'cashOnDelivery'">
          貨到付款會在完成配送或取貨時入帳，不使用前台模擬付款完成。
        </p>
        <button
          v-else-if="canCompleteDemo"
          type="button"
          data-test="complete-payment"
          :disabled="isCompleting"
          @click="completePayment('succeeded')"
        >
          {{ isCompleting ? '處理中…' : '模擬付款成功' }}
        </button>
      </section>

      <p
        v-if="actionError"
        role="alert"
      >
        {{ actionError }}
      </p>
    </template>
  </section>
</template>

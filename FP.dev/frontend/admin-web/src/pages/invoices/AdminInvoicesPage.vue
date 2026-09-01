<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import {
  useInvoiceIssuanceLookup,
  useInvoiceList,
  useIssueInvoice,
} from '../../features/invoices/useInvoices'
import {
  formatInvoiceDate,
  formatInvoiceMoney,
  invoiceStatusLabels,
} from '../../features/invoices/labels'
import type { SimulatedInvoiceStatus } from '../../features/invoices/types'
import { useSearchFilters } from '../../features/shared/useSearchFilters'

const { filters, listParams, search, goToPage } = useSearchFilters(20)
const router = useRouter()
const selectedStatus = ref<SimulatedInvoiceStatus | ''>('')
const statusOptions = Object.keys(invoiceStatusLabels) as SimulatedInvoiceStatus[]
const query = computed(() => ({
  ...listParams.value,
  statuses: selectedStatus.value ? [selectedStatus.value] : undefined,
}))
const { data: result, isPending, isError, error, refetch } = useInvoiceList(query)
const apiError = computed(() => isApiError(error.value) ? error.value : undefined)
const totalPages = computed(() => Number(result.value?.totalPages ?? 0))
const orderPublicId = ref('')
const idempotencyKey = ref('')
const issueFeedback = ref('')
const issuanceLookup = useInvoiceIssuanceLookup()
const issueInvoice = useIssueInvoice()
const issuanceError = computed(() => isApiError(issuanceLookup.error.value)
  ? issuanceLookup.error.value
  : undefined)
const issueError = computed(() => isApiError(issueInvoice.error.value)
  ? issueInvoice.error.value
  : undefined)
const canIssue = computed(() => Boolean(
  issuanceLookup.data.value?.orderIsPaid
  && !issuanceLookup.data.value.orderIsCancelled
  && !issuanceLookup.data.value.hasInvoice,
))

function changeStatus() {
  filters.pageNumber = 1
}

function createIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID()
  }
  return `invoice-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

function clearIssuanceSnapshot() {
  issuanceLookup.reset()
  idempotencyKey.value = ''
  issueFeedback.value = ''
}

async function lookupOrder() {
  const normalizedOrderPublicId = orderPublicId.value.trim()
  issueFeedback.value = ''
  idempotencyKey.value = ''
  try {
    await issuanceLookup.mutateAsync(normalizedOrderPublicId)
    idempotencyKey.value = createIdempotencyKey()
  } catch {
    // Mutation state owns the visible error and retry path.
  }
}

async function issueSelectedOrder() {
  const snapshot = issuanceLookup.data.value
  if (!snapshot || !canIssue.value || issueInvoice.isPending.value) {
    return
  }

  try {
    const issued = await issueInvoice.mutateAsync({
      orderPublicId: snapshot.orderPublicId,
      request: { orderRowVersion: snapshot.rowVersion },
      idempotencyKey: idempotencyKey.value,
    })
    await router.push(`/invoices/${issued.invoice.publicId}`)
  } catch (caught) {
    if (isApiError(caught) && caught.code === 'concurrency_conflict') {
      issueFeedback.value = '訂單狀態已更新，已重新查詢；請確認後再開立。'
      idempotencyKey.value = ''
      try {
        await issuanceLookup.mutateAsync(snapshot.orderPublicId)
        idempotencyKey.value = createIdempotencyKey()
      } catch {
        // Lookup mutation renders its own retryable error state.
      }
    }
  }
}
</script>

<template>
  <section aria-labelledby="invoice-list-title">
    <header>
      <h1 id="invoice-list-title">
        模擬發票管理
      </h1>
      <p>所有資料均為 DEMO 模擬發票，不具稅務或兌獎效力。</p>
    </header>

    <section aria-labelledby="manual-issue-title">
      <h2 id="manual-issue-title">
        手動開立
      </h2>
      <form
        aria-label="手動開立模擬發票"
        @submit.prevent="lookupOrder"
      >
        <label for="invoice-order-public-id">訂單 PublicId</label>
        <input
          id="invoice-order-public-id"
          v-model="orderPublicId"
          type="text"
          required
          autocomplete="off"
          placeholder="輸入訂單 PublicId"
          @input="clearIssuanceSnapshot"
        >
        <button
          type="submit"
          :disabled="issuanceLookup.isPending.value"
        >
          {{ issuanceLookup.isPending.value ? '查詢中…' : '查詢可開票狀態' }}
        </button>
      </form>

      <ErrorState
        v-if="issuanceLookup.isError.value"
        :correlation-id="issuanceError?.correlationId"
        :trace-id="issuanceError?.traceId"
        @retry="lookupOrder"
      />
      <div
        v-else-if="issuanceLookup.data.value"
        aria-live="polite"
      >
        <dl>
          <dt>訂單編號</dt>
          <dd>{{ issuanceLookup.data.value.orderNumber }}</dd>
          <dt>付款狀態</dt>
          <dd>{{ issuanceLookup.data.value.orderIsPaid ? '已付款' : '未付款' }}</dd>
          <dt>取消狀態</dt>
          <dd>{{ issuanceLookup.data.value.orderIsCancelled ? '已取消' : '未取消' }}</dd>
          <dt>發票狀態</dt>
          <dd>{{ issuanceLookup.data.value.hasInvoice ? '已有發票' : '尚未開立' }}</dd>
        </dl>
        <p v-if="!canIssue">
          僅限已付款、未取消且尚無發票的訂單手動開立。
        </p>
        <button
          v-else
          data-test="issue-invoice"
          type="button"
          :disabled="issueInvoice.isPending.value"
          @click="issueSelectedOrder"
        >
          {{ issueInvoice.isPending.value ? '開立中…' : '確認開立模擬發票' }}
        </button>
      </div>
      <p
        v-if="issueFeedback"
        role="status"
      >
        {{ issueFeedback }}
      </p>
      <ErrorState
        v-if="issueInvoice.isError.value && !issueFeedback"
        :correlation-id="issueError?.correlationId"
        :trace-id="issueError?.traceId"
        @retry="issueSelectedOrder"
      />
    </section>

    <form
      aria-label="發票搜尋"
      @submit.prevent="search"
    >
      <label for="invoice-query">發票號碼</label>
      <input
        id="invoice-query"
        v-model="filters.q"
        type="search"
        placeholder="例如 DEMO-202609"
      >
      <label for="invoice-status">狀態</label>
      <select
        id="invoice-status"
        v-model="selectedStatus"
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
          {{ invoiceStatusLabels[status] }}
        </option>
      </select>
      <button type="submit">
        搜尋
      </button>
    </form>

    <LoadingState
      v-if="isPending"
      label="發票清單載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="apiError?.correlationId"
      :trace-id="apiError?.traceId"
      @retry="() => refetch()"
    />
    <EmptyState
      v-else-if="!result?.items.length"
      title="沒有符合條件的模擬發票"
    />
    <template v-else>
      <table>
        <caption class="sr-only">
          模擬發票清單
        </caption>
        <thead>
          <tr>
            <th scope="col">
              發票號碼
            </th>
            <th scope="col">
              訂單
            </th>
            <th scope="col">
              狀態
            </th>
            <th scope="col">
              未稅／稅額／含稅
            </th>
            <th scope="col">
              開立時間
            </th>
            <th scope="col">
              操作
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="invoice in result.items"
            :key="invoice.publicId"
          >
            <td>{{ invoice.invoiceNumber }}</td>
            <td>{{ invoice.orderNumber }}</td>
            <td>{{ invoiceStatusLabels[invoice.status] }}</td>
            <td>
              {{ formatInvoiceMoney(invoice.netAmount) }}／
              {{ formatInvoiceMoney(invoice.taxAmount) }}／
              {{ formatInvoiceMoney(invoice.grossAmount) }}
            </td>
            <td>{{ formatInvoiceDate(invoice.issuedAtUtc) }}</td>
            <td>
              <RouterLink :to="`/invoices/${invoice.publicId}`">
                查看明細
              </RouterLink>
            </td>
          </tr>
        </tbody>
      </table>

      <nav aria-label="發票分頁">
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

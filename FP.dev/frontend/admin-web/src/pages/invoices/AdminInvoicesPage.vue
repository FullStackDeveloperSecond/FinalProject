<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState, ErrorState, LoadingState, PagePager } from '@doselect/web-shared/components'
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
import type { InvoiceSortOption } from '../../features/invoices/api'
import { useSearchFilters } from '../../features/shared/useSearchFilters'

const { filters, listParams, search, goToPage } = useSearchFilters(20)
const router = useRouter()
const selectedStatus = ref<SimulatedInvoiceStatus | ''>('')
const statusOptions = Object.keys(invoiceStatusLabels) as SimulatedInvoiceStatus[]
type InvoiceSortField = 'invoiceNumber' | 'orderNumber' | 'status' | 'issuedAt'
type SortDirection = 'Asc' | 'Desc'
const sortField = ref<InvoiceSortField>('invoiceNumber')
const sortDirection = ref<SortDirection>('Desc')
const sort = computed(() => `${sortField.value}${sortDirection.value}` as InvoiceSortOption)
const query = computed(() => ({
  ...listParams.value,
  statuses: selectedStatus.value ? [selectedStatus.value] : undefined,
  sort: sort.value,
}))
const { data: result, isPending, isError, error, refetch } = useInvoiceList(query)
const apiError = computed(() => isApiError(error.value) ? error.value : undefined)
const totalPages = computed(() => Number(result.value?.totalPages ?? 0))
const orderNumber = ref('')
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

function setSort(field: InvoiceSortField) {
  if (sortField.value === field) {
    sortDirection.value = sortDirection.value === 'Asc' ? 'Desc' : 'Asc'
  } else {
    sortField.value = field
    sortDirection.value = 'Asc'
  }
  filters.pageNumber = 1
}

function ariaSort(field: InvoiceSortField): 'ascending' | 'descending' | 'none' {
  if (sortField.value !== field) return 'none'
  return sortDirection.value === 'Asc' ? 'ascending' : 'descending'
}

function sortIndicator(field: InvoiceSortField): string {
  if (sortField.value !== field) return '↕'
  return sortDirection.value === 'Asc' ? '↑' : '↓'
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
  const normalizedOrderNumber = orderNumber.value.trim()
  issueFeedback.value = ''
  idempotencyKey.value = ''
  try {
    await issuanceLookup.mutateAsync(normalizedOrderNumber)
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
  <section
    class="finance-page"
    aria-labelledby="invoice-list-title"
  >
    <header>
      <h1 id="invoice-list-title">
        模擬發票管理
      </h1>
      <p>所有資料均為 DEMO 模擬發票，不具稅務或兌獎效力。</p>
    </header>

    <section
      class="finance-panel"
      aria-labelledby="manual-issue-title"
    >
      <h2 id="manual-issue-title">
        手動開立
      </h2>
      <form
        class="finance-filter"
        aria-label="手動開立模擬發票"
        @submit.prevent="lookupOrder"
      >
        <div class="finance-field">
          <label for="invoice-order-number">訂單號碼</label>
          <input
            id="invoice-order-number"
            v-model="orderNumber"
            type="text"
            required
            maxlength="64"
            autocomplete="off"
            placeholder="輸入訂單號碼"
            @input="clearIssuanceSnapshot"
          >
        </div>
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
        <dl
          class="finance-summary"
          aria-label="開票資格摘要"
        >
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
      class="finance-filter finance-panel"
      aria-label="發票搜尋"
      @submit.prevent="search"
    >
      <div class="finance-field">
        <label for="invoice-query">發票號碼</label>
        <input
          id="invoice-query"
          v-model="filters.q"
          type="search"
          placeholder="例如 DEMO-202609"
        >
      </div>
      <div class="finance-field">
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
      </div>
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
      <div
        class="table-scroll"
        role="region"
        aria-label="發票清單表格"
        tabindex="0"
      >
        <table>
          <caption class="sr-only">
            模擬發票清單
          </caption>
          <thead>
            <tr>
              <th
                scope="col"
                data-sort="invoiceNumber"
                :aria-sort="ariaSort('invoiceNumber')"
              >
                <button
                  type="button"
                  class="finance-sort-button"
                  aria-label="依發票號碼排序"
                  @click="setSort('invoiceNumber')"
                >
                  發票號碼 <span aria-hidden="true">{{ sortIndicator('invoiceNumber') }}</span>
                </button>
              </th>
              <th
                scope="col"
                data-sort="orderNumber"
                :aria-sort="ariaSort('orderNumber')"
              >
                <button
                  type="button"
                  class="finance-sort-button"
                  aria-label="依訂單號碼排序"
                  @click="setSort('orderNumber')"
                >
                  訂單 <span aria-hidden="true">{{ sortIndicator('orderNumber') }}</span>
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
                  aria-label="依發票狀態排序"
                  @click="setSort('status')"
                >
                  狀態 <span aria-hidden="true">{{ sortIndicator('status') }}</span>
                </button>
              </th>
              <th scope="col">
                未稅／稅額／含稅
              </th>
              <th
                scope="col"
                data-sort="issuedAt"
                :aria-sort="ariaSort('issuedAt')"
              >
                <button
                  type="button"
                  class="finance-sort-button"
                  aria-label="依開立時間排序"
                  @click="setSort('issuedAt')"
                >
                  開立時間 <span aria-hidden="true">{{ sortIndicator('issuedAt') }}</span>
                </button>
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
      </div>

      <PagePager
        :page="filters.pageNumber"
        :total-records="totalPages"
        :page-size="1"
        aria-label="發票分頁"
        @update:page="goToPage"
      />
    </template>
  </section>
</template>

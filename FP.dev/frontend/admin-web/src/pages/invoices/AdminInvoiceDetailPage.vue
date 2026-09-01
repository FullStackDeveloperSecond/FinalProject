<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState, ErrorState, HttpStatusPage, LoadingState } from '@doselect/web-shared/components'
import { useInvoice, useVoidInvoice } from '../../features/invoices/useInvoices'
import {
  formatInvoiceDate,
  formatInvoiceMoney,
  invoiceStatusLabels,
} from '../../features/invoices/labels'
import { describeApiError } from '../../features/shared/errorMessages'

const route = useRoute()
const invoicePublicId = computed(() => String(route.params.invoiceId))
const { data: invoice, isPending, isError, error, refetch } = useInvoice(invoicePublicId)
const apiError = computed(() => isApiError(error.value) ? error.value : undefined)
const voidMutation = useVoidInvoice()
const reasonCode = ref('')
const note = ref('')
const confirmed = ref(false)
const mayVoid = computed(() => invoice.value?.availableActions.includes('void') ?? false)
const voidError = computed(() => {
  const candidate = voidMutation.error.value
  return isApiError(candidate) ? describeApiError(candidate) : '發票作廢失敗，請稍後再試。'
})

async function submitVoid() {
  if (!invoice.value || !mayVoid.value || !confirmed.value || !reasonCode.value) {
    return
  }

  try {
    await voidMutation.mutateAsync({
      invoicePublicId: invoice.value.invoice.publicId,
      request: {
        reasonCode: reasonCode.value,
        note: note.value.trim() || null,
        rowVersion: invoice.value.invoice.rowVersion,
      },
    })
    confirmed.value = false
  }
  catch {
    // mutation 保留錯誤；管理員重新整理最新 RowVersion 後再決定是否重送。
  }
}
</script>

<template>
  <section aria-labelledby="invoice-detail-title">
    <LoadingState
      v-if="isPending"
      label="發票明細載入中"
    />
    <HttpStatusPage
      v-else-if="apiError?.status === 404"
      :status="404"
      home-href="/admin/invoices"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="apiError?.correlationId"
      :trace-id="apiError?.traceId"
      @retry="() => refetch()"
    />

    <template v-else-if="invoice">
      <p>
        <RouterLink to="/invoices">
          返回發票清單
        </RouterLink>
      </p>
      <h1 id="invoice-detail-title">
        模擬發票 {{ invoice.invoice.invoiceNumber }}
      </h1>
      <p><strong>{{ invoice.invoice.demoMarker }}</strong> — 此資料不具稅務或兌獎效力。</p>

      <dl>
        <dt>狀態</dt>
        <dd>{{ invoiceStatusLabels[invoice.invoice.status] }}</dd>
        <dt>訂單</dt>
        <dd>{{ invoice.orderNumber }}</dd>
        <dt>買受人</dt>
        <dd>{{ invoice.invoice.buyerEmailMasked ?? '—' }}</dd>
        <dt>統一編號</dt>
        <dd>{{ invoice.invoice.companyTaxIdMasked ?? '—' }}</dd>
        <dt>未稅／稅額／含稅</dt>
        <dd>
          {{ formatInvoiceMoney(invoice.invoice.netAmount) }}／
          {{ formatInvoiceMoney(invoice.invoice.taxAmount) }}／
          {{ formatInvoiceMoney(invoice.invoice.grossAmount) }}
        </dd>
        <dt>開立時間</dt>
        <dd>{{ formatInvoiceDate(invoice.invoice.issuedAtUtc) }}</dd>
        <dt>作廢時間</dt>
        <dd>{{ formatInvoiceDate(invoice.invoice.voidedAtUtc) }}</dd>
      </dl>

      <section aria-labelledby="invoice-items-title">
        <h2 id="invoice-items-title">
          發票明細
        </h2>
        <table>
          <thead>
            <tr>
              <th scope="col">
                項目
              </th>
              <th scope="col">
                數量
              </th>
              <th scope="col">
                未稅／稅額／含稅
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="item in invoice.invoice.items"
              :key="item.publicId"
            >
              <td>{{ item.productName }}（{{ item.skuCode }}）</td>
              <td>{{ item.quantity }}</td>
              <td>
                {{ formatInvoiceMoney(item.netAmount) }}／
                {{ formatInvoiceMoney(item.taxAmount) }}／
                {{ formatInvoiceMoney(item.grossAmount) }}
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <section aria-labelledby="invoice-allowances-title">
        <h2 id="invoice-allowances-title">
          折讓
        </h2>
        <EmptyState
          v-if="invoice.invoice.allowances.length === 0"
          title="尚無折讓"
        />
        <ul v-else>
          <li
            v-for="allowance in invoice.invoice.allowances"
            :key="allowance.publicId"
          >
            {{ allowance.allowanceNumber }}：{{ formatInvoiceMoney(allowance.grossAmount) }}，
            {{ formatInvoiceDate(allowance.issuedAtUtc) }}
          </li>
        </ul>
      </section>

      <section
        v-if="mayVoid"
        aria-labelledby="invoice-void-title"
      >
        <h2 id="invoice-void-title">
          作廢發票
        </h2>
        <p>只有訂單已整筆取消且尚未發生成功退款時可作廢；已有退款必須建立折讓。</p>
        <form @submit.prevent="submitVoid">
          <label for="invoice-void-reason">作廢原因</label>
          <select
            id="invoice-void-reason"
            v-model="reasonCode"
            required
          >
            <option
              value=""
              disabled
            >
              請選擇原因
            </option>
            <option value="order_cancelled">
              訂單整筆取消
            </option>
            <option value="merchant_correction">
              商家更正
            </option>
          </select>
          <label for="invoice-void-note">補充說明（選填）</label>
          <textarea
            id="invoice-void-note"
            v-model="note"
            maxlength="1000"
          />
          <label>
            <input
              v-model="confirmed"
              type="checkbox"
            >
            我已核對訂單取消與退款狀態，確認作廢並留下中央 Audit。
          </label>
          <p
            v-if="voidMutation.isError.value"
            role="alert"
          >
            {{ voidError }}
          </p>
          <button
            type="submit"
            :disabled="voidMutation.isPending.value || !confirmed || !reasonCode"
          >
            {{ voidMutation.isPending.value ? '處理中…' : '確認作廢' }}
          </button>
        </form>
      </section>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { ErrorState, HttpStatusPage, LoadingState } from '@doselect/web-shared/components'
import { useApproveRefund, useExecuteRefund, useRefund } from '../../features/refunds/useRefunds'
import {
  allocationSign,
  formatRefundDate,
  formatRefundMoney,
  refundAllocationLabels,
  refundStatusLabels,
} from '../../features/refunds/labels'
import { describeApiError } from '../../features/shared/errorMessages'

const route = useRoute()
const refundPublicId = computed(() => String(route.params.refundId))
const { data: refund, isPending, isError, error, refetch } = useRefund(refundPublicId)
const apiError = computed(() => isApiError(error.value) ? error.value : undefined)

const reasonCode = ref('')
const note = ref('')
const confirmed = ref(false)
const idempotencyKey = ref(createIdempotencyKey())
const executeMutation = useExecuteRefund()

const approveReasonCode = ref('')
const approveNote = ref('')
const approveConfirmed = ref(false)
const approveIdempotencyKey = ref(createIdempotencyKey())
const approveMutation = useApproveRefund()

const mayExecute = computed(() => refund.value?.status === 'approved')
const mayApprove = computed(() => refund.value?.status === 'pendingReview')
const executionError = computed(() => {
  const candidate = executeMutation.error.value
  return isApiError(candidate) ? describeApiError(candidate) : '退款執行失敗，請稍後再試。'
})
const approvalError = computed(() => {
  const candidate = approveMutation.error.value
  return isApiError(candidate) ? describeApiError(candidate) : '退款核准失敗，請稍後再試。'
})

function createIdempotencyKey(): string {
  return globalThis.crypto?.randomUUID?.()
    ?? `refund-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

async function submitExecution() {
  if (!refund.value || !mayExecute.value || !confirmed.value || !reasonCode.value) {
    return
  }

  try {
    await executeMutation.mutateAsync({
      refundPublicId: refund.value.publicId,
      request: {
        reasonCode: reasonCode.value,
        note: note.value.trim() || null,
        refundRowVersion: refund.value.rowVersion,
      },
      idempotencyKey: idempotencyKey.value,
    })
    confirmed.value = false
    idempotencyKey.value = createIdempotencyKey()
  }
  catch {
    // mutation 保留同一把 idempotency key 與錯誤，管理員可安全重送同一請求。
  }
}

async function submitApproval() {
  if (!refund.value || !mayApprove.value || !approveConfirmed.value || !approveReasonCode.value) {
    return
  }

  try {
    await approveMutation.mutateAsync({
      refundPublicId: refund.value.publicId,
      request: {
        reasonCode: approveReasonCode.value,
        note: approveNote.value.trim() || null,
        refundRowVersion: refund.value.rowVersion,
      },
      idempotencyKey: approveIdempotencyKey.value,
    })
    approveConfirmed.value = false
    approveIdempotencyKey.value = createIdempotencyKey()
  }
  catch {
    // mutation 保留同一把 idempotency key 與錯誤，管理員可安全重送同一請求。
  }
}
</script>

<template>
  <section aria-labelledby="refund-detail-title">
    <LoadingState
      v-if="isPending"
      label="退款明細載入中"
    />
    <HttpStatusPage
      v-else-if="apiError?.status === 404"
      :status="404"
      home-href="/admin/refunds"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="apiError?.correlationId"
      :trace-id="apiError?.traceId"
      @retry="() => refetch()"
    />

    <template v-else-if="refund">
      <p>
        <RouterLink to="/refunds">
          返回退款清單
        </RouterLink>
      </p>
      <h1 id="refund-detail-title">
        退款 {{ refund.refundNumber }}
      </h1>

      <dl>
        <dt>狀態</dt>
        <dd>{{ refundStatusLabels[refund.status] }}</dd>
        <dt>申請金額</dt>
        <dd>{{ formatRefundMoney(refund.requestedAmount) }}</dd>
        <dt>退款上限（核准金額）</dt>
        <dd>{{ formatRefundMoney(refund.approvedAmount) }}</dd>
        <dt>成功退款金額</dt>
        <dd>{{ formatRefundMoney(refund.succeededAmount) }}</dd>
        <dt>訂單</dt>
        <dd>
          <RouterLink :to="`/orders/${refund.orderPublicId}`">
            {{ refund.orderPublicId }}
          </RouterLink>
        </dd>
        <dt>退貨案件</dt>
        <dd>
          <RouterLink
            v-if="refund.returnPublicId"
            :to="`/returns/${refund.returnPublicId}`"
          >
            {{ refund.returnPublicId }}
          </RouterLink>
          <template v-else>
            —
          </template>
        </dd>
        <dt>建立時間</dt>
        <dd>{{ formatRefundDate(refund.createdAtUtc) }}</dd>
        <dt>完成時間</dt>
        <dd>{{ formatRefundDate(refund.succeededAtUtc) }}</dd>
      </dl>

      <section aria-labelledby="refund-allocations-title">
        <h2 id="refund-allocations-title">
          可信分攤明細
        </h2>
        <p>「＋」增加退款，「－」從退款扣回；金額由後端交易快照計算，介面不能修改。</p>
        <table v-if="refund.allocations.length">
          <thead>
            <tr>
              <th scope="col">
                類型
              </th>
              <th scope="col">
                商品／數量
              </th>
              <th scope="col">
                金額
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(allocation, index) in refund.allocations"
              :key="`${allocation.type}-${allocation.orderItemPublicId ?? index}`"
            >
              <td>{{ refundAllocationLabels[allocation.type] }}</td>
              <td>
                <template v-if="allocation.orderItemPublicId">
                  {{ allocation.orderItemPublicId }} × {{ allocation.quantity }}
                </template>
                <template v-else>
                  —
                </template>
              </td>
              <td>
                {{ allocationSign(allocation.type) }}{{ formatRefundMoney(allocation.amount) }}
              </td>
            </tr>
          </tbody>
        </table>
        <p v-else-if="refund.status === 'cancelled'">
          核准時重算後已無款可退，退款已終止為「已取消」，未產生退款分攤。
        </p>
        <p v-else>
          尚未執行退款；後端會在執行交易內依可信快照計算並保存分攤，成功後顯示於此。
        </p>
      </section>

      <section aria-labelledby="refund-history-title">
        <h2 id="refund-history-title">
          處理歷程
        </h2>
        <ul>
          <li>申請：{{ refund.requestedBy?.maskedLabel ?? '系統／未知' }}，{{ formatRefundDate(refund.createdAtUtc) }}</li>
          <li v-if="refund.approvedBy">
            核准：{{ refund.approvedBy.maskedLabel }}
          </li>
          <li v-if="refund.executedBy">
            執行：{{ refund.executedBy.maskedLabel }}，{{ formatRefundDate(refund.succeededAtUtc) }}
          </li>
        </ul>
      </section>

      <section
        v-if="mayApprove"
        aria-labelledby="refund-approve-title"
      >
        <h2 id="refund-approve-title">
          核准退款
        </h2>
        <p>此操作要求 FinanceManager／SuperAdmin 且目前登入已通過 TOTP；送出後會留下中央 Audit。核准金額由後端依可信交易快照重新計算，若重算後已無款可退，退款會直接終止為「已取消」，不需要另外處理。</p>
        <form @submit.prevent="submitApproval">
          <label for="refund-approve-reason">核准原因</label>
          <select
            id="refund-approve-reason"
            v-model="approveReasonCode"
            name="approveReasonCode"
            required
          >
            <option
              value=""
              disabled
            >
              請選擇原因
            </option>
            <option value="customer_request">
              顧客退款申請
            </option>
            <option value="merchant_correction">
              商家更正
            </option>
            <option value="return_approved">
              退貨已核准
            </option>
          </select>

          <label for="refund-approve-note">補充說明（選填）</label>
          <textarea
            id="refund-approve-note"
            v-model="approveNote"
            name="approveNote"
            maxlength="1000"
          />

          <label>
            <input
              v-model="approveConfirmed"
              name="approveConfirmed"
              type="checkbox"
            >
            我已核對申請金額與訂單，確認核准後金額將依後端重新計算。
          </label>

          <p
            v-if="approveMutation.isError.value"
            role="alert"
          >
            {{ approvalError }}
          </p>

          <button
            type="submit"
            :disabled="approveMutation.isPending.value || !approveConfirmed || !approveReasonCode"
          >
            {{ approveMutation.isPending.value ? '核准中…' : '確認核准退款' }}
          </button>
        </form>
      </section>

      <section
        v-if="mayExecute"
        aria-labelledby="refund-execute-title"
      >
        <h2 id="refund-execute-title">
          執行退款
        </h2>
        <p>此操作要求 FinanceManager／SuperAdmin 且目前登入已通過 TOTP；送出後會留下中央 Audit。</p>
        <form @submit.prevent="submitExecution">
          <label for="refund-reason">執行原因</label>
          <select
            id="refund-reason"
            v-model="reasonCode"
            name="reasonCode"
            required
          >
            <option
              value=""
              disabled
            >
              請選擇原因
            </option>
            <option value="customer_request">
              顧客退款申請
            </option>
            <option value="merchant_correction">
              商家更正
            </option>
            <option value="return_approved">
              退貨已核准
            </option>
          </select>

          <label for="refund-note">補充說明（選填）</label>
          <textarea
            id="refund-note"
            v-model="note"
            name="note"
            maxlength="1000"
          />

          <label>
            <input
              v-model="confirmed"
              name="confirmed"
              type="checkbox"
            >
            我已核對退款上限、分攤正負方向與訂單，確認執行不可重複的退款副作用。
          </label>

          <p
            v-if="executeMutation.isError.value"
            role="alert"
          >
            {{ executionError }}
          </p>

          <button
            type="submit"
            :disabled="executeMutation.isPending.value || !confirmed || !reasonCode"
          >
            {{ executeMutation.isPending.value ? '執行中…' : '確認執行退款' }}
          </button>
        </form>
      </section>
    </template>
  </section>
</template>

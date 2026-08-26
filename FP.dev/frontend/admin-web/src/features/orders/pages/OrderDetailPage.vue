<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import { EmptyState, ErrorState, HttpStatusPage, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { ORDER_ACTION_OPTIONS, orderStatusLabel, summaryStatusLabel, badgeLabel } from '../api'
import {
  useAdminOrderActionMutation,
  useAdminOrderDetailQuery,
  useAdminOrderRecipientQuery,
} from '../queries/useAdminOrders'

const route = useRoute()
const publicId = computed(() => String(route.params.publicId))

const { data: order, isPending, isError, error, refetch } = useAdminOrderDetailQuery(publicId)
const apiError = computed(() => (isApiError(error.value) ? error.value : undefined))

const showRecipient = ref(false)
const { data: recipient, isPending: isRecipientPending } = useAdminOrderRecipientQuery(publicId, showRecipient)

const actionMutation = useAdminOrderActionMutation()
const selectedAction = ref('')
const reasonCode = ref('')
const note = ref('')
const actionErrorMessage = ref<string | undefined>(undefined)

const selectedActionOption = computed(() =>
  ORDER_ACTION_OPTIONS.find(option => option.value === selectedAction.value))

function startAction(actionName: string): void {
  selectedAction.value = actionName
  reasonCode.value = ''
  note.value = ''
  actionErrorMessage.value = undefined
}

function cancelActionForm(): void {
  selectedAction.value = ''
}

async function submitAction(): Promise<void> {
  if (!order.value || !selectedAction.value) {
    return
  }
  if (selectedActionOption.value?.requiresReason && !reasonCode.value) {
    return
  }

  actionErrorMessage.value = undefined
  try {
    await actionMutation.mutateAsync({
      publicId: order.value.publicId,
      actionName: selectedAction.value,
      request: {
        reasonCode: reasonCode.value || undefined,
        note: note.value || undefined,
        rowVersion: order.value.rowVersion,
      },
    })
    selectedAction.value = ''
  }
  catch (submitError) {
    actionErrorMessage.value = isApiError(submitError)
      ? describeActionError(submitError.code)
      : '執行操作時發生問題，請稍後再試。'
  }
}

function describeActionError(code: string): string {
  switch (code) {
    case 'order_state_conflict':
      return '訂單狀態已變更，請重新整理後再試一次。'
    case 'order_cancellation_not_allowed':
      return '這筆訂單目前的狀態已無法取消。'
    case 'concurrency_conflict':
      return '訂單資料已被更新，請重新整理後再試一次。'
    case 'validation_failed':
      return '輸入內容不正確，請確認後再試一次。'
    default:
      return '執行操作時發生問題，請稍後再試。'
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
    <LoadingState
      v-if="isPending"
      label="訂單資料載入中"
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

    <HttpStatusPage
      v-else-if="apiError?.status === 404"
      :status="404"
      home-href="/orders"
    />

    <ErrorState
      v-else-if="isError"
      :correlation-id="apiError?.correlationId"
      :trace-id="apiError?.traceId"
      @retry="() => refetch()"
    />

    <template v-else-if="order">
      <h1 id="page-title">
        訂單 {{ order.orderNumber }}
      </h1>
      <p>
        {{ summaryStatusLabel(order.summaryStatus) }}
        <span
          v-for="badge in order.badges"
          :key="badge"
        >（{{ badgeLabel(badge) }}）</span>
      </p>
      <dl>
        <dt>訂單狀態</dt>
        <dd>{{ orderStatusLabel[order.orderStatus] ?? order.orderStatus }}</dd>
        <dt>付款狀態</dt>
        <dd>{{ order.paymentStatus }}</dd>
        <dt>物流狀態</dt>
        <dd>{{ order.fulfillmentStatus }}</dd>
        <dt>組裝狀態</dt>
        <dd>{{ order.assemblyStatus }}</dd>
        <dt>退款狀態</dt>
        <dd>{{ order.orderRefundStatus }}</dd>
        <dt>買家</dt>
        <dd>{{ order.buyerType === 'Member' ? '會員' : '訪客' }}／{{ order.maskedBuyerEmail }}</dd>
        <dt>配送方式</dt>
        <dd>
          {{ order.shippingMethodCode }}<template v-if="order.storeName">
            （{{ order.storeName }}）
          </template>
        </dd>
      </dl>

      <section aria-labelledby="items-title">
        <h2 id="items-title">
          商品明細
        </h2>
        <table>
          <thead>
            <tr>
              <th scope="col">
                商品
              </th>
              <th scope="col">
                數量
              </th>
              <th scope="col">
                單價
              </th>
              <th scope="col">
                折扣
              </th>
              <th scope="col">
                小計
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="item in order.items"
              :key="item.publicId"
            >
              <td>{{ item.productNameSnapshot }}（{{ item.skuNameSnapshot }}）</td>
              <td>{{ item.quantity }}</td>
              <td>NT$ {{ item.finalUnitPrice }}</td>
              <td>NT$ {{ item.discountAllocation }}</td>
              <td>NT$ {{ item.lineTotal }}</td>
            </tr>
          </tbody>
        </table>
        <dl>
          <dt>商品小計</dt>
          <dd>NT$ {{ order.amounts.merchandiseSubtotal }}</dd>
          <dt>折扣</dt>
          <dd>NT$ {{ order.amounts.itemDiscountTotal }}</dd>
          <dt>運費</dt>
          <dd>NT$ {{ order.amounts.shippingFee }}</dd>
          <dt>組裝費</dt>
          <dd>NT$ {{ order.amounts.assemblyFee }}</dd>
          <dt>應付總額</dt>
          <dd>NT$ {{ order.amounts.grandTotal }}</dd>
          <dt>已付金額</dt>
          <dd>NT$ {{ order.amounts.paidAmount }}</dd>
          <dt>已退金額</dt>
          <dd>NT$ {{ order.amounts.refundedAmount }}</dd>
        </dl>
      </section>

      <section aria-labelledby="recipient-title">
        <h2 id="recipient-title">
          收件資料
        </h2>
        <button
          v-if="!showRecipient"
          type="button"
          @click="showRecipient = true"
        >
          查看完整收件資料
        </button>
        <LoadingState
          v-else-if="isRecipientPending"
          label="收件資料載入中"
        />
        <dl v-else-if="recipient">
          <dt>收件人</dt>
          <dd>{{ recipient.recipientName }}</dd>
          <dt>電話</dt>
          <dd>{{ recipient.recipientPhone }}</dd>
          <dt>Email</dt>
          <dd>{{ recipient.recipientEmail }}</dd>
          <dt>地址</dt>
          <dd>
            <template v-if="recipient.postalCode">
              {{ recipient.postalCode }} {{ recipient.recipientCity }}{{ recipient.recipientDistrict }}{{ recipient.addressLine1 }}{{ recipient.addressLine2 }}
            </template>
            <template v-else-if="recipient.storeName">
              超商取貨：{{ recipient.storeName }}（{{ recipient.storeAddress }}）
            </template>
          </dd>
        </dl>
      </section>

      <section aria-labelledby="history-title">
        <h2 id="history-title">
          狀態歷程
        </h2>
        <EmptyState
          v-if="order.statusHistory.length === 0"
          title="尚無狀態變更紀錄"
        />
        <ul v-else>
          <li
            v-for="(entry, index) in order.statusHistory"
            :key="index"
          >
            {{ formatDateTime(entry.occurredAtUtc) }}
            — {{ entry.stateDimension }}：{{ entry.fromStatus ?? '（初始）' }} → {{ entry.toStatus }}
            <template v-if="entry.reasonCode">
              （原因：{{ entry.reasonCode }}）
            </template>
          </li>
        </ul>
      </section>

      <section aria-labelledby="actions-title">
        <h2 id="actions-title">
          操作
        </h2>
        <EmptyState
          v-if="order.availableActions.length === 0"
          title="目前沒有可執行的操作"
        />
        <template v-else>
          <button
            v-for="actionName in order.availableActions"
            :key="actionName"
            type="button"
            @click="startAction(actionName)"
          >
            {{ ORDER_ACTION_OPTIONS.find(option => option.value === actionName)?.label ?? actionName }}
          </button>

          <form
            v-if="selectedAction"
            @submit.prevent="submitAction"
          >
            <p>
              確定要執行「{{ selectedActionOption?.label ?? selectedAction }}」嗎？此操作會變更訂單狀態並留下紀錄。
            </p>
            <template v-if="selectedActionOption?.requiresReason">
              <label for="action-reason">原因</label>
              <select
                id="action-reason"
                v-model="reasonCode"
                required
              >
                <option
                  value=""
                  disabled
                >
                  請選擇原因
                </option>
                <option value="merchant_unfulfillable">
                  商家無法履約
                </option>
                <option value="customer_request">
                  顧客要求（管理員代處理）
                </option>
                <option value="other">
                  其他原因
                </option>
              </select>
            </template>

            <label for="action-note">補充說明（選填）</label>
            <textarea
              id="action-note"
              v-model="note"
              maxlength="500"
            />

            <p
              v-if="actionErrorMessage"
              role="alert"
            >
              {{ actionErrorMessage }}
            </p>

            <button
              type="submit"
              :disabled="actionMutation.isPending.value || (selectedActionOption?.requiresReason && !reasonCode)"
            >
              {{ actionMutation.isPending.value ? '處理中…' : '確認執行' }}
            </button>
            <button
              type="button"
              :disabled="actionMutation.isPending.value"
              @click="cancelActionForm"
            >
              取消
            </button>
          </form>
        </template>
      </section>
    </template>
  </section>
</template>

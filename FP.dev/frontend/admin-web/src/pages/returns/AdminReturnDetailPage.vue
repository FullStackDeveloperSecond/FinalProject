<script setup lang="ts">
import { ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  useAdminReturnDetailQuery,
  useExtendShipmentDeadlineMutation,
  useInspectReturnMutation,
  useReceiveReturnMutation,
  useReviewReturnMutation,
} from '../../features/returns/queries'
import { conditionCodeOptions, formatDateTime, priorityLabels, statusLabels } from '../../features/returns/labels'

const route = useRoute()
const returnId = computed(() => String(route.params.returnId))

const { data, isPending, isError, error, refetch } = useAdminReturnDetailQuery(returnId)
const reviewMutation = useReviewReturnMutation(returnId)
const receiveMutation = useReceiveReturnMutation(returnId)
const inspectMutation = useInspectReturnMutation(returnId)
const extendMutation = useExtendShipmentDeadlineMutation(returnId)

const reviewReasonCode = ref('')
const reviewNote = ref('')
const receiveNote = ref('')
const extendReasonCode = ref('')
const inspectionLines = reactive<Record<string, { conditionCode: string, disposition: 0 | 1 | 2, note: string }>>({})

const canReview = computed(() => data.value?.availableActions.includes('review') ?? false)
const canReceive = computed(() => data.value?.availableActions.includes('receive') ?? false)
const canInspect = computed(() => data.value?.availableActions.includes('inspect') ?? false)
const canExtend = computed(() => data.value?.availableActions.includes('extendShipmentDeadline') ?? false)

function ensureInspectionLine(itemPublicId: string) {
  inspectionLines[itemPublicId] ??= { conditionCode: conditionCodeOptions[0], disposition: 0, note: '' }
  return inspectionLines[itemPublicId]
}

async function handleApprove() {
  if (!data.value) {
    return
  }

  await reviewMutation.mutateAsync({
    approved: true,
    items: data.value.return.items.map((item) => ({
      returnItemPublicId: item.publicId,
      approvedQuantity: item.quantity,
      inspectionRequired: true,
    })),
    reasonCode: reviewReasonCode.value.trim() || 'eligible',
    note: reviewNote.value.trim() || null,
    returnRowVersion: data.value.return.rowVersion,
  })
}

async function handleReject() {
  if (!data.value) {
    return
  }

  await reviewMutation.mutateAsync({
    approved: false,
    items: [],
    reasonCode: reviewReasonCode.value.trim() || 'not-eligible',
    note: reviewNote.value.trim() || null,
    returnRowVersion: data.value.return.rowVersion,
  })
}

async function handleReceive() {
  if (!data.value) {
    return
  }

  await receiveMutation.mutateAsync({
    note: receiveNote.value.trim() || null,
    returnRowVersion: data.value.return.rowVersion,
  })
}

async function handleInspect() {
  if (!data.value) {
    return
  }

  await inspectMutation.mutateAsync({
    items: data.value.return.items.map((item) => {
      const line = ensureInspectionLine(item.publicId)
      return {
        returnItemPublicId: item.publicId,
        conditionCode: line.conditionCode,
        disposition: line.disposition,
        note: line.note.trim() || null,
      }
    }),
    returnRowVersion: data.value.return.rowVersion,
  })
}

async function handleExtend() {
  if (!data.value) {
    return
  }

  await extendMutation.mutateAsync({
    reasonCode: extendReasonCode.value.trim() || 'customer-requested',
    returnRowVersion: data.value.return.rowVersion,
  })
}

function isConflict(err: unknown): boolean {
  return isApiError(err) && err.status === 409
}
</script>

<template>
  <section aria-labelledby="admin-return-detail-title">
    <RouterLink to="/returns">
      ← 返回退貨案件列表
    </RouterLink>

    <LoadingState v-if="isPending" />
    <ErrorState
      v-else-if="isError"
      :title="isApiError(error) && error.status === 404 ? '找不到這個退貨案件' : '無法載入退貨案件'"
      :description="isApiError(error) ? error.message : '請稍後再試一次。'"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      :trace-id="isApiError(error) ? error.traceId : undefined"
      @retry="refetch()"
    />

    <template v-else-if="data">
      <h1 id="admin-return-detail-title">
        {{ data.return.returnNumber }}
      </h1>
      <dl class="admin-return-detail__summary">
        <div>
          <dt>狀態</dt>
          <dd>{{ statusLabels[data.return.status] }}</dd>
        </div>
        <div>
          <dt>優先度</dt>
          <dd>{{ priorityLabels[data.return.priority] }}</dd>
        </div>
        <div>
          <dt>訂單編號</dt>
          <dd>{{ data.return.orderNumber }}</dd>
        </div>
        <div v-if="data.return.returnShipmentDueAtUtc">
          <dt>寄回期限</dt>
          <dd>
            {{ formatDateTime(data.return.returnShipmentDueAtUtc) }}
            <span v-if="data.return.shipmentDeadlineExtended">（已延長一次）</span>
          </dd>
        </div>
      </dl>

      <section
        v-if="data.return.description"
        aria-labelledby="admin-return-description-title"
      >
        <h2 id="admin-return-description-title">
          顧客申請說明
        </h2>
        <p class="admin-return-detail__description">
          {{ data.return.description }}
        </p>
      </section>

      <section aria-labelledby="admin-return-items-title">
        <h2 id="admin-return-items-title">
          退貨品項
        </h2>
        <ul class="admin-return-detail__items">
          <li
            v-for="item in data.return.items"
            :key="item.publicId"
          >
            {{ item.productNameSnapshot || item.skuCodeSnapshot }}｜數量 {{ item.quantity }}｜檢查狀態 {{ item.inspectionStatus }}
          </li>
        </ul>
      </section>

      <section
        v-if="canReview"
        aria-labelledby="admin-return-review-title"
        class="admin-return-detail__action-panel"
      >
        <h2 id="admin-return-review-title">
          審核
        </h2>
        <label>
          <span>理由代碼</span>
          <input
            v-model="reviewReasonCode"
            type="text"
          >
        </label>
        <label>
          <span>備註</span>
          <textarea
            v-model="reviewNote"
            rows="2"
          />
        </label>
        <p
          v-if="isConflict(reviewMutation.error.value)"
          class="admin-return-detail__conflict"
          role="alert"
        >
          此案件已被其他人更新，請重新整理後再操作。
          <button
            type="button"
            @click="refetch()"
          >
            重新整理
          </button>
        </p>
        <div class="admin-return-detail__actions">
          <button
            type="button"
            :disabled="reviewMutation.isPending.value"
            @click="handleApprove"
          >
            核准
          </button>
          <button
            type="button"
            :disabled="reviewMutation.isPending.value"
            @click="handleReject"
          >
            拒絕
          </button>
        </div>
      </section>

      <section
        v-if="canReceive"
        aria-labelledby="admin-return-receive-title"
        class="admin-return-detail__action-panel"
      >
        <h2 id="admin-return-receive-title">
          收貨登記
        </h2>
        <label>
          <span>備註</span>
          <textarea
            v-model="receiveNote"
            rows="2"
          />
        </label>
        <p
          v-if="isConflict(receiveMutation.error.value)"
          class="admin-return-detail__conflict"
          role="alert"
        >
          此案件已被其他人更新，請重新整理後再操作。
        </p>
        <button
          type="button"
          :disabled="receiveMutation.isPending.value"
          @click="handleReceive"
        >
          確認收貨
        </button>
      </section>

      <section
        v-if="canInspect"
        aria-labelledby="admin-return-inspect-title"
        class="admin-return-detail__action-panel"
      >
        <h2 id="admin-return-inspect-title">
          商品檢查
        </h2>
        <div
          v-for="item in data.return.items"
          :key="item.publicId"
          class="admin-return-detail__inspect-row"
        >
          <p>{{ item.productNameSnapshot || item.skuCodeSnapshot }}</p>
          <label>
            <span>商品狀態</span>
            <select v-model="ensureInspectionLine(item.publicId).conditionCode">
              <option
                v-for="code in conditionCodeOptions"
                :key="code"
                :value="code"
              >
                {{ code }}
              </option>
            </select>
          </label>
          <label>
            <span>回補判定</span>
            <select v-model.number="ensureInspectionLine(item.publicId).disposition">
              <option :value="0">
                可轉售 Resellable
              </option>
              <option :value="1">
                隔離 Quarantine
              </option>
              <option :value="2">
                報廢 Scrap
              </option>
            </select>
          </label>
        </div>
        <p
          v-if="isConflict(inspectMutation.error.value)"
          class="admin-return-detail__conflict"
          role="alert"
        >
          此案件已被其他人更新，請重新整理後再操作。
        </p>
        <button
          type="button"
          :disabled="inspectMutation.isPending.value"
          @click="handleInspect"
        >
          送出檢查結果
        </button>
      </section>

      <section
        v-if="canExtend"
        aria-labelledby="admin-return-extend-title"
        class="admin-return-detail__action-panel"
      >
        <h2 id="admin-return-extend-title">
          延長寄回期限
        </h2>
        <label>
          <span>延長原因</span>
          <input
            v-model="extendReasonCode"
            type="text"
          >
        </label>
        <p
          v-if="isConflict(extendMutation.error.value)"
          class="admin-return-detail__conflict"
          role="alert"
        >
          此案件已被其他人更新，請重新整理後再操作。
        </p>
        <button
          type="button"
          :disabled="extendMutation.isPending.value"
          @click="handleExtend"
        >
          延長 7 天
        </button>
      </section>

      <section aria-labelledby="admin-return-refund-title">
        <h2 id="admin-return-refund-title">
          可退款品項預覽
        </h2>
        <p class="admin-return-detail__hint">
          僅供退款人員參考，實際退款金額與執行由退款模組處理。
        </p>
        <ul class="admin-return-detail__items">
          <li
            v-for="preview in data.refundableItemsPreview"
            :key="preview.returnItemPublicId"
          >
            {{ preview.skuCodeSnapshot }}｜數量 {{ preview.quantity }}
          </li>
        </ul>
      </section>

      <section aria-labelledby="admin-return-history-title">
        <h2 id="admin-return-history-title">
          歷程紀錄
        </h2>
        <ul class="admin-return-detail__history">
          <li
            v-for="(entry, index) in data.history"
            :key="index"
          >
            {{ formatDateTime(entry.occurredAtUtc) }}｜{{ entry.fromStatus !== null ? statusLabels[entry.fromStatus] : '—' }} → {{ statusLabels[entry.toStatus] }}
            <span v-if="entry.reasonCode">（{{ entry.reasonCode }}）</span>
          </li>
        </ul>
      </section>
    </template>
  </section>
</template>

<style scoped>
.admin-return-detail__summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
  gap: 1rem;
  margin-block: 1.5rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.75rem;
}

.admin-return-detail__summary dt {
  font-size: 0.75rem;
  color: #6b7280;
}

.admin-return-detail__summary dd {
  margin-inline-start: 0;
  font-weight: 700;
}

.admin-return-detail__items,
.admin-return-detail__history {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding-left: 1.25rem;
}

.admin-return-detail__action-panel {
  margin-top: 1.5rem;
  padding: 1rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.admin-return-detail__action-panel label {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  font-weight: 700;
}

.admin-return-detail__action-panel input,
.admin-return-detail__action-panel select,
.admin-return-detail__action-panel textarea {
  font-weight: 400;
  font: inherit;
  padding: 0.5rem 0.625rem;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
}

.admin-return-detail__inspect-row {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 1rem;
  padding-block: 0.5rem;
  border-bottom: 1px solid #e5e7eb;
}

.admin-return-detail__actions {
  display: flex;
  gap: 0.75rem;
}

.admin-return-detail__conflict {
  color: #b91c1c;
}

.admin-return-detail__hint {
  color: #6b7280;
  font-size: 0.875rem;
}

.admin-return-detail__description {
  white-space: pre-wrap;
  color: #374151;
}
</style>

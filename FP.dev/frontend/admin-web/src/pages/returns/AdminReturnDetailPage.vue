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
import {
  assemblyFeeDispositionOptions,
  conditionCodeOptions,
  formatDateTime,
  priorityLabels,
  statusLabels,
} from '../../features/returns/labels'
import type { AssemblyFeeDisposition, RestockDisposition } from '../../features/returns/types'

const route = useRoute()
const returnId = computed(() => String(route.params.returnId))

const { data, isPending, isError, error, refetch } = useAdminReturnDetailQuery(returnId)
const reviewMutation = useReviewReturnMutation(returnId)
const receiveMutation = useReceiveReturnMutation(returnId)
const inspectMutation = useInspectReturnMutation(returnId)
const extendMutation = useExtendShipmentDeadlineMutation(returnId)

const reviewReasonCode = ref('')
const reviewNote = ref('')
// Defaults to true (the physical-return path). Deliberately a plain ref, not derived from the
// query's `data`, so a background refetch while the admin is mid-review never silently resets
// their choice.
const requiresShipmentInspection = ref(true)
const receiveNote = ref('')
const extendReasonCode = ref('')
const inspectionLines = reactive<Record<string, { conditionCode: string, disposition: RestockDisposition, note: string }>>({})

// alex 2026-09-05 #109 裁定：這兩欄不得有會影響退款結果的靜默預設值，一律從空字串
// 起始，逼管理員自己選、自己填——不能讓 UI 替他們決定 notApplicable／0。
const reviewAssemblyFeeDisposition = ref<AssemblyFeeDisposition | ''>('')
// 刻意維持原始字串，不轉成 JavaScript number 再做任何四捨五入——這是退款計算會用
// 到的可信輸入，JS 的 IEEE-754 浮點數與後端 decimal／MidpointRounding.AwayFromZero
// 不是同一套規則（例如 1.005 用 Math.round(value*100)/100 會因為浮點誤差變成
// 1.00，但後端規則應該是 1.01）。畫面只驗證格式，不能替管理員「修正」金額
// （alex 2026-09-05 #111 review P2 裁定）。<input type="text"> 搭配下面的正規表達式
// 檢查，不用 type="number"：那個型別的 v-model 會把值轉成 number，一樣會弄丟原始
// 字串。
const reviewReturnShippingCost = ref('')
const inspectAssemblyFeeDisposition = ref<AssemblyFeeDisposition | ''>('')
const inspectReturnShippingCost = ref('')

const canReview = computed(() => data.value?.availableActions.includes('review') ?? false)
const canReceive = computed(() => data.value?.availableActions.includes('receive') ?? false)
const canInspect = computed(() => data.value?.availableActions.includes('inspect') ?? false)
const canExtend = computed(() => data.value?.availableActions.includes('extendShipmentDeadline') ?? false)

// 取消勾選「需要寄回檢查」才會建立 Refund，此時才需要可信欄位（見 #109 D1）。
const reviewNeedsTrustedFields = computed(() => !requiresShipmentInspection.value)

// 非負、最多兩位小數；超過兩位小數（例如 1.005）或負數一律擋下送出，不自動修正。
const shippingCostPattern = /^\d+(\.\d{1,2})?$/

function isValidShippingCost(value: string): boolean {
  return shippingCostPattern.test(value.trim())
}

const canSubmitApprove = computed(() => {
  if (!reviewNeedsTrustedFields.value) {
    return true
  }
  return reviewAssemblyFeeDisposition.value !== '' && isValidShippingCost(reviewReturnShippingCost.value)
})

const canSubmitInspect = computed(() =>
  inspectAssemblyFeeDisposition.value !== '' && isValidShippingCost(inspectReturnShippingCost.value))

function ensureInspectionLine(itemPublicId: string) {
  inspectionLines[itemPublicId] ??= { conditionCode: conditionCodeOptions[0], disposition: 'resellable', note: '' }
  return inspectionLines[itemPublicId]
}

async function handleApprove() {
  if (!data.value || !canSubmitApprove.value) {
    return
  }

  await reviewMutation.mutateAsync({
    approved: true,
    items: data.value.return.items.map((item) => ({
      returnItemPublicId: item.publicId,
      approvedQuantity: item.quantity,
      inspectionRequired: requiresShipmentInspection.value,
    })),
    reasonCode: reviewReasonCode.value.trim() || 'eligible',
    note: reviewNote.value.trim() || null,
    returnRowVersion: data.value.return.rowVersion,
    // 需要寄回檢查時完全省略這兩欄，不送 null——即使管理員曾經取消勾選、填過值，
    // 重新勾選後這裡讀的是目前的勾選狀態，不會把隱藏欄位的殘留值一起送出去
    // （alex #109 裁定第 2 點）。
    ...(reviewNeedsTrustedFields.value
      ? {
          assemblyFeeDisposition: reviewAssemblyFeeDisposition.value as AssemblyFeeDisposition,
          returnShippingCost: reviewReturnShippingCost.value.trim(),
        }
      : {}),
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
  if (!data.value || !canSubmitInspect.value) {
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
    // 檢查完成一定會建立 Refund，這兩欄固定必填（alex #109 裁定第 4 點）。
    assemblyFeeDisposition: inspectAssemblyFeeDisposition.value as AssemblyFeeDisposition,
    returnShippingCost: inspectReturnShippingCost.value.trim(),
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
            <span v-if="item.description">｜品項說明 {{ item.description }}</span>
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
            maxlength="64"
          >
        </label>
        <label>
          <span>備註</span>
          <textarea
            v-model="reviewNote"
            rows="2"
            maxlength="500"
          />
        </label>
        <label class="admin-return-detail__checkbox-row">
          <input
            v-model="requiresShipmentInspection"
            type="checkbox"
          >
          <span>需要寄回檢查</span>
        </label>
        <p class="admin-return-detail__hint">
          <template v-if="requiresShipmentInspection">
            核准後將要求顧客寄回商品，待收貨與檢查完成後才會進入等待退款。
          </template>
          <template v-else>
            核准後將跳過實體寄回與商品檢查，直接進入等待退款（適用於無需寄回的核准，例如商譽退款）。
          </template>
        </p>
        <template v-if="reviewNeedsTrustedFields">
          <label>
            <span>組裝費處置</span>
            <select
              v-model="reviewAssemblyFeeDisposition"
              name="reviewAssemblyFeeDisposition"
            >
              <option
                value=""
                disabled
              >
                請選擇
              </option>
              <option
                v-for="option in assemblyFeeDispositionOptions"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </option>
            </select>
          </label>
          <label>
            <span>退貨運費</span>
            <input
              v-model="reviewReturnShippingCost"
              name="reviewReturnShippingCost"
              type="text"
              inputmode="decimal"
              placeholder="0.00"
            >
          </label>
        </template>
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
            :disabled="reviewMutation.isPending.value || !canSubmitApprove"
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
            maxlength="500"
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
            <select v-model="ensureInspectionLine(item.publicId).disposition">
              <option value="resellable">
                可轉售 Resellable
              </option>
              <option value="quarantine">
                隔離 Quarantine
              </option>
              <option value="scrap">
                報廢 Scrap
              </option>
            </select>
          </label>
        </div>
        <label>
          <span>組裝費處置</span>
          <select
            v-model="inspectAssemblyFeeDisposition"
            name="inspectAssemblyFeeDisposition"
          >
            <option
              value=""
              disabled
            >
              請選擇
            </option>
            <option
              v-for="option in assemblyFeeDispositionOptions"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
        </label>
        <label>
          <span>退貨運費</span>
          <input
            v-model="inspectReturnShippingCost"
            name="inspectReturnShippingCost"
            type="text"
            inputmode="decimal"
            placeholder="0.00"
          >
        </label>
        <p
          v-if="isConflict(inspectMutation.error.value)"
          class="admin-return-detail__conflict"
          role="alert"
        >
          此案件已被其他人更新，請重新整理後再操作。
        </p>
        <button
          type="button"
          :disabled="inspectMutation.isPending.value || !canSubmitInspect"
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
            maxlength="64"
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
  border: 1px solid var(--color-border);
  border-radius: 0.75rem;
}

.admin-return-detail__summary dt {
  font-size: 0.75rem;
  color: var(--color-text-muted);
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
  border: 1px solid var(--color-border);
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
  border: 1px solid var(--color-border);
  border-radius: 0.375rem;
}

.admin-return-detail__checkbox-row {
  flex-direction: row !important;
  align-items: center;
  gap: 0.5rem !important;
}

.admin-return-detail__checkbox-row input {
  padding: 0 !important;
  border: none !important;
  width: auto;
}

.admin-return-detail__inspect-row {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 1rem;
  padding-block: 0.5rem;
  border-bottom: 1px solid var(--color-border);
}

.admin-return-detail__actions {
  display: flex;
  gap: 0.75rem;
}

.admin-return-detail__conflict {
  color: var(--color-danger);
}

.admin-return-detail__hint {
  color: var(--color-text-muted);
  font-size: 0.875rem;
}

.admin-return-detail__description {
  white-space: pre-wrap;
  color: var(--color-text);
}
</style>

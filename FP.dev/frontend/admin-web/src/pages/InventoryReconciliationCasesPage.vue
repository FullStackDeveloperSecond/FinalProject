<script setup lang="ts">
/**
 * A-29 (M功能桌面UI與Route規格.md): 對帳案件列表、確認受理、駁回與修正庫存（UC-ADM-INV-01 對帳）。
 * 後端契約是組長 PR #100 的對帳裁定 A1～H1（PR #107）：acknowledge 只帶 RowVersion；dismiss／resolve
 * 共用 `{ reasonCode, note, rowVersion }`，reasonCode 依動作各有白名單，note 必填。
 */
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive } from 'vue'
import type { ReconciliationCaseCloseAction } from '../features/inventory/api'
import {
  useAcknowledgeReconciliationCase,
  useCloseReconciliationCase,
  useInventoryReconciliationCaseList,
} from '../features/inventory/useInventory'
import type { InventoryReconciliationCaseDto } from '../features/inventory/types'
import { describeApiError } from '../features/shared/errorMessages'

// 白名單鏡射後端 InventoryReconciliationReasonCodes.ForDismiss／ForResolve（組長裁定 D1）：
// `false_positive` 只能駁回（差異本來就不存在，沒有東西可修），`count_verified` 只能修正
// （已實點確認帳本才是對的，不能拿來把差異藏掉）。送錯組合後端一樣回 400，這裡只是不給錯的選項。
const DISMISS_REASON_OPTIONS = [
  { value: 'false_positive', label: '核對基準錯誤（誤報）' },
  { value: 'system_error', label: '系統錯誤' },
  { value: 'other', label: '其他' },
]
const RESOLVE_REASON_OPTIONS = [
  { value: 'count_verified', label: '實點確認' },
  { value: 'system_error', label: '系統錯誤' },
  { value: 'other', label: '其他' },
]

const STATUS_OPTIONS = ['Open', 'Acknowledged', 'Resolved', 'Dismissed']

// 與 A-11／A-12 相同的草稿→套用模式：只有按下搜尋才換 query key，並把頁碼重設為 1。
const draftFilters = reactive({ status: '' })
const appliedFilters = reactive({ status: '', pageNumber: 1 })
const pageSize = 20
const listParams = computed(() => ({
  status: appliedFilters.status || undefined,
  pageNumber: appliedFilters.pageNumber,
  pageSize,
}))
const { data, isPending, isError, error, refetch } = useInventoryReconciliationCaseList(listParams)
const items = computed<InventoryReconciliationCaseDto[]>(() => data.value?.items ?? [])
const totalPages = computed(() => Number(data.value?.totalPages ?? 0))

function search() {
  appliedFilters.status = draftFilters.status
  appliedFilters.pageNumber = 1
}

function goToPage(nextPage: number) {
  appliedFilters.pageNumber = nextPage
}

// 後端沒有回 availableActions（案件 DTO 是 #36 定的），可執行的動作由狀態決定，規則與
// 服務層一致：acknowledge 只認 Open；dismiss／resolve 認 Open 或 Acknowledged（裁定 C1）。
function canAcknowledge(reconciliationCase: InventoryReconciliationCaseDto): boolean {
  return reconciliationCase.status === 'Open'
}

function canClose(reconciliationCase: InventoryReconciliationCaseDto): boolean {
  return reconciliationCase.status === 'Open' || reconciliationCase.status === 'Acknowledged'
}

const acknowledgeMutation = useAcknowledgeReconciliationCase()
const closeMutation = useCloseReconciliationCase()

function acknowledge(reconciliationCase: InventoryReconciliationCaseDto) {
  closeForm.publicId = null
  acknowledgeMutation.mutate({ publicId: reconciliationCase.publicId, rowVersion: reconciliationCase.rowVersion })
}

// 同一時間只開一張結案表單；表單記住它是為哪個案件、哪個動作開的，送出時用該列當下的 RowVersion。
const closeForm = reactive({
  publicId: null as string | null,
  action: 'dismiss' as ReconciliationCaseCloseAction,
  reasonCode: '',
  note: '',
})
const closeReasonOptions = computed(() =>
  closeForm.action === 'dismiss' ? DISMISS_REASON_OPTIONS : RESOLVE_REASON_OPTIONS)
const closeActionLabel = computed(() => (closeForm.action === 'dismiss' ? '駁回' : '修正庫存'))

function startClose(reconciliationCase: InventoryReconciliationCaseDto, action: ReconciliationCaseCloseAction) {
  closeMutation.reset()
  closeForm.publicId = reconciliationCase.publicId
  closeForm.action = action
  closeForm.reasonCode = ''
  closeForm.note = ''
}

function cancelClose() {
  closeForm.publicId = null
}

function confirmClose(reconciliationCase: InventoryReconciliationCaseDto) {
  const message = closeForm.action === 'dismiss'
    ? `確定要駁回 SKU ${reconciliationCase.sku.skuCode} 的對帳案件嗎？案件會以「核對基準錯誤」結案，庫存不會變動。`
    : `確定要修正 SKU ${reconciliationCase.sku.skuCode} 的庫存嗎？在庫 ${Number(reconciliationCase.expectedOnHand)} → ${Number(reconciliationCase.actualOnHand)}、保留 ${Number(reconciliationCase.expectedReserved)} → ${Number(reconciliationCase.actualReserved)}，並留下一筆修正異動。此動作無法復原。`
  if (!globalThis.confirm(message)) {
    return
  }
  closeMutation.mutate({
    publicId: reconciliationCase.publicId,
    action: closeForm.action,
    request: {
      reasonCode: closeForm.reasonCode,
      note: closeForm.note,
      rowVersion: reconciliationCase.rowVersion,
    },
  }, {
    onSuccess: () => {
      closeForm.publicId = null
    },
  })
}

function formatDateTime(value: string | null): string {
  return value ? new Date(value).toLocaleString('zh-Hant-TW') : '—'
}

function formatQuantities(expected: number | string, actual: number | string): string {
  return Number(expected) === Number(actual) ? `${Number(expected)}` : `${Number(expected)} → ${Number(actual)}`
}

function actorLabel(reconciliationCase: InventoryReconciliationCaseDto): string {
  const actor = reconciliationCase.resolvedBy ?? reconciliationCase.acknowledgedBy
  return actor?.email ?? '—'
}
</script>

<template>
  <section aria-labelledby="inventory-reconciliation-title">
    <h1 id="inventory-reconciliation-title">
      庫存對帳案件
    </h1>
    <p class="reconciliation-intro">
      每日對帳把 Balance 與 Movement／Reservation 帳本重算值比對，不一致的 SKU 開成案件。「駁回」表示核對基準錯誤、庫存不變；
      「修正庫存」把 Balance 改成帳本重算值並留下一筆修正異動。兩者都會寫入中央稽核。
    </p>

    <form
      class="reconciliation-filters"
      aria-label="對帳篩選"
      @submit.prevent="search"
    >
      <select
        v-model="draftFilters.status"
        aria-label="狀態"
      >
        <option value="">
          全部狀態
        </option>
        <option
          v-for="status in STATUS_OPTIONS"
          :key="status"
          :value="status"
        >
          {{ status }}
        </option>
      </select>
      <button type="submit">
        搜尋
      </button>
    </form>

    <LoadingState
      v-if="isPending"
      label="對帳案件載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />
    <EmptyState
      v-else-if="items.length === 0"
      title="沒有符合條件的對帳案件"
    />
    <template v-else>
      <p
        v-if="isApiError(acknowledgeMutation.error.value)"
        class="reconciliation-error"
        role="alert"
      >
        {{ describeApiError(acknowledgeMutation.error.value) }}
      </p>
      <table class="reconciliation-table">
        <thead>
          <tr>
            <th>SKU</th>
            <th>狀態</th>
            <th>在庫（Balance → 帳本）</th>
            <th>保留（Balance → 帳本）</th>
            <th>偵測時間</th>
            <th>處理人</th>
            <th>結案說明</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <template
            v-for="reconciliationCase in items"
            :key="reconciliationCase.publicId"
          >
            <tr :data-case-id="reconciliationCase.publicId">
              <td>
                {{ reconciliationCase.sku.skuCode }}
                <small class="reconciliation-table__sku-name">{{ reconciliationCase.sku.nameZhTw }}</small>
              </td>
              <td>{{ reconciliationCase.status }}</td>
              <td>{{ formatQuantities(reconciliationCase.expectedOnHand, reconciliationCase.actualOnHand) }}</td>
              <td>{{ formatQuantities(reconciliationCase.expectedReserved, reconciliationCase.actualReserved) }}</td>
              <td>{{ formatDateTime(reconciliationCase.detectedAtUtc) }}</td>
              <td>{{ actorLabel(reconciliationCase) }}</td>
              <td>{{ reconciliationCase.resolutionReason ?? '—' }}</td>
              <td class="reconciliation-table__actions">
                <button
                  v-if="canAcknowledge(reconciliationCase)"
                  type="button"
                  :disabled="acknowledgeMutation.isPending.value"
                  @click="acknowledge(reconciliationCase)"
                >
                  確認受理
                </button>
                <template v-if="canClose(reconciliationCase) && closeForm.publicId !== reconciliationCase.publicId">
                  <button
                    type="button"
                    @click="startClose(reconciliationCase, 'dismiss')"
                  >
                    駁回
                  </button>
                  <button
                    type="button"
                    @click="startClose(reconciliationCase, 'resolve')"
                  >
                    修正庫存
                  </button>
                </template>
              </td>
            </tr>
            <tr
              v-if="closeForm.publicId === reconciliationCase.publicId"
              class="reconciliation-table__close-row"
            >
              <td colspan="8">
                <div class="close-form">
                  <span class="close-form__title">{{ closeActionLabel }}</span>
                  <label>
                    原因代碼
                    <select
                      v-model="closeForm.reasonCode"
                      required
                      aria-label="原因代碼"
                    >
                      <option
                        value=""
                        disabled
                      >
                        請選擇原因
                      </option>
                      <option
                        v-for="option in closeReasonOptions"
                        :key="option.value"
                        :value="option.value"
                      >
                        {{ option.label }}
                      </option>
                    </select>
                  </label>
                  <label>
                    說明
                    <input
                      v-model="closeForm.note"
                      maxlength="500"
                      required
                      aria-label="說明"
                    >
                  </label>
                  <button
                    type="button"
                    :disabled="closeMutation.isPending.value || !closeForm.reasonCode || !closeForm.note.trim()"
                    @click="confirmClose(reconciliationCase)"
                  >
                    確認{{ closeActionLabel }}
                  </button>
                  <button
                    type="button"
                    @click="cancelClose"
                  >
                    取消
                  </button>
                </div>
                <p
                  v-if="isApiError(closeMutation.error.value)"
                  class="close-form__error"
                  role="alert"
                >
                  {{ describeApiError(closeMutation.error.value) }}
                </p>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
      <div
        v-if="totalPages > 1"
        class="reconciliation-pagination"
      >
        <button
          type="button"
          :disabled="appliedFilters.pageNumber <= 1"
          @click="goToPage(appliedFilters.pageNumber - 1)"
        >
          上一頁
        </button>
        <span>第 {{ appliedFilters.pageNumber }} / {{ totalPages }} 頁</span>
        <button
          type="button"
          :disabled="appliedFilters.pageNumber >= totalPages"
          @click="goToPage(appliedFilters.pageNumber + 1)"
        >
          下一頁
        </button>
      </div>
    </template>
  </section>
</template>

<style scoped>
.reconciliation-intro {
  color: #4b5563;
  font-size: 0.875rem;
  margin-block-end: 1rem;
}

.reconciliation-filters {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-block-end: 1.5rem;
}

.reconciliation-filters select,
.close-form select,
.close-form input {
  min-height: 2.5rem;
  padding: 0.375rem 0.625rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.reconciliation-table {
  width: 100%;
  border-collapse: collapse;
}

.reconciliation-table th,
.reconciliation-table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
  vertical-align: top;
}

.reconciliation-table__sku-name {
  display: block;
  color: #6b7280;
}

.reconciliation-table__actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.reconciliation-table__close-row {
  background: #f9fafb;
}

.close-form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 0.75rem;
}

.close-form__title {
  font-weight: 600;
}

.close-form label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
}

.close-form__error,
.reconciliation-error {
  color: #b91c1c;
  font-size: 0.875rem;
  margin: 0.5rem 0;
}

.reconciliation-pagination {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 1rem;
  margin-block-start: 1.5rem;
}
</style>

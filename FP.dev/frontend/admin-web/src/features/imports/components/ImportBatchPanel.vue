<script setup lang="ts">
/**
 * A-07 與 A-13 共用的批次面板：統計、狀態、逐列預覽、錯誤 CSV 下載與原子確認。
 *
 * 兩頁只有「上傳什麼」不同——商品是三個資料集、庫存是一個——所以上傳表單留在各自的頁面，
 * 上傳之後的一切都在這裡。共用的是同一段程式而不是兩份長得像的程式：確認按鈕什麼時候該亮、
 * 錯誤怎麼呈現這種規則，分成兩份遲早會分岔。
 */
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref, watch } from 'vue'
import { describeApiError } from '../../shared/errorMessages'
import { useConfirmImport, useDownloadImportErrors, useImportBatch, useImportRows, type ImportKind } from '../useImports'

const props = defineProps<{
  kind: ImportKind
  batchId: string | null
}>()

const emit = defineEmits<{ committed: [] }>()

const errorsOnly = ref(false)
const cursor = ref<string | undefined>(undefined)

const { data: batch, isPending: isBatchPending, isError: isBatchError, error: batchError, refetch: refetchBatch }
  = useImportBatch(props.kind, () => props.batchId)

const rowsParams = computed(() => ({ errorsOnly: errorsOnly.value, cursor: cursor.value, pageSize: 50 }))
const { data: rows, isPending: isRowsPending, isError: isRowsError, error: rowsError, refetch: refetchRows }
  = useImportRows(props.kind, () => props.batchId, rowsParams)

// 換批次或換篩選就回到第一頁；沿用舊游標會拿到一份對不上目前條件的資料。
watch(() => [props.batchId, errorsOnly.value], () => { cursor.value = undefined })

const confirmMutation = useConfirmImport(props.kind)
const downloadErrors = useDownloadImportErrors(props.kind)
const actionError = ref<string | null>(null)
const committedMessage = ref<string | null>(null)

// 產生的契約型別把整數欄位標成 `string | number`（大整數相容），所以比較前先轉成數字，
// 不要用會把 '0' 與 0 混為一談的寬鬆比較。
const errorCount = computed(() => Number(batch.value?.errorCount ?? 0))

/**
 * 只有 Ready 才能確認。
 *
 * 這裡刻意「不」再加一條 `errorCount === 0`：ImportBatch.SetPreviewStatistics 就是用
 * `errorCount == 0 ? Ready : Invalid` 決定狀態的，所以 Ready 必然沒有錯誤列——多寫一條永遠成立的
 * 條件只會讓人以為它擋得住什麼。反向驗證時就是拿它開刀才發現：拿掉它測試照樣是綠的，因為狀態那
 * 條已經擋住了。錯誤列的說明文字仍然依 errorCount 顯示，那是 Invalid 批次真的會走到的路徑。
 */
const canConfirm = computed(() =>
  batch.value?.status === 'Ready'
  && !confirmMutation.isPending.value)

const isCommitted = computed(() => batch.value?.status === 'Committed')

async function confirm() {
  if (!props.batchId || !batch.value || !canConfirm.value) return
  actionError.value = null
  try {
    const result = await confirmMutation.mutateAsync({
      batchId: props.batchId,
      rowVersion: batch.value.rowVersion as unknown as string,
    })
    committedMessage.value = `已套用 ${result.rowCount} 列。`
    emit('committed')
  }
  catch (caught) {
    actionError.value = confirmErrorMessage(caught)
  }
}

/**
 * 匯入是整批單一交易，所以每一種失敗都要講清楚「什麼都沒有寫入」——否則管理員無從判斷該不該
 * 重做，只能自己去翻資料。
 */
function confirmErrorMessage(caught: unknown): string {
  const code = isApiError(caught) ? caught.code : undefined
  switch (code) {
    case 'import_already_committed':
      return '這個批次已經提交過了，沒有重複套用。'
    case 'import_batch_expired':
      return '這個批次已超過 24 小時效期，整批未套用。請重新上傳。'
    case 'concurrency_conflict':
      return '預覽之後資料已被其他人修改，整批未套用。請重新上傳以重新預覽。'
    case 'inventory_import_validation_failed':
    case 'import_validation_failed':
      return '批次驗證未通過，整批未套用。請下載錯誤檔修正後重新上傳。'
    default:
      return isApiError(caught) ? describeApiError(caught) : '提交失敗，整批未套用。'
  }
}

async function download() {
  if (!props.batchId) return
  actionError.value = null
  try {
    await downloadErrors.mutateAsync(props.batchId)
  }
  catch {
    actionError.value = '錯誤檔下載失敗，請稍後再試。'
  }
}

function formatStatus(status: string): string {
  return {
    Uploaded: '已上傳',
    Validating: '驗證中',
    Ready: '可提交',
    Invalid: '有錯誤',
    Committing: '提交中',
    Committed: '已提交',
    Failed: '失敗',
    Expired: '已逾期',
  }[status] ?? status
}

function formatAction(action: string): string {
  return { Insert: '新增', Update: '更新', NoChange: '無變更', Error: '錯誤' }[action] ?? action
}
</script>

<template>
  <section
    v-if="batchId"
    class="import-panel"
    aria-labelledby="import-batch-title"
  >
    <h2 id="import-batch-title">
      預覽結果
    </h2>

    <LoadingState
      v-if="isBatchPending"
      label="批次載入中"
    />
    <ErrorState
      v-else-if="isBatchError"
      :correlation-id="isApiError(batchError) ? batchError.correlationId : undefined"
      @retry="refetchBatch"
    />
    <template v-else-if="batch">
      <dl class="import-panel__stats">
        <div>
          <dt>狀態</dt>
          <dd>{{ formatStatus(batch.status) }}</dd>
        </div>
        <div>
          <dt>總列數</dt>
          <dd>{{ batch.rowCount }}</dd>
        </div>
        <div>
          <dt>新增</dt>
          <dd>{{ batch.newCount }}</dd>
        </div>
        <div>
          <dt>更新</dt>
          <dd>{{ batch.updatedCount }}</dd>
        </div>
        <div>
          <dt>無變更</dt>
          <dd>{{ batch.unchangedCount }}</dd>
        </div>
        <div>
          <dt>錯誤</dt>
          <dd :class="{ 'import-panel__errors': errorCount > 0 }">
            {{ batch.errorCount }}
          </dd>
        </div>
      </dl>

      <div
        class="import-panel__actions"
        role="group"
        aria-label="批次動作"
      >
        <label>
          <input
            v-model="errorsOnly"
            type="checkbox"
            aria-label="只顯示錯誤列"
          >
          只顯示錯誤列
        </label>
        <button
          type="button"
          :disabled="downloadErrors.isPending.value"
          @click="download"
        >
          下載錯誤 CSV
        </button>
        <button
          type="button"
          :disabled="!canConfirm"
          @click="confirm"
        >
          確認匯入
        </button>
        <p
          v-if="errorCount > 0"
          class="import-panel__hint"
        >
          有 {{ errorCount }} 列錯誤，整批不會套用。請下載錯誤檔修正後重新上傳。
        </p>
        <p
          v-else-if="isCommitted"
          class="import-panel__hint"
        >
          這個批次已提交，不會再套用第二次。
        </p>
      </div>

      <p
        v-if="committedMessage"
        class="import-panel__message"
        role="status"
      >
        {{ committedMessage }}
      </p>
      <p
        v-if="actionError"
        class="import-panel__error"
        role="alert"
      >
        {{ actionError }}
      </p>

      <LoadingState
        v-if="isRowsPending"
        label="預覽列載入中"
      />
      <ErrorState
        v-else-if="isRowsError"
        :correlation-id="isApiError(rowsError) ? rowsError.correlationId : undefined"
        @retry="refetchRows"
      />
      <EmptyState
        v-else-if="(rows?.items.length ?? 0) === 0"
        title="沒有可顯示的預覽列"
        description="調整篩選條件，或重新上傳檔案。"
      />
      <template v-else-if="rows">
        <table class="import-panel__rows">
          <thead>
            <tr>
              <th>資料集</th>
              <th>來源列</th>
              <th>鍵值</th>
              <th>動作</th>
              <th>錯誤碼</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in rows.items"
              :key="`${row.dataset}-${row.sourceRowNumber}`"
              :class="{ 'import-panel__row--error': row.action === 'Error' }"
            >
              <td>{{ row.dataset }}</td>
              <td>{{ row.sourceRowNumber }}</td>
              <td>{{ row.importKey }}</td>
              <td>{{ formatAction(row.action) }}</td>
              <td>{{ row.errorCodes.join('、') }}</td>
            </tr>
          </tbody>
        </table>
        <button
          v-if="rows.hasMore"
          type="button"
          @click="cursor = rows.nextCursor ?? undefined"
        >
          載入更多
        </button>
      </template>
    </template>
  </section>
</template>

<style scoped>
.import-panel {
  margin-block-start: 2rem;
}

.import-panel__stats {
  display: flex;
  flex-wrap: wrap;
  gap: 1.5rem;
  margin-block: 1rem;
}

.import-panel__stats dt {
  color: #6b7280;
  font-size: 0.8125rem;
}

.import-panel__stats dd {
  margin: 0;
  font-size: 1.125rem;
}

.import-panel__errors {
  color: #b91c1c;
  font-weight: 600;
}

.import-panel__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  margin-block: 1rem;
}

.import-panel__hint {
  flex-basis: 100%;
  margin: 0;
  color: #6b7280;
  font-size: 0.8125rem;
}

.import-panel__message {
  color: #047857;
}

.import-panel__error {
  color: #b91c1c;
}

.import-panel__rows {
  width: 100%;
  border-collapse: collapse;
}

.import-panel__rows th,
.import-panel__rows td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.import-panel__row--error {
  background: #fef2f2;
}
</style>

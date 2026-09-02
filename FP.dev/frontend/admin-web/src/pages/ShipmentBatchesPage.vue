<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { EmptyState, ErrorState, HttpStatusPage } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { describeApiError } from '../features/shared/errorMessages'
import {
  BATCH_SHIPMENT_ACTIONS,
  MAX_BATCH_SHIPMENT_ORDERS,
  type BatchShipmentAction,
  type BatchShipmentItemResultDto,
  type BatchShipmentResultDto,
} from '../features/shipping/types'
import {
  batchShipmentSelection,
  clearBatchShipmentSelection,
} from '../features/shipping/batchSelection'
import { useShipBatch } from '../features/shipping/useShipping'

const shippingAction = ref<BatchShipmentAction>('createLabel')
const result = ref<BatchShipmentResultDto | null>(null)

/**
 * 冪等鍵在第一次送出時產生，之後只要選取沒變就沿用同一把——網路中斷後重按「送出」時，帶的是
 * 同一個鍵而不是新的一批。送出成功後才換新的，下一批才會是新的批次。
 */
const idempotencyKey = ref(crypto.randomUUID())

const { mutateAsync, isPending, isError, error, reset } = useShipBatch()
const apiError = computed(() => (isApiError(error.value) ? error.value : undefined))
const submitError = ref<string | null>(null)

const selection = computed(() => batchShipmentSelection.value)
const canSubmit = computed(() =>
  selection.value.length > 0 && selection.value.length <= MAX_BATCH_SHIPMENT_ORDERS && !isPending.value)

const actionLabel = computed(() =>
  BATCH_SHIPMENT_ACTIONS.find(option => option.value === shippingAction.value)?.label ?? shippingAction.value)

/**
 * 已經印過物流單的訂單（履約狀態 Preparing）只能用 markShipped 接續完成，再 createLabel 一次
 * 一定失敗。與其讓管理員送出去才收到一整批錯誤碼，不如在按鈕旁邊先講。
 */
const alreadyPreparedCount = computed(() =>
  selection.value.filter(candidate => candidate.fulfillmentStatus !== 'Pending').length)
const warnsAboutPreparedOrders = computed(() =>
  shippingAction.value === 'createLabel' && alreadyPreparedCount.value > 0)

async function submit(): Promise<void> {
  if (!canSubmit.value) {
    return
  }

  submitError.value = null
  let response: BatchShipmentResultDto
  try {
    response = await mutateAsync({
      orders: selection.value.map(candidate => ({
        orderPublicId: candidate.publicId,
        rowVersion: candidate.rowVersion,
      })),
      shippingAction: shippingAction.value,
      idempotencyKey: idempotencyKey.value,
    })
  }
  catch (caught) {
    // 這裡必須接住，否則 mutateAsync 的 rejection 會變成未處理的 Promise 錯誤。
    // 冪等鍵刻意不換：重試要是同一批，伺服器才認得出這是同一次送出。
    submitError.value = submitErrorMessage(caught)
    return
  }

  result.value = response
  idempotencyKey.value = crypto.randomUUID()

  // 送出後清掉選取：每一筆訂單的 RowVersion 都已經被這次出貨推進，留著只會讓管理員拿過期的
  // 版本再送一次，然後整批得到 concurrency_conflict。要再出一批就回列表重新勾。
  clearBatchShipmentSelection()
}

/**
 * 冪等衝突要講清楚「貨有沒有出」，否則管理員只會一直重按。
 */
function submitErrorMessage(caught: unknown): string {
  const code = isApiError(caught) ? caught.code : undefined
  switch (code) {
    case 'idempotency_payload_conflict':
      return '這批的識別碼已經用在另一份訂單清單上了。請回訂單管理重新勾選，系統會給新的識別碼。'
    case 'idempotency_request_in_progress':
      return '同一批出貨還在處理中，或上一次送出沒有完整結束。請稍候再查看訂單狀態；若要補送剩下的訂單，請回列表重新勾選以取得新的識別碼。'
    default:
      return isApiError(caught) ? describeApiError(caught) : '送出失敗，請稍後再試。'
  }
}

function rowStatusLabel(item: BatchShipmentItemResultDto): string {
  return item.errorCode ? `失敗（${item.errorCode}）` : item.status
}

function csvCell(value: string | number | null | undefined): string {
  const text = value === null || value === undefined ? '' : String(value)
  return `"${text.replaceAll('"', '""')}"`
}

/**
 * 逐筆結果的 CSV 由這份同步回應就地產生。`GET .../batches/{id}/result.csv` 需要一張批次表才
 * 能重新下載，而最終 Schema 沒有那張表，所以先讓管理員在這一頁把結果留下來。
 */
function downloadCsv(): void {
  if (!result.value) {
    return
  }

  const header = ['列號', '訂單編號', '訂單識別碼', '狀態', '物流單號', '錯誤碼', '訊息']
  const rows = result.value.items.map(item => [
    item.sourceRowNumber,
    item.orderNumber,
    item.orderPublicId,
    item.status,
    item.trackingNumber,
    item.errorCode,
    item.message,
  ])

  // 開頭的 BOM 是給 Excel 的：沒有它 Excel 會用系統編碼開檔，中文欄位直接變亂碼。
  const csv = `\uFEFF${[header, ...rows].map(row => row.map(csvCell).join(',')).join('\r\n')}\r\n`
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `batch-shipment-${result.value.batchPublicId}.csv`
  link.click()
  URL.revokeObjectURL(url)
}

function startOver(): void {
  result.value = null
  submitError.value = null
  reset()
}
</script>

<template>
  <section aria-labelledby="page-title">
    <h1 id="page-title">
      批次出貨
    </h1>

    <HttpStatusPage
      v-if="apiError?.status === 401"
      :status="401"
      home-href="/"
    />

    <HttpStatusPage
      v-else-if="apiError?.status === 403"
      :status="403"
      home-href="/"
    />

    <template v-else>
      <template v-if="result">
        <h2>批次結果</h2>
        <p
          v-if="result.isReplay"
          class="batches__replay"
          role="status"
        >
          這是先前那一次送出的結果，沒有任何訂單被重複出貨。
        </p>
        <p>
          共 {{ result.total }} 筆，成功 {{ result.succeeded }} 筆、失敗 {{ result.failed }} 筆。
        </p>
        <p>
          <button
            type="button"
            @click="downloadCsv"
          >
            下載結果 CSV
          </button>
          <button
            type="button"
            @click="startOver"
          >
            開始新的批次
          </button>
        </p>

        <table>
          <caption>逐筆結果</caption>
          <thead>
            <tr>
              <th scope="col">
                列號
              </th>
              <th scope="col">
                訂單編號
              </th>
              <th scope="col">
                狀態
              </th>
              <th scope="col">
                物流單號
              </th>
              <th scope="col">
                訊息
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="item in result.items"
              :key="item.sourceRowNumber"
            >
              <td>{{ item.sourceRowNumber }}</td>
              <td>{{ item.orderNumber ?? item.orderPublicId }}</td>
              <td>{{ rowStatusLabel(item) }}</td>
              <td>{{ item.trackingNumber ?? '—' }}</td>
              <td>{{ item.message ?? '—' }}</td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <EmptyState
          v-if="selection.length === 0"
          title="尚未選取訂單"
          description="到訂單管理勾選待出貨的訂單，再回到這一頁送出批次。"
        />

        <template v-else>
          <p>已選取 {{ selection.length }} 筆訂單（單批上限 {{ MAX_BATCH_SHIPMENT_ORDERS }} 筆）。</p>

          <fieldset>
            <legend>出貨動作</legend>
            <label
              v-for="option in BATCH_SHIPMENT_ACTIONS"
              :key="option.value"
            >
              <input
                v-model="shippingAction"
                type="radio"
                name="shipping-action"
                :value="option.value"
              >
              {{ option.label }}
            </label>
          </fieldset>

          <table>
            <caption>待送出的訂單</caption>
            <thead>
              <tr>
                <th scope="col">
                  列號
                </th>
                <th scope="col">
                  訂單編號
                </th>
                <th scope="col">
                  履約狀態
                </th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(candidate, index) in selection"
                :key="candidate.publicId"
              >
                <td>{{ index + 1 }}</td>
                <td>{{ candidate.orderNumber }}</td>
                <td>{{ candidate.fulfillmentStatus }}</td>
              </tr>
            </tbody>
          </table>

          <p
            v-if="warnsAboutPreparedOrders"
            class="batches__warning"
            role="alert"
          >
            選取的訂單中有 {{ alreadyPreparedCount }} 筆已經建立過物流單，再建立一次會失敗。
            請改選「標記已出貨」接續完成。
          </p>

          <p
            v-if="submitError"
            class="batches__warning"
            role="alert"
          >
            {{ submitError }}
          </p>
          <ErrorState
            v-else-if="isError"
            :correlation-id="apiError?.correlationId"
            :trace-id="apiError?.traceId"
            @retry="submit"
          />

          <button
            type="button"
            :disabled="!canSubmit"
            @click="submit"
          >
            {{ isPending ? '送出中…' : `送出批次（${actionLabel}）` }}
          </button>
        </template>

        <p>
          <RouterLink to="/orders">
            回訂單管理
          </RouterLink>
        </p>
      </template>
    </template>
  </section>
</template>

<style scoped>
.batches__replay {
  color: #047857;
}

.batches__warning {
  color: #b91c1c;
}
</style>

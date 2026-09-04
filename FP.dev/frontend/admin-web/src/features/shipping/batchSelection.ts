import { computed, reactive } from 'vue'
import { MAX_BATCH_SHIPMENT_ORDERS } from './types'

/**
 * A-14 訂單列表勾選 → A-16 批次出貨頁的交接。
 *
 * 走模組層級的狀態而不是 query string：一批最多 100 筆訂單，把 publicId 與 RowVersion 塞進網址
 * 會超過瀏覽器與伺服器的實際上限，而 RowVersion 是併發仲裁用的資料，不該出現在網址列、瀏覽
 * 紀錄或伺服器日誌裡。代價是重新整理 A-16 會清空選取——這是刻意的：RowVersion 一旦過期，那一筆
 * 本來就該讓管理員回列表重新勾選，而不是拿著舊版本硬送。
 */
export interface BatchShipmentCandidate {
  publicId: string
  orderNumber: string
  rowVersion: string
  summaryStatus: string
  fulfillmentStatus: string
}

const state = reactive({
  candidates: [] as BatchShipmentCandidate[],
})

export const batchShipmentSelection = computed<readonly BatchShipmentCandidate[]>(
  () => state.candidates,
)

export function setBatchShipmentSelection(candidates: readonly BatchShipmentCandidate[]): void {
  state.candidates = candidates.slice(0, MAX_BATCH_SHIPMENT_ORDERS).map(candidate => ({ ...candidate }))
}

export function clearBatchShipmentSelection(): void {
  state.candidates = []
}

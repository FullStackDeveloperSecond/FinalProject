import { assemblyStatusLabel } from '../orders/api'
import type { BatchShipmentItemResultDto } from './types'

export function batchResultMessage(item: BatchShipmentItemResultDto): string {
  if (!item.errorCode) return '處理完成。'
  const assembly = /^The order's assembly is (\w+)\.$/.exec(item.message ?? '')
  if (assembly) return `組裝狀態為「${assemblyStatusLabel[assembly[1]!] ?? '尚未完成'}」。請至訂單詳情更新組裝進度，完成測試並確認可出貨後再試。`
  if (item.message?.includes('already has a shipment')) return '已建立物流單，請改用「標記已出貨」接續處理。'
  if (item.message?.includes('not paid')) return '非貨到付款訂單尚未付款，請先確認付款結果。'
  if (item.message && /[\u3400-\u9fff]/.test(item.message)) return item.message
  switch (item.errorCode) {
    case 'concurrency_conflict': return '訂單已更新，請回訂單管理重新選取後再試。'
    case 'shipping_order_not_ready': return '訂單尚不符合出貨條件，請查看付款、組裝、物流單及庫存保留狀態。'
    case 'shipping_method_not_allowed': return '配送方式不可用，請檢查訂單的配送設定。'
    case 'resource_not_found': return '找不到訂單，請重新整理列表。'
    default: return '本筆未完成，請重新檢查訂單；若持續失敗，請將結果提供給管理員。'
  }
}

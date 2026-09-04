import type { components } from '@doselect/web-shared/api'

export type ConvenienceStoreDto = components['schemas']['ConvenienceStoreDto']
export type CreateConvenienceStoreRequest = components['schemas']['CreateConvenienceStoreRequest']
export type UpdateConvenienceStoreRequest = components['schemas']['UpdateConvenienceStoreRequest']

export type BatchShipmentRequest = components['schemas']['AdminBatchShipmentRequest']
export type BatchShipmentOrderInput = components['schemas']['AdminBatchShipmentOrder']
export type BatchShipmentResultDto = components['schemas']['BatchShipmentResultDto']
export type BatchShipmentItemResultDto = components['schemas']['BatchShipmentItemResultDto']

/**
 * UC-ADM-SHIP-02 的兩個動作。`createLabel` 只建立物流單、把訂單推到準備出貨；`markShipped`
 * 才把保留庫存轉為已消耗並扣掉實體庫存。差別就是「貨有沒有真的離開倉庫」，所以送出前要讓
 * 管理員看清楚自己選的是哪一個。
 */
export const BATCH_SHIPMENT_ACTIONS = [
  { value: 'createLabel', label: '建立物流單（不扣庫存）' },
  { value: 'markShipped', label: '標記已出貨（扣庫存）' },
] as const
export type BatchShipmentAction = (typeof BATCH_SHIPMENT_ACTIONS)[number]['value']

/** 後端 MaxBatchSize 也是 100；超過整批會被退回 shipping_batch_limit_exceeded，所以前台先擋。 */
export const MAX_BATCH_SHIPMENT_ORDERS = 100

export type PackageLimitVersionDto = components['schemas']['PackageLimitVersionDto']
export type CreatePackageLimitVersionRequest = components['schemas']['CreatePackageLimitVersionRequest']
export type PublishPackageLimitVersionRequest = components['schemas']['PublishPackageLimitVersionRequest']

/**
 * 購物車、訂單、付款與物流.md 的包裹限制安全範圍，與後端 Domain 的 PackageLimitSafeRanges 一致
 * （超商 1～45cm／3～105cm／0.1～5kg；宅配 1～150cm／3～150cm／0.1～20kg）。這是「程式固定、
 * 一般管理員不可突破」的界線，後端一定會再驗一次；前台帶著它只是為了在送出前就給提示，不是
 * 把它當成唯一防線。
 */
export interface PackageLimitSafeRange {
  minSideCm: number
  maxSideCm: number
  minTotalCm: number
  maxTotalCm: number
  minWeightKg: number
  maxWeightKg: number
}

export const SHIPPING_PROVIDER_CODES = ['StorePickup', 'HomeDelivery'] as const
export type ShippingProviderCode = (typeof SHIPPING_PROVIDER_CODES)[number]

export const PACKAGE_LIMIT_SAFE_RANGES: Record<ShippingProviderCode, PackageLimitSafeRange> = {
  StorePickup: { minSideCm: 1, maxSideCm: 45, minTotalCm: 3, maxTotalCm: 105, minWeightKg: 0.1, maxWeightKg: 5 },
  HomeDelivery: { minSideCm: 1, maxSideCm: 150, minTotalCm: 3, maxTotalCm: 150, minWeightKg: 0.1, maxWeightKg: 20 },
}

export const PROVIDER_LABELS: Record<ShippingProviderCode, string> = {
  StorePickup: '超商取貨',
  HomeDelivery: '宅配',
}

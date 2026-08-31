import type { components } from '@doselect/web-shared/api'

export type CouponDto = components['schemas']['CouponDto']
export type CouponStatus = components['schemas']['CouponStatus']
export type CouponScopeType = components['schemas']['CouponScopeType']
export type CouponDiscountType = components['schemas']['CouponDiscountType']
export type CreateCouponRequest = components['schemas']['CreateCouponRequest']
export type UpdateCouponRequest = components['schemas']['UpdateCouponRequest']
export type CouponActionRequest = components['schemas']['CouponActionRequest']

/**
 * 管理員能對優惠券做的三個動作。
 *
 * 刻意只有這三個：`Scheduled → Active`（到達開始時間）與 `Exhausted → Active`
 * （名額返還）是排程與名額返還的系統事件，後端對那兩個狀態的 `activate`
 * 一律回 `coupon_state_conflict`（狀態機設計「優惠券狀態」）。
 */
export const couponActions = ['activate', 'pause', 'disable'] as const

export type CouponAction = (typeof couponActions)[number]

import { isAmountDiscount } from './labels'
import type { CouponDiscountType } from './types'

/**
 * 回傳第一個違反折扣規則的中文訊息；全部通過時回 `null`。
 *
 * 逐條對應後端 `AdminCouponRules.RequireValidRule`：
 *
 * - 固定金額與百分比都需要大於 0 的 `discountValue`。
 * - **百分比另外需要大於 0 的 `maximumDiscount`。** 少了它，後端直接回
 *   400 `validation_failed`，而 Domain 的 `HasCompleteDiscountRule` 也會把這張券
 *   判成不可啟用。
 * - 兩種免運不看金額，兩個欄位都不該有值（表單會隱藏並在送出時清成 `null`）。
 *
 * 前端擋下來**不是安全邊界** —— 後端仍會擋。這裡只是不要讓管理員照著介面
 * 正常填完，按下儲存才收到一句英文錯誤。
 */
export function describeDiscountProblem(
  discountType: CouponDiscountType,
  discountValue: number | null,
  maximumDiscount: number | null,
): string | null {
  if (!isAmountDiscount(discountType)) {
    return null
  }

  if (discountValue === null || Number.isNaN(discountValue) || discountValue <= 0) {
    return discountType === 'percentage'
      ? '折扣百分比必須大於 0。'
      : '折扣金額必須大於 0。'
  }

  if (discountType !== 'percentage') {
    return null
  }

  if (maximumDiscount === null || Number.isNaN(maximumDiscount) || maximumDiscount <= 0) {
    return '百分比折扣必須填寫大於 0 的最高折抵。'
  }

  return null
}

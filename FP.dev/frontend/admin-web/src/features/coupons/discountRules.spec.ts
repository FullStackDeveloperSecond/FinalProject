import { describe, expect, it } from 'vitest'
import { describeDiscountProblem } from './discountRules'

describe('describeDiscountProblem', () => {
  it('requires a positive maximum discount for a percentage coupon', () => {
    // 後端 RequireValidRule 對 percentage 明確要求 maximumDiscount > 0。
    expect(describeDiscountProblem('percentage', 0.1, null))
      .toBe('百分比折扣必須填寫大於 0 的最高折抵。')
    expect(describeDiscountProblem('percentage', 0.1, 0))
      .toBe('百分比折扣必須填寫大於 0 的最高折抵。')
  })

  it('accepts a percentage coupon that has one', () => {
    expect(describeDiscountProblem('percentage', 0.1, 500)).toBeNull()
  })

  it('does not require a maximum discount for a fixed-amount coupon', () => {
    expect(describeDiscountProblem('fixedAmount', 300, null)).toBeNull()
  })

  it.each([
    ['fixedAmount' as const, '折扣金額必須大於 0。'],
    ['percentage' as const, '折扣百分比必須大於 0。'],
  ])('requires a positive discount value for %s', (discountType, expected) => {
    expect(describeDiscountProblem(discountType, null, 500)).toBe(expected)
    expect(describeDiscountProblem(discountType, 0, 500)).toBe(expected)
  })

  it.each([
    ['freeShipping' as const],
    ['assemblyFreeShipping' as const],
  ])('asks a free-shipping coupon (%s) for no amounts at all', (discountType) => {
    // 免運券的折抵由運費決定，Domain 的 HasCompleteDiscountRule 對它們一律為 true。
    expect(describeDiscountProblem(discountType, null, null)).toBeNull()
  })

  it('reports the missing discount value before the missing maximum', () => {
    // 兩個都空時先講第一個要填的欄位，不要一次丟兩句話。
    expect(describeDiscountProblem('percentage', null, null))
      .toBe('折扣百分比必須大於 0。')
  })
})

import type { CouponAction, CouponDiscountType, CouponDto, CouponStatus } from './types'

export const statusLabels: Record<CouponStatus, string> = {
  draft: '草稿',
  scheduled: '已排程',
  active: '啟用中',
  paused: '已暫停',
  expired: '已到期',
  exhausted: '名額用盡',
  disabled: '已停用',
}

export const discountTypeLabels: Record<CouponDiscountType, string> = {
  fixedAmount: '固定金額',
  percentage: '百分比',
  freeShipping: '一般免運',
  assemblyFreeShipping: '組裝免運',
}

/**
 * 這個折扣類型會不會用到 `discountValue` 與 `maximumDiscount`。
 *
 * 免運券的折抵金額由運費決定，不是設定出來的：Domain 的
 * `HasCompleteDiscountRule` 對兩種免運一律為 `true`，不看 `DiscountValue`。
 * 所以表單不該問，送出也不該帶。
 */
export function isAmountDiscount(discountType: CouponDiscountType): boolean {
  return discountType === 'fixedAmount' || discountType === 'percentage'
}

export const actionLabels: Record<CouponAction, string> = {
  activate: '啟用',
  pause: '暫停',
  disable: '停用',
}

/**
 * 這個狀態下管理員可以執行哪些動作。
 *
 * 與後端 `EfAdminCouponService.ActivateAsync` 及權威狀態機逐項對應：
 *
 * - `activate` 只接受 `draft` 與 `paused`。`scheduled → active` 是「到達開始時間」、
 *   `exhausted → active` 是「名額返還」，兩者都是**系統事件**，管理員呼叫會被後端
 *   以 `coupon_state_conflict` 拒絕，所以介面上也不提供。
 * - `pause` 只接受 `active`。
 * - `disable` 接受所有非終態；`expired` 與 `disabled` 是終態。
 *
 * 前端隱藏按鈕**不是安全邊界** —— 後端仍會擋。這裡只是不要讓管理員按一個
 * 必然失敗的按鈕。
 */
export function availableActions(status: CouponStatus): CouponAction[] {
  switch (status) {
    case 'draft':
      return ['activate', 'disable']
    case 'paused':
      return ['activate', 'disable']
    case 'active':
      return ['pause', 'disable']
    case 'scheduled':
    case 'exhausted':
      return ['disable']
    case 'expired':
    case 'disabled':
      return []
  }
}

/**
 * 這個狀態還能不能修改規則。
 *
 * Domain 的規則修訂在 `Expired` 與 `Disabled` 兩個終態直接丟
 * `InvalidOperationException`（映射成 409 `coupon_state_conflict`），
 * 管理員會填完整張表單才知道。與 `availableActions` 同一原則：
 * 不提供必然失敗的按鈕。
 *
 * 前端隱藏**不是安全邊界** —— 後端仍會擋。
 */
export function canEditRules(status: CouponStatus): boolean {
  return status !== 'expired' && status !== 'disabled'
}

/**
 * 折扣的顯示字串。
 *
 * 百分比折扣在 Domain 是 **0～1 的比例**（`RequireWellFormedRule` 限制），
 * 不是百分點，所以顯示時要乘 100。
 */
export function describeDiscount(coupon: CouponDto): string {
  // 免運券的 discountValue 是 null。走到下面的 formatMoney 會顯示成「—」或 NT$0，
  // 看起來像一張沒有效果的券。
  if (!isAmountDiscount(coupon.discountType)) {
    return discountTypeLabels[coupon.discountType]
  }

  const value = Number(coupon.discountValue ?? 0)
  if (coupon.discountType === 'percentage') {
    const percent = Math.round(value * 1000) / 10
    const cap = coupon.maximumDiscount === null
      ? ''
      : `，最高折 ${formatMoney(coupon.maximumDiscount)}`
    return `${percent}%${cap}`
  }

  return formatMoney(coupon.discountValue)
}

export function describeUsage(coupon: CouponDto): string {
  const used = Number(coupon.usage.totalRedeemedCount)
  if (coupon.usage.totalUsageLimit === null) {
    return `${used} / 不限`
  }

  return `${used} / ${Number(coupon.usage.totalUsageLimit)}`
}

export function formatMoney(value: number | string | null): string {
  if (value === null) {
    return '—'
  }

  return `NT$${Number(value).toLocaleString('zh-TW')}`
}

/** 只顯示日期部分；後端一律回 UTC ISO 字串。 */
export function formatDate(value: string): string {
  return new Date(value).toLocaleDateString('zh-TW')
}

/**
 * 規則預覽的範圍摘要。
 *
 * 只給數量，不逐筆列名稱：`CouponScopeDto` 只有 `publicId`，要顯示名稱得回頭
 * 查目錄，而列表可以同時展開多張券 —— 預覽不值得那些請求。
 * 排除清單即使在全站範圍下也要顯示，後端允許這個組合。
 */
export function describeScope(coupon: CouponDto): string {
  const scope = coupon.scope
  const parts = [
    scope.scopeType === 'all'
      ? '全站'
      : `指定 ${scope.categoryPublicIds.length} 個分類、${scope.productPublicIds.length} 件商品`,
  ]

  if (scope.excludedProductPublicIds.length > 0) {
    parts.push(`排除 ${scope.excludedProductPublicIds.length} 件商品`)
  }

  return parts.join('，')
}

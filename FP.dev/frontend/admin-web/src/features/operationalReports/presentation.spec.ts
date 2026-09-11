import { describe, expect, it } from 'vitest'
import { formatMetric, metricLabel, unitLabel } from './presentation'

describe('operational report presentation', () => {
  it.each([
    ['paid_amount', '已付款金額'],
    ['quantity', '銷售數量'],
    ['cost_of_goods_sold', '銷貨成本'],
    ['low_stock_count', '低庫存 SKU 數'],
    ['completed_order_count', '已完成訂單數'],
    ['observed_days', '觀測天數'],
  ])('renders the current summary metric %s in Chinese', (metricKey, expected) => {
    expect(metricLabel(metricKey)).toBe(expected)
  })

  it.each([
    ['payment_method_credit_card_share', '信用卡付款占比'],
    ['payment_method_cash_on_delivery_share', '貨到付款占比'],
    ['payment_method_unknown_share', '其他付款方式占比'],
  ])('renders payment method share %s in Chinese', (metricKey, expected) => {
    expect(metricLabel(metricKey)).toBe(expected)
  })

  it('does not expose an unknown internal metric key to administrators', () => {
    expect(metricLabel('future_internal_metric')).toBe('其他指標')
  })

  it('formats the report API unit tokens as localized currency and units', () => {
    expect(formatMetric(1250, 'TWD')).toBe('NT$1,250')
    expect(unitLabel('TWD')).toBe('新臺幣')
    expect(unitLabel('day')).toBe('天')
  })
})

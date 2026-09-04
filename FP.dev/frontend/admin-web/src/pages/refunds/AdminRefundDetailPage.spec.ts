import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'

const mockListRefunds = vi.fn()
const mockGetRefund = vi.fn()
const mockExecuteRefund = vi.fn()
const mockApproveRefund = vi.fn()

vi.mock('../../features/refunds/api', () => ({
  listRefunds: mockListRefunds,
  getRefund: mockGetRefund,
  executeRefund: mockExecuteRefund,
  approveRefund: mockApproveRefund,
}))

const { default: AdminRefundDetailPage } = await import('./AdminRefundDetailPage.vue')

const refundId = '018f2e6a-0000-7000-8000-000000000050'

function refund(overrides: Record<string, unknown> = {}) {
  return {
    publicId: refundId,
    refundNumber: 'RF-202609-000001',
    orderPublicId: '018f2e6a-0000-7000-8000-000000000030',
    returnPublicId: '018f2e6a-0000-7000-8000-000000000040',
    status: 'approved',
    requestedAmount: 500,
    approvedAmount: 480,
    succeededAmount: null,
    allocations: [
      { orderItemPublicId: 'item-1', quantity: 1, type: 'itemRefund', amount: 500 },
      { orderItemPublicId: null, quantity: null, type: 'discountClawback', amount: 20 },
    ],
    requestedBy: { publicId: 'admin-1', maskedLabel: 'f***@example.test' },
    approvedBy: { publicId: 'admin-1', maskedLabel: 'f***@example.test' },
    executedBy: null,
    createdAtUtc: '2026-09-01T00:00:00Z',
    succeededAtUtc: null,
    rowVersion: 'AAAAAAAAAAE=',
    ...overrides,
  }
}

async function mountPage() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/refunds', component: { template: '<div />' } },
      { path: '/refunds/:refundId', component: AdminRefundDetailPage },
      { path: '/orders/:orderId', component: { template: '<div />' } },
      { path: '/returns/:returnId', component: { template: '<div />' } },
    ],
  })
  await router.push(`/refunds/${refundId}`)
  await router.isReady()
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(AdminRefundDetailPage, {
    global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
  })
}

describe('AdminRefundDetailPage', () => {
  beforeEach(() => vi.resetAllMocks())

  it('explains the signed allocation breakdown and approved limit', async () => {
    mockGetRefund.mockResolvedValue(refund())

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('退款上限')
    expect(wrapper.text()).toContain('商品退款')
    expect(wrapper.text()).toContain('優惠追回')
    expect(wrapper.text()).toContain('+NT$500')
    expect(wrapper.text()).toContain('-NT$20')
  })

  it('requires an explicit confirmation before executing the approved refund', async () => {
    mockGetRefund.mockResolvedValue(refund())
    mockExecuteRefund.mockResolvedValue(refund({ status: 'succeeded', succeededAmount: 480 }))

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('[name="reasonCode"]').setValue('customer_request')

    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeDefined()
    await wrapper.find('[name="confirmed"]').setValue(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockExecuteRefund).toHaveBeenCalledWith(
      refundId,
      expect.objectContaining({
        reasonCode: 'customer_request',
        refundRowVersion: 'AAAAAAAAAAE=',
      }),
      expect.any(String),
    )
  })

  it('can execute an approved refund before allocations are persisted', async () => {
    // 分攤在 RefundExecutor 成功執行時才由可信快照計算並寫入；待執行退款的空陣列
    // 不是「缺少快照」的證據，不能拿來鎖住執行按鈕。
    mockGetRefund.mockResolvedValue(refund({ allocations: [] }))
    mockExecuteRefund.mockResolvedValue(refund({ status: 'succeeded', succeededAmount: 480 }))

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('[name="reasonCode"]').setValue('customer_request')
    await wrapper.find('[name="confirmed"]').setValue(true)

    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeUndefined()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockExecuteRefund).toHaveBeenCalledOnce()
  })

  it('requires an explicit confirmation before approving a pending refund', async () => {
    mockGetRefund.mockResolvedValue(refund({ status: 'pendingReview', approvedBy: null }))
    mockApproveRefund.mockResolvedValue(refund({ status: 'approved', approvedAmount: 480 }))

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('[name="approveReasonCode"]').setValue('return_approved')

    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeDefined()
    await wrapper.find('[name="approveConfirmed"]').setValue(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockApproveRefund).toHaveBeenCalledWith(
      refundId,
      expect.objectContaining({
        reasonCode: 'return_approved',
        refundRowVersion: 'AAAAAAAAAAE=',
      }),
      expect.any(String),
    )
  })

  it('does not show the execute action for a refund still awaiting approval', async () => {
    mockGetRefund.mockResolvedValue(refund({ status: 'pendingReview', approvedBy: null }))

    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.find('#refund-approve-title').exists()).toBe(true)
    expect(wrapper.find('#refund-execute-title').exists()).toBe(false)
  })

  it('surfaces a zero-net approval as a cancelled refund, not an error', async () => {
    // alex 2026-09-04 #103 裁定，延續 #99 A1：核准時重算後已無款可退終止為
    // Cancelled，Controller 一律回 200——前端不需要另外分支，畫面只是照常顯示
    // 重新查詢回來的狀態。
    mockGetRefund.mockResolvedValue(refund({ status: 'pendingReview', approvedBy: null }))
    mockApproveRefund.mockResolvedValue(
      refund({ status: 'cancelled', approvedAmount: null, allocations: [] }),
    )

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('[name="approveReasonCode"]').setValue('return_approved')
    await wrapper.find('[name="approveConfirmed"]').setValue(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockApproveRefund).toHaveBeenCalledOnce()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('已取消')

    // alex 2026-09-04 #104 review：已取消的頁面上方說「不需要另外處理」，空分攤區
    // 不能沿用「尚未執行退款……成功後顯示於此」那段——兩者互相矛盾，只斷言
    // 「已取消」看不出這個矛盾還在。
    expect(wrapper.text()).toContain('未產生退款分攤')
    expect(wrapper.text()).not.toContain('尚未執行退款')
  })

  it('shows an approval-specific message when the trusted snapshot is unavailable', async () => {
    // alex 2026-09-04 #104 review：refund_snapshot_unavailable 的共用文字原本寫死
    // 「不能執行退款」，但這裡撞到的是核准動作，不是執行——文字不能誤導管理員
    // 以為是執行失敗。
    mockGetRefund.mockResolvedValue(refund({ status: 'pendingReview', approvedBy: null }))
    mockApproveRefund.mockRejectedValue(
      new ApiError('Conflict', { status: 409, code: 'refund_snapshot_unavailable' }),
    )

    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('[name="approveReasonCode"]').setValue('return_approved')
    await wrapper.find('[name="approveConfirmed"]').setValue(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const alert = wrapper.find('[role="alert"]')
    expect(alert.exists()).toBe(true)
    expect(alert.text()).toContain('無法處理退款')
    expect(alert.text()).not.toContain('不能執行退款')
  })
})

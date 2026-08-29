import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { ApiError } from '@doselect/web-shared/api'
import { describe, expect, it, vi } from 'vitest'

const mockListCoupons = vi.fn()
const mockGetCoupon = vi.fn()
const mockCreateCoupon = vi.fn()
const mockUpdateCoupon = vi.fn()
const mockExecuteCouponAction = vi.fn()

vi.mock('../../features/coupons/api', () => ({
  listCoupons: mockListCoupons,
  getCoupon: mockGetCoupon,
  createCoupon: mockCreateCoupon,
  updateCoupon: mockUpdateCoupon,
  executeCouponAction: mockExecuteCouponAction,
}))

const { default: AdminCouponsPage } = await import('./AdminCouponsPage.vue')

function coupon(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'c1',
    code: 'WELCOME300',
    nameZhTw: '新會員',
    discountType: 'fixedAmount',
    status: 'draft',
    discountValue: 300,
    minimumSpend: 3000,
    maximumDiscount: null,
    startsAtUtc: '2026-08-20T00:00:00Z',
    endsAtUtc: '2026-09-20T00:00:00Z',
    memberOnly: false,
    excludeSaleItems: false,
    scope: { scopeType: 'all', categoryPublicIds: [], productPublicIds: [], excludedProductPublicIds: [] },
    usage: { totalRedeemedCount: 2, totalUsageLimit: 100, perMemberLimit: 1, remainingCount: 98 },
    ruleVersion: 1,
    createdAtUtc: '2026-08-19T00:00:00Z',
    updatedAtUtc: '2026-08-19T00:00:00Z',
    rowVersion: 'AAA=',
    ...overrides,
  }
}

function page(items: ReturnType<typeof coupon>[]) {
  return { items, pageNumber: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }
}

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(AdminCouponsPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
}

describe('AdminCouponsPage', () => {
  it('renders the loaded coupon list', async () => {
    mockListCoupons.mockResolvedValueOnce(page([coupon()]))

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('WELCOME300')
    expect(wrapper.text()).toContain('新會員')
    expect(wrapper.text()).toContain('草稿')
    expect(wrapper.text()).toContain('2 / 100')
  })

  it('shows a percentage discount as percentage points, not the stored fraction', async () => {
    // Domain 的百分比是 0～1 的比例；直接顯示 0.1 會被讀成「一折」。
    mockListCoupons.mockResolvedValueOnce(page([
      coupon({ discountType: 'percentage', discountValue: 0.1, maximumDiscount: 500 }),
    ]))

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('10%')
    expect(wrapper.text()).not.toContain('0.1%')
  })

  it.each([
    ['scheduled', '已排程'],
    ['exhausted', '名額用盡'],
  ])('does not offer activate for %s, which is a system event', async (status) => {
    // Scheduled → Active 是「到達開始時間」、Exhausted → Active 是「名額返還」，
    // 兩者都是系統事件，後端會以 coupon_state_conflict 拒絕管理員的 activate。
    mockListCoupons.mockResolvedValueOnce(page([coupon({ status })]))

    const wrapper = mountPage()
    await flushPromises()

    const actionButtons = wrapper.findAll('.coupons-actions button').map((button) => button.text())
    expect(actionButtons).not.toContain('啟用')
    expect(actionButtons).toContain('停用')
  })

  it.each([
    ['expired'],
    ['disabled'],
  ])('offers no state action for the terminal status %s', async (status) => {
    mockListCoupons.mockResolvedValueOnce(page([coupon({ status })]))

    const wrapper = mountPage()
    await flushPromises()

    const actionButtons = wrapper.findAll('.coupons-actions button').map((button) => button.text())
    expect(actionButtons).not.toContain('啟用')
    expect(actionButtons).not.toContain('暫停')
    expect(actionButtons).not.toContain('停用')
  })

  it('sends the RowVersion carried from the loaded row when running an action', async () => {
    mockListCoupons.mockResolvedValue(page([coupon({ status: 'active' })]))
    mockExecuteCouponAction.mockResolvedValueOnce(coupon({ status: 'paused' }))

    const wrapper = mountPage()
    await flushPromises()

    const pause = wrapper.findAll('.coupons-actions button').find((button) => button.text() === '暫停')
    await pause!.trigger('click')
    await flushPromises()

    expect(mockExecuteCouponAction).toHaveBeenCalledWith(
      'c1',
      'pause',
      expect.objectContaining({ rowVersion: 'AAA=' }),
    )
  })

  it('converts the percentage input back to a fraction before sending it', async () => {
    mockListCoupons.mockResolvedValue(page([]))
    mockCreateCoupon.mockResolvedValueOnce(coupon())

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.find('.coupons-header button').trigger('click')
    await wrapper.find('[name="code"]').setValue('SAVE10')
    await wrapper.find('[name="nameZhTw"]').setValue('九折')
    await wrapper.find('[name="discountType"]').setValue('percentage')
    await wrapper.find('[name="discountValue"]').setValue('10')
    await wrapper.find('[name="startsAt"]').setValue('2026-09-01T00:00')
    await wrapper.find('[name="endsAt"]').setValue('2026-09-30T00:00')
    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    // 表單填 10（百分點），送出必須是 0.1（比例）。送 10 會被 Domain 直接拒絕。
    expect(mockCreateCoupon).toHaveBeenCalledWith(
      expect.objectContaining({ discountType: 'percentage', discountValue: 0.1 }),
    )
  })

  it('surfaces a state conflict from an action instead of failing silently', async () => {
    mockListCoupons.mockResolvedValue(page([coupon({ status: 'active' })]))
    mockExecuteCouponAction.mockRejectedValueOnce(
      new ApiError('conflict', { status: 409, code: 'coupon_state_conflict' }),
    )

    const wrapper = mountPage()
    await flushPromises()

    const pause = wrapper.findAll('.coupons-actions button').find((button) => button.text() === '暫停')
    await pause!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('優惠券目前狀態不允許這個操作')
  })

  it('shows the rule preview only for the expanded coupon', async () => {
    mockListCoupons.mockResolvedValue(page([coupon()]))

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).not.toContain('規則版本')

    const preview = wrapper.findAll('.coupons-actions button').find((button) => button.text() === '規則預覽')
    await preview!.trigger('click')

    expect(wrapper.text()).toContain('規則版本')
    expect(wrapper.text()).toContain('全站')
  })

  it('renders an empty state rather than an empty table', async () => {
    mockListCoupons.mockResolvedValueOnce(page([]))

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('沒有符合條件的優惠券')
    expect(wrapper.find('table').exists()).toBe(false)
  })
})

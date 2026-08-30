import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { ApiError } from '@doselect/web-shared/api'
import { beforeEach, describe, expect, it, vi } from 'vitest'

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

const mockLoadCategoryOptions = vi.fn()
const mockSearchProductOptions = vi.fn()
const mockResolveProductOptions = vi.fn()

vi.mock('../../features/catalog-reference/api', () => ({
  loadCategoryOptions: mockLoadCategoryOptions,
  searchProductOptions: mockSearchProductOptions,
  resolveProductOptions: mockResolveProductOptions,
  CategoryTreeTruncatedError: class extends Error {},
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

/** 挑選器選項的預設形狀；status 與 isSelectable 少了就會被當成不可選。 */
function productOption(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'p9',
    code: 'GPU-09',
    name: '旗艦顯示卡',
    status: 'published',
    isSelectable: true,
    ...overrides,
  }
}

function page(items: ReturnType<typeof coupon>[]) {
  return { items, pageNumber: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }
}

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/coupons', name: 'admin-coupons', component: AdminCouponsPage }],
  })
}

/**
 * 掛頁面。傳 `router` 進來的測試自己決定起始網址（用來驗還原條件）；
 * 其餘測試拿一個乾淨的 router。
 */
function mountPage(router: Router = createTestRouter()) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(AdminCouponsPage, {
    global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
  })
}

/** 開啟新增表單並填好與範圍無關的必填欄位。 */
async function openCreateForm(wrapper: ReturnType<typeof mountPage>) {
  await wrapper.find('.coupons-header button').trigger('click')
  await wrapper.find('[name="code"]').setValue('SCOPE300')
  await wrapper.find('[name="nameZhTw"]').setValue('範圍測試')
  await wrapper.find('[name="discountValue"]').setValue('300')
  await wrapper.find('[name="startsAt"]').setValue('2026-09-01T00:00')
  await wrapper.find('[name="endsAt"]').setValue('2026-09-30T00:00')
}

describe('AdminCouponsPage', () => {
  // 每個測試都自己安排回應。不清掉累積的呼叫紀錄，`toHaveBeenCalledWith`
  // 會比對到**上一個測試**留下的呼叫 —— 送出內容退化時測試仍然是綠的。
  beforeEach(() => {
    vi.resetAllMocks()
    mockLoadCategoryOptions.mockResolvedValue([])
    mockSearchProductOptions.mockResolvedValue({ items: [], hasMore: false })
    mockResolveProductOptions.mockResolvedValue({})
  })

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
  ])('offers neither a state action nor an edit for the terminal status %s', async (status) => {
    // Domain 的規則修訂在 Expired／Disabled 直接拒絕，管理員會填完整張表單才拿到 409。
    mockListCoupons.mockResolvedValueOnce(page([coupon({ status })]))

    const wrapper = mountPage()
    await flushPromises()

    const actionButtons = wrapper.findAll('.coupons-actions button').map((button) => button.text())
    expect(actionButtons).not.toContain('啟用')
    expect(actionButtons).not.toContain('暫停')
    expect(actionButtons).not.toContain('停用')
    expect(actionButtons).not.toContain('修改')
    expect(actionButtons).toContain('規則預覽')
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
    // 百分比券必須有最高折抵，否則後端 RequireValidRule 回 400。
    await wrapper.find('[name="maximumDiscount"]').setValue('500')
    await wrapper.find('[name="startsAt"]').setValue('2026-09-01T00:00')
    await wrapper.find('[name="endsAt"]').setValue('2026-09-30T00:00')
    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    // 表單填 10（百分點），送出必須是 0.1（比例）。送 10 會被 Domain 直接拒絕。
    expect(mockCreateCoupon).toHaveBeenCalledWith(
      expect.objectContaining({
        discountType: 'percentage',
        discountValue: 0.1,
        maximumDiscount: 500,
      }),
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

  it('will not submit 指定範圍 with nothing selected', async () => {
    // 後端 RequireValidRule 會以 400 拒絕，但那要等使用者填完整張表單才知道。
    mockListCoupons.mockResolvedValue(page([]))
    mockLoadCategoryOptions.mockResolvedValue([])

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.find('.coupons-header button').trigger('click')
    await wrapper.find('input[value="restricted"]').trigger('change')
    await flushPromises()

    expect(wrapper.text()).toContain('指定範圍至少要選擇一個分類或商品。')
    expect(wrapper.find('.coupons-form-actions button[type="submit"]').attributes('disabled'))
      .toBeDefined()
  })

  it('sends the chosen category with a 指定範圍 coupon', async () => {
    mockListCoupons.mockResolvedValue(page([]))
    mockCreateCoupon.mockResolvedValueOnce(coupon())
    mockLoadCategoryOptions.mockResolvedValue([
      { publicId: 'cat-1', code: 'gpu', name: '顯示卡', path: '電腦 / 顯示卡' },
    ])

    const wrapper = mountPage()
    await flushPromises()

    await openCreateForm(wrapper)
    await wrapper.find('input[value="restricted"]').trigger('change')
    await flushPromises()

    await wrapper.find('.scope-results input[type="checkbox"]').trigger('change')
    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    expect(mockCreateCoupon).toHaveBeenCalledWith(expect.objectContaining({
      scopeType: 'restricted',
      categoryPublicIds: ['cat-1'],
      productPublicIds: null,
    }))
  })

  it('drops an included selection when the admin switches back to 全站', async () => {
    // 只把挑選器收起來是不夠的：後端對「All 卻帶了包含範圍」直接回 400，
    // 而「先挑了分類、又改回全站」是很自然的操作順序。
    mockListCoupons.mockResolvedValue(page([]))
    mockCreateCoupon.mockResolvedValueOnce(coupon())
    mockLoadCategoryOptions.mockResolvedValue([
      { publicId: 'cat-1', code: 'gpu', name: '顯示卡', path: '電腦 / 顯示卡' },
    ])

    const wrapper = mountPage()
    await flushPromises()

    await openCreateForm(wrapper)
    await wrapper.find('input[value="restricted"]').trigger('change')
    await flushPromises()
    await wrapper.find('.scope-results input[type="checkbox"]').trigger('change')

    // 先確認真的選到了。少了這一步，這個測試在「切回全站會丟掉」壞掉時
    // 仍然會綠 —— 因為「從來沒選到」跟「選了但正確丟掉」送出的內容一模一樣。
    expect((wrapper.find('.scope-results input[type="checkbox"]').element as HTMLInputElement).checked)
      .toBe(true)

    await wrapper.find('input[value="all"]').trigger('change')
    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    expect(mockCreateCoupon).toHaveBeenCalledWith(expect.objectContaining({
      scopeType: 'all',
      categoryPublicIds: null,
      productPublicIds: null,
    }))
  })

  it('keeps an exclusion list on a 全站 coupon', async () => {
    // 「全站折扣，特定機種除外」是後端允許的組合，不能跟包含範圍一起被丟掉。
    mockListCoupons.mockResolvedValue(page([]))
    mockCreateCoupon.mockResolvedValueOnce(coupon())
    mockSearchProductOptions.mockResolvedValue({
      items: [productOption()],
      hasMore: false,
    })
    mockResolveProductOptions.mockResolvedValue({})

    const wrapper = mountPage()
    await flushPromises()

    await openCreateForm(wrapper)
    await wrapper.find('[aria-label="搜尋排除商品"]').setValue('顯示卡')
    await wrapper.findAll('.scope-products button').find(button => button.text() === '搜尋')!
      .trigger('click')
    await flushPromises()

    await wrapper.find('.scope-results input[type="checkbox"]').trigger('change')
    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    expect(mockCreateCoupon).toHaveBeenCalledWith(expect.objectContaining({
      scopeType: 'all',
      excludedProductPublicIds: ['p9'],
    }))
  })

  it('pre-fills an existing 指定範圍 coupon and sends it back unchanged', async () => {
    mockListCoupons.mockResolvedValue(page([coupon({
      scope: {
        scopeType: 'restricted',
        categoryPublicIds: ['cat-1'],
        productPublicIds: [],
        excludedProductPublicIds: ['p9'],
      },
    })]))
    mockUpdateCoupon.mockResolvedValueOnce(coupon())
    mockLoadCategoryOptions.mockResolvedValue([
      { publicId: 'cat-1', code: 'gpu', name: '顯示卡', path: '電腦 / 顯示卡' },
    ])
    mockResolveProductOptions.mockResolvedValue({
      p9: productOption(),
    })

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('.coupons-actions button').find(button => button.text() === '修改')!
      .trigger('click')
    await flushPromises()

    expect(wrapper.find('input[value="restricted"]').attributes('checked')).toBeDefined()
    expect(wrapper.text()).toContain('旗艦顯示卡')

    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    expect(mockUpdateCoupon).toHaveBeenCalledWith('c1', expect.objectContaining({
      scopeType: 'restricted',
      categoryPublicIds: ['cat-1'],
      excludedProductPublicIds: ['p9'],
      rowVersion: 'AAA=',
    }))
  })

  it('names a selected category that is no longer active instead of hiding it', async () => {
    // 分類清單只含啟用中的分類。舊券引用的分類被停用後仍然生效，
    // 不列出來管理員會以為這張券沒有設定分類範圍。
    mockListCoupons.mockResolvedValue(page([coupon({
      scope: {
        scopeType: 'restricted',
        categoryPublicIds: ['cat-retired'],
        productPublicIds: [],
        excludedProductPublicIds: [],
      },
    })]))
    mockLoadCategoryOptions.mockResolvedValue([])

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('.coupons-actions button').find(button => button.text() === '修改')!
      .trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('cat-retired')
    expect(wrapper.text()).toContain('已停用或找不到的分類')
  })

  it('summarises the scope in the rule preview', async () => {
    mockListCoupons.mockResolvedValue(page([coupon({
      scope: {
        scopeType: 'restricted',
        categoryPublicIds: ['cat-1', 'cat-2'],
        productPublicIds: ['p1'],
        excludedProductPublicIds: ['p9'],
      },
    })]))

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('.coupons-actions button').find(button => button.text() === '規則預覽')!
      .trigger('click')

    expect(wrapper.text()).toContain('指定 2 個分類、1 件商品，排除 1 件商品')
  })

  it('does not shift the UTC period or the scope when only the name is edited', async () => {
    // UpdateCouponRequest 是完整覆寫不是 patch，所以「沒有被編輯的欄位」也得原封不動送回去。
    // CI 跑在 UTC，而錯的寫法在 UTC 下往返剛好是對的 —— 一定要指定非 UTC 時區。
    vi.stubEnv('TZ', 'Asia/Taipei')

    try {
      mockListCoupons.mockResolvedValue(page([coupon({
        scope: {
          scopeType: 'restricted',
          categoryPublicIds: ['cat-1'],
          productPublicIds: [],
          excludedProductPublicIds: ['p9'],
        },
      })]))
      mockUpdateCoupon.mockResolvedValueOnce(coupon())
      mockLoadCategoryOptions.mockResolvedValue([
        { publicId: 'cat-1', code: 'gpu', name: '顯示卡', path: '電腦 / 顯示卡' },
      ])
      mockResolveProductOptions.mockResolvedValue({
        p9: productOption(),
      })

      const wrapper = mountPage()
      await flushPromises()

      await wrapper.findAll('.coupons-actions button').find(button => button.text() === '修改')!
        .trigger('click')
      await flushPromises()

      // 表單要顯示**本機**牆上時間。少了這條，下面的斷言證明不了載入方向是對的：
      // toUtcInstant 在輸入值與原值相符時原樣送回，兩邊用同一個（壞掉的）轉換
      // 仍然會相符，instant 照樣完好 —— 錯的是管理員眼前看到的時間。
      expect((wrapper.find('[name="startsAt"]').element as HTMLInputElement).value)
        .toBe('2026-08-20T08:00')

      await wrapper.find('[name="nameZhTw"]').setValue('改個名字')
      await wrapper.find('.coupons-form').trigger('submit')
      await flushPromises()

      expect(mockUpdateCoupon).toHaveBeenCalledWith('c1', expect.objectContaining({
        nameZhTw: '改個名字',
        startsAtUtc: '2026-08-20T00:00:00Z',
        endsAtUtc: '2026-09-20T00:00:00Z',
        scopeType: 'restricted',
        categoryPublicIds: ['cat-1'],
        excludedProductPublicIds: ['p9'],
      }))
    }
    finally {
      vi.unstubAllEnvs()
    }
  })

  it('drops the amount fields when the type is switched to free shipping', async () => {
    // 先填了金額再改成免運是很自然的順序。只把欄位藏起來，那個數字還在表單狀態裡。
    mockListCoupons.mockResolvedValue(page([]))
    mockCreateCoupon.mockResolvedValueOnce(coupon())

    const wrapper = mountPage()
    await flushPromises()

    await openCreateForm(wrapper)
    await wrapper.find('[name="maximumDiscount"]').setValue('500')
    await wrapper.find('[name="discountType"]').setValue('freeShipping')
    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    expect(mockCreateCoupon).toHaveBeenCalledWith(expect.objectContaining({
      discountType: 'freeShipping',
      discountValue: null,
      maximumDiscount: null,
    }))
  })

  it('stops asking for an amount once the type is free shipping', async () => {
    // Domain 的 HasCompleteDiscountRule 對兩種免運一律為 true，不看 DiscountValue，
    // 而 discountValue 欄位是 required —— 留著會讓免運券根本送不出去。
    mockListCoupons.mockResolvedValue(page([]))

    const wrapper = mountPage()
    await flushPromises()

    await openCreateForm(wrapper)
    expect(wrapper.find('[name="discountValue"]').exists()).toBe(true)

    await wrapper.find('[name="discountType"]').setValue('assemblyFreeShipping')

    expect(wrapper.find('[name="discountValue"]').exists()).toBe(false)
    expect(wrapper.find('[name="maximumDiscount"]').exists()).toBe(false)
  })

  it.each([
    ['freeShipping', '一般免運'],
    ['assemblyFreeShipping', '組裝免運'],
  ])('names a %s coupon instead of showing it as a zero discount', async (discountType, label) => {
    // 免運券的 discountValue 是 null，當成金額顯示會變成「—」或 NT$0，
    // 看起來像一張沒有效果的券。
    mockListCoupons.mockResolvedValueOnce(page([
      coupon({ discountType, discountValue: null }),
    ]))

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain(label)
    expect(wrapper.text()).not.toContain('NT$0')
  })

  it('loads a free-shipping coupon back into the form without inventing an amount', async () => {
    mockListCoupons.mockResolvedValue(page([
      coupon({ discountType: 'freeShipping', discountValue: null }),
    ]))
    mockUpdateCoupon.mockResolvedValueOnce(coupon())

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('.coupons-actions button').find(button => button.text() === '修改')!
      .trigger('click')
    await flushPromises()

    expect((wrapper.find('[name="discountType"]').element as HTMLSelectElement).value)
      .toBe('freeShipping')

    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    expect(mockUpdateCoupon).toHaveBeenCalledWith('c1', expect.objectContaining({
      discountType: 'freeShipping',
      discountValue: null,
    }))
  })

  it('does not carry an edit error into a freshly opened create form', async () => {
    // 錯誤區同時讀 createMutation 與 updateMutation，只清目前這一個是不夠的。
    mockListCoupons.mockResolvedValue(page([coupon()]))
    mockUpdateCoupon.mockRejectedValueOnce(
      new ApiError('conflict', { status: 409, code: 'coupon_state_conflict' }),
    )

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('.coupons-actions button').find(button => button.text() === '修改')!
      .trigger('click')
    await flushPromises()
    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('優惠券目前狀態不允許這個操作')

    await wrapper.findAll('.coupons-form-actions button').find(button => button.text() === '取消')!
      .trigger('click')
    await wrapper.find('.coupons-header button').trigger('click')
    await flushPromises()

    expect(wrapper.text()).not.toContain('優惠券目前狀態不允許這個操作')
  })

  it('will not submit a percentage coupon without a maximum discount', async () => {
    // 後端 RequireValidRule 要求 percentage 的 maximumDiscount > 0，
    // 前端不擋的話，管理員照介面正常填完會直接吃一句英文 400。
    mockListCoupons.mockResolvedValue(page([]))

    const wrapper = mountPage()
    await flushPromises()

    await openCreateForm(wrapper)
    await wrapper.find('[name="discountType"]').setValue('percentage')
    await wrapper.find('[name="discountValue"]').setValue('10')

    expect(wrapper.text()).toContain('百分比折扣必須填寫大於 0 的最高折抵。')
    expect(wrapper.find('.coupons-form-actions button[type="submit"]').attributes('disabled'))
      .toBeDefined()

    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    expect(mockCreateCoupon).not.toHaveBeenCalled()
  })

  it('accepts a percentage coupon once the maximum discount is filled in', async () => {
    mockListCoupons.mockResolvedValue(page([]))
    mockCreateCoupon.mockResolvedValueOnce(coupon())

    const wrapper = mountPage()
    await flushPromises()

    await openCreateForm(wrapper)
    await wrapper.find('[name="discountType"]').setValue('percentage')
    await wrapper.find('[name="discountValue"]').setValue('10')
    await wrapper.find('[name="maximumDiscount"]').setValue('500')
    await wrapper.find('.coupons-form').trigger('submit')
    await flushPromises()

    expect(mockCreateCoupon).toHaveBeenCalledWith(expect.objectContaining({
      discountValue: 0.1,
      maximumDiscount: 500,
    }))
  })

  it('asks for confirmation before disabling, and cancelling calls no API', async () => {
    // disabled 是終態，誤觸之後沒有任何方式可以重新啟用。
    mockListCoupons.mockResolvedValue(page([coupon({ status: 'active' })]))

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('.coupons-actions button').find(button => button.text() === '停用')!
      .trigger('click')

    expect(wrapper.find('.coupons-confirm').exists()).toBe(true)
    expect(wrapper.text()).toContain('WELCOME300')
    expect(wrapper.text()).toContain('啟用中')
    expect(wrapper.text()).toContain('終態')
    expect(mockExecuteCouponAction).not.toHaveBeenCalled()

    await wrapper.findAll('.coupons-confirm-actions button').find(b => b.text() === '取消')!
      .trigger('click')
    await flushPromises()

    expect(wrapper.find('.coupons-confirm').exists()).toBe(false)
    expect(mockExecuteCouponAction).not.toHaveBeenCalled()
  })

  it('disables only after the confirmation is accepted', async () => {
    mockListCoupons.mockResolvedValue(page([coupon({ status: 'active' })]))
    mockExecuteCouponAction.mockResolvedValueOnce(coupon({ status: 'disabled' }))

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('.coupons-actions button').find(button => button.text() === '停用')!
      .trigger('click')
    await wrapper.findAll('.coupons-confirm-actions button').find(b => b.text() === '確認停用')!
      .trigger('click')
    await flushPromises()

    expect(mockExecuteCouponAction).toHaveBeenCalledWith(
      'c1', 'disable', expect.objectContaining({ rowVersion: 'AAA=' }))
  })

  it('runs a reversible action without asking for confirmation', async () => {
    // 暫停可以再啟用回來，不需要多擋一層。
    mockListCoupons.mockResolvedValue(page([coupon({ status: 'active' })]))
    mockExecuteCouponAction.mockResolvedValueOnce(coupon({ status: 'paused' }))

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('.coupons-actions button').find(button => button.text() === '暫停')!
      .trigger('click')
    await flushPromises()

    expect(wrapper.find('.coupons-confirm').exists()).toBe(false)
    expect(mockExecuteCouponAction).toHaveBeenCalledWith('c1', 'pause', expect.anything())
  })

  it('restores the list conditions from the URL', async () => {
    // 重新整理、返回或把網址貼給別人，看到的必須是同一份清單。
    mockListCoupons.mockResolvedValue(page([]))
    const router = createTestRouter()
    await router.push('/coupons?q=WEL&status=active&status=paused&page=3')
    await router.isReady()

    mountPage(router)
    await flushPromises()

    expect(mockListCoupons).toHaveBeenCalledWith(expect.objectContaining({
      q: 'WEL',
      statuses: ['active', 'paused'],
      pageNumber: 3,
    }))
  })

  it('ignores a status in the URL that is not a real coupon status', async () => {
    mockListCoupons.mockResolvedValue(page([]))
    const router = createTestRouter()
    await router.push('/coupons?status=active&status=not-a-status')
    await router.isReady()

    mountPage(router)
    await flushPromises()

    expect(mockListCoupons).toHaveBeenCalledWith(expect.objectContaining({
      statuses: ['active'],
    }))
  })

  it('writes the applied conditions back to the URL', async () => {
    mockListCoupons.mockResolvedValue(page([]))
    const router = createTestRouter()
    await router.push('/coupons')
    await router.isReady()

    const wrapper = mountPage(router)
    await flushPromises()

    await wrapper.findAll('.coupons-statuses input')[2].trigger('change')
    await flushPromises()

    expect(router.currentRoute.value.query.status).toEqual(['active'])
  })

  it('renders an empty state rather than an empty table', async () => {
    mockListCoupons.mockResolvedValueOnce(page([]))

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('沒有符合條件的優惠券')
    expect(wrapper.find('table').exists()).toBe(false)
  })
})

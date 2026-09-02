import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'

const mockList = vi.fn()
const mockCreate = vi.fn()
const mockUpdate = vi.fn()

vi.mock('../features/shipping/api', () => ({
  listConvenienceStores: mockList,
  createConvenienceStore: mockCreate,
  updateConvenienceStore: mockUpdate,
  listPackageLimitVersions: vi.fn(),
  createPackageLimitVersion: vi.fn(),
  publishPackageLimitVersion: vi.fn(),
}))

const { default: ShippingStoresPage } = await import('./ShippingStoresPage.vue')
const { useAdminAuthStore } = await import('../features/auth/stores/useAdminAuthStore')

function store(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 's1',
    providerCode: '7-11',
    storeCode: 'ST-001',
    storeName: '大安門市',
    address: '台北市大安區某路1號',
    city: '台北市',
    district: '大安區',
    isDemoData: true,
    isActive: true,
    rowVersion: 'AAA=',
    ...overrides,
  }
}

function page(items: unknown[]) {
  return { items, pageNumber: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }
}

function signIn(roles: string[]) {
  const auth = useAdminAuthStore()
  auth.session = {
    isAuthenticated: true,
    user: {
      publicId: 'admin-1',
      displayName: 'Ops',
      emailMasked: 'o***@example.test',
      emailVerified: true,
      locale: 'zh-TW',
      roles,
    },
    expiresAtUtc: null,
    requiresTwoFactor: false,
  }
}

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(ShippingStoresPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
}

describe('ShippingStoresPage', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    signIn(['OrderManager'])
  })

  afterEach(() => {
    vi.restoreAllMocks()
    mockList.mockReset()
    mockCreate.mockReset()
    mockUpdate.mockReset()
  })

  it('renders the loaded stores and marks them as demo data', async () => {
    mockList.mockResolvedValue(page([store()]))

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('大安門市')
    expect(wrapper.text()).toContain('ST-001')
    expect(wrapper.text()).toContain('展示資料')
  })

  /**
   * UC-ADM-STORE-01：「CatalogManager 只有檢視權限」。後端 Policy 才是邊界，但不該給一個按下去
   * 必定 403 的入口。
   */
  it('hides every write control from a view-only CatalogManager', async () => {
    signIn(['CatalogManager'])
    mockList.mockResolvedValue(page([store()]))

    const wrapper = mountPage()
    await flushPromises()

    const labels = wrapper.findAll('button').map((button) => button.text())
    expect(labels).not.toContain('新增門市')
    expect(labels).not.toContain('編輯')
    expect(labels).not.toContain('停用')
  })

  it('offers the write controls to an OrderManager', async () => {
    mockList.mockResolvedValue(page([store()]))

    const wrapper = mountPage()
    await flushPromises()

    const labels = wrapper.findAll('button').map((button) => button.text())
    expect(labels).toContain('新增門市')
    expect(labels).toContain('編輯')
    expect(labels).toContain('停用')
  })

  /** UC-ADM-STORE-01：「拒絕實體刪除，只允許停用」——所以整頁不該有刪除入口。 */
  it('never offers a delete action', async () => {
    mockList.mockResolvedValue(page([store()]))

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.findAll('button').map((button) => button.text())).not.toContain('刪除')
  })

  it('confirms before deactivating and sends isActive false with the RowVersion', async () => {
    mockList.mockResolvedValue(page([store()]))
    mockUpdate.mockResolvedValue(store({ isActive: false }))
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '停用')!.trigger('click')
    await flushPromises()

    expect(globalThis.confirm).toHaveBeenCalled()
    expect(mockUpdate).toHaveBeenCalledWith('s1', expect.objectContaining({
      isActive: false,
      rowVersion: 'AAA=',
    }))
  })

  it('does not deactivate when the confirmation is dismissed', async () => {
    mockList.mockResolvedValue(page([store()]))
    vi.spyOn(globalThis, 'confirm').mockReturnValue(false)

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '停用')!.trigger('click')
    await flushPromises()

    expect(mockUpdate).not.toHaveBeenCalled()
  })

  /** 品牌與門市代碼建立後不可修改（更新契約裡根本沒有這兩個欄位）。 */
  it('does not offer the immutable fields when editing', async () => {
    mockList.mockResolvedValue(page([store()]))

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '編輯')!.trigger('click')

    const editForm = wrapper.find('form[aria-label="編輯門市"]')
    expect(editForm.exists()).toBe(true)
    expect(editForm.find('input[aria-label="門市代碼"]').exists()).toBe(false)
    expect(editForm.find('select[aria-label="新增品牌"]').exists()).toBe(false)
    expect(editForm.find('input[aria-label="編輯門市名稱"]').exists()).toBe(true)
  })

  /** 組長 PR #37 round-2 review, item 3：篩選只綁草稿，送出時才一起套用並把頁碼歸 1。 */
  it('does not query while filters are being edited, then applies them on submit', async () => {
    mockList.mockResolvedValue(page([store()]))

    const wrapper = mountPage()
    await flushPromises()
    const callsBefore = mockList.mock.calls.length

    await wrapper.find('select[aria-label="品牌"]').setValue('FamilyMart')
    await wrapper.find('input[aria-label="縣市"]').setValue('台北市')
    await flushPromises()
    expect(mockList.mock.calls.length).toBe(callsBefore)

    await wrapper.find('form[aria-label="門市篩選"]').trigger('submit')
    await flushPromises()

    expect(mockList).toHaveBeenLastCalledWith(expect.objectContaining({
      providerCode: 'FamilyMart',
      city: '台北市',
      pageNumber: 1,
    }))
  })

  it('surfaces the API error message when a write fails', async () => {
    mockList.mockResolvedValue(page([store()]))
    mockUpdate.mockRejectedValue(new ApiError('conflict', {
      status: 409,
      code: 'concurrency_conflict',
    }))
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '停用')!.trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('已被其他人修改')
  })

  /**
   * 組長 PR #78 round-2 review item 3：在「只顯示啟用中」的最後一頁停用最後一筆，該頁就不存在了
   * ——畫面停在空白頁，而 EmptyState 又把分頁控制藏掉，使用者沒有回到有效頁面的入口。
   */
  it('falls back to the last valid page when a deactivation empties the current one', async () => {
    let totalPages = 2
    mockList.mockImplementation(async ({ pageNumber }: { pageNumber: number }) => ({
      items: pageNumber <= totalPages ? [store({ publicId: `s${pageNumber}` })] : [],
      pageNumber,
      pageSize: 20,
      totalCount: totalPages,
      totalPages,
    }))
    mockUpdate.mockImplementation(async () => {
      totalPages = 1
      return store({ isActive: false })
    })
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('button').find((button) => button.text() === '下一頁')!.trigger('click')
    await flushPromises()
    expect((mockList.mock.calls.at(-1)![0] as { pageNumber: number }).pageNumber).toBe(2)

    await wrapper.findAll('button').find((button) => button.text() === '停用')!.trigger('click')
    await flushPromises()

    await vi.waitFor(() => {
      expect((mockList.mock.calls.at(-1)![0] as { pageNumber: number }).pageNumber).toBe(1)
    })
  })

  /**
   * 組長 PR #78 round-3 review [P2]：`placeholderData` 會跨物流商／縣市／行政區／Active-only／
   * 頁碼保留上一組門市，而頁面只看 `isPending`——新查詢還在飛的時候，畫面上是舊門市，而且那些列
   * 的「編輯」「停用」按鈕照樣按得下去。管理員會在新篩選條件的畫面上改到另一組查詢的門市。
   *
   * 這支測試把第二次查詢卡住，斷言在 pending 期間舊門市「已經不在畫面上」，因此也不可能被編輯
   * 或停用——只斷言「API 被呼叫兩次」是看不出這件事的。
   */
  it('drops the previous stores and their write controls while a new filter is loading', async () => {
    mockList.mockResolvedValueOnce(page([store({ publicId: 's1', storeName: '大安門市' })]))

    const wrapper = mountPage()
    await flushPromises()
    expect(wrapper.text()).toContain('大安門市')
    expect(wrapper.findAll('button').some((button) => button.text() === '停用')).toBe(true)

    let releaseSecond!: (value: unknown) => void
    mockList.mockImplementationOnce(() => new Promise((resolve) => { releaseSecond = resolve }))

    await wrapper.find('input[aria-label="縣市"]').setValue('高雄市')
    await wrapper.find('form[aria-label="門市篩選"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).not.toContain('大安門市')
    expect(wrapper.text()).toContain('門市載入中')
    // 舊列不在畫面上，寫入入口自然也不在——不會有「對上一組查詢的門市按下停用」這種事。
    expect(wrapper.findAll('button').some((button) => button.text() === '停用')).toBe(false)
    expect(wrapper.findAll('button').some((button) => button.text() === '編輯')).toBe(false)

    releaseSecond(page([store({ publicId: 's9', storeName: '苓雅門市', city: '高雄市' })]))
    await flushPromises()
    expect(wrapper.text()).toContain('苓雅門市')
  })

  /** 換頁同理：頁碼也是 query key 的一部分。 */
  it('drops the previous page rows while the next page is loading', async () => {
    mockList.mockResolvedValueOnce({
      items: [store({ publicId: 's1', storeName: '第一頁門市' })],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 40,
      totalPages: 2,
    })

    const wrapper = mountPage()
    await flushPromises()
    expect(wrapper.text()).toContain('第一頁門市')

    let releaseSecond!: (value: unknown) => void
    mockList.mockImplementationOnce(() => new Promise((resolve) => { releaseSecond = resolve }))

    await wrapper.findAll('button').find((button) => button.text() === '下一頁')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).not.toContain('第一頁門市')
    expect(wrapper.findAll('button').some((button) => button.text() === '停用')).toBe(false)

    releaseSecond({
      items: [store({ publicId: 's2', storeName: '第二頁門市' })],
      pageNumber: 2,
      pageSize: 20,
      totalCount: 40,
      totalPages: 2,
    })
    await flushPromises()
    expect(wrapper.text()).toContain('第二頁門市')
  })
})

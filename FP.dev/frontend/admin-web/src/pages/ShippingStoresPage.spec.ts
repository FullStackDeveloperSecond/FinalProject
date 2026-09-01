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
})

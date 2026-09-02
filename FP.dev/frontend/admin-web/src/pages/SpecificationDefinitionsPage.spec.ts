import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'

const mockList = vi.fn()
const mockCreate = vi.fn()
const mockUpdate = vi.fn()
const mockDisable = vi.fn()
const mockListCategories = vi.fn()

vi.mock('../features/specificationDefinitions/api', () => ({
  listSpecificationDefinitions: mockList,
  createSpecificationDefinition: mockCreate,
  updateSpecificationDefinition: mockUpdate,
  disableSpecificationDefinition: mockDisable,
}))

vi.mock('../features/categories/api', () => ({
  listCategories: mockListCategories,
  createCategory: vi.fn(),
  updateCategory: vi.fn(),
}))

const { default: SpecificationDefinitionsPage } = await import('./SpecificationDefinitionsPage.vue')

function definition(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'd1',
    categoryPublicId: 'cat-1',
    categoryCode: 'CPU',
    semanticKey: 'CPU_SOCKET',
    displayNameZhTw: 'CPU 腳位',
    valueType: 'Option',
    unitCode: null,
    isRequired: true,
    allowsMultiple: true,
    isProtected: false,
    isActive: true,
    sortOrder: 0,
    options: [{ publicId: 'o1', code: 'AM5', displayNameZhTw: 'AM5', isActive: true, sortOrder: 0 }],
    rowVersion: 'AAA=',
    ...overrides,
  }
}

function page(items: unknown[], overrides: Record<string, unknown> = {}) {
  return { items, pageNumber: 1, pageSize: 20, totalCount: items.length, totalPages: 1, ...overrides }
}

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const wrapper = mount(SpecificationDefinitionsPage, {
    global: { plugins: [[VueQueryPlugin, { queryClient }]] },
  })
  return { wrapper, queryClient }
}

describe('SpecificationDefinitionsPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    mockList.mockReset()
    mockCreate.mockReset()
    mockUpdate.mockReset()
    mockDisable.mockReset()
    mockListCategories.mockReset()
  })

  it('renders the loaded definitions with their option count and category', async () => {
    mockList.mockResolvedValue(page([definition()]))
    mockListCategories.mockResolvedValue(page([{ publicId: 'cat-1', nameZhTw: 'CPU', code: 'CPU' }]))

    const { wrapper } = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('CPU_SOCKET')
    expect(wrapper.text()).toContain('CPU 腳位')
  })

  /**
   * 資料字典：受保護的 Category／SemanticKey 組合是固定相容性引擎的必要輸入，停用它會讓該分類的
   * 硬性規則永遠缺料。後端會回 specification_definition_referenced，前台也不該讓按鈕可按。
   */
  it('disables the 停用 action for a protected definition', async () => {
    mockList.mockResolvedValue(page([definition({ isProtected: true })]))
    mockListCategories.mockResolvedValue(page([]))

    const { wrapper } = mountPage()
    await flushPromises()

    const disableButton = wrapper.findAll('button').find((button) => button.text() === '停用')!
    expect(disableButton.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('受保護')
  })

  it('confirms before disabling an ordinary definition and sends its RowVersion', async () => {
    mockList.mockResolvedValue(page([definition({ isProtected: false })]))
    mockListCategories.mockResolvedValue(page([]))
    mockDisable.mockResolvedValue(definition({ isActive: false }))
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const { wrapper } = mountPage()
    await flushPromises()

    await wrapper.findAll('button').find((button) => button.text() === '停用')!.trigger('click')
    await flushPromises()

    expect(globalThis.confirm).toHaveBeenCalled()
    expect(mockDisable).toHaveBeenCalledWith('d1', { rowVersion: 'AAA=' })
  })

  it('does not disable when the confirmation is dismissed', async () => {
    mockList.mockResolvedValue(page([definition()]))
    mockListCategories.mockResolvedValue(page([]))
    vi.spyOn(globalThis, 'confirm').mockReturnValue(false)

    const { wrapper } = mountPage()
    await flushPromises()

    await wrapper.findAll('button').find((button) => button.text() === '停用')!.trigger('click')
    await flushPromises()

    expect(mockDisable).not.toHaveBeenCalled()
  })

  /**
   * 組長 PR #37 round-2 review, item 3 的同一條規則：篩選輸入只綁草稿，送出時才一起套用並把頁碼
   * 歸 1，避免「新條件配舊頁碼」。
   */
  it('does not query while filters are being edited, then applies them on submit', async () => {
    mockList.mockResolvedValue(page([definition()]))
    mockListCategories.mockResolvedValue(page([{ publicId: 'cat-1', nameZhTw: 'CPU', code: 'CPU' }]))

    const { wrapper } = mountPage()
    await flushPromises()
    const callsBefore = mockList.mock.calls.length

    await wrapper.find('input[aria-label="關鍵字"]').setValue('socket')
    await wrapper.find('select[aria-label="分類"]').setValue('cat-1')
    await flushPromises()
    expect(mockList.mock.calls.length).toBe(callsBefore)

    await wrapper.find('form[aria-label="規格範本篩選"]').trigger('submit')
    await flushPromises()

    expect(mockList).toHaveBeenLastCalledWith(expect.objectContaining({
      q: 'socket',
      categoryPublicId: 'cat-1',
      pageNumber: 1,
    }))
  })

  /**
   * 資料字典：結構欄位（分類、語意鍵、型別、單位、是否多選）被使用後不可改，所以編輯表單根本
   * 不提供這些欄位——測試釘住這個契約，避免日後有人「順手」把它們加回可編輯。
   */
  it('does not offer the structural fields when editing', async () => {
    mockList.mockResolvedValue(page([definition()]))
    mockListCategories.mockResolvedValue(page([]))

    const { wrapper } = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '編輯')!.trigger('click')

    const editForm = wrapper.find('form[aria-label="編輯規格"]')
    expect(editForm.exists()).toBe(true)
    expect(editForm.find('input[aria-label="語意鍵"]').exists()).toBe(false)
    expect(editForm.find('select[aria-label="值型別"]').exists()).toBe(false)
    expect(editForm.find('input[aria-label="編輯顯示名稱"]').exists()).toBe(true)
  })

  it('sends the edited display name, required flag, sort order and options with the RowVersion', async () => {
    mockList.mockResolvedValue(page([definition()]))
    mockListCategories.mockResolvedValue(page([]))
    mockUpdate.mockResolvedValue(definition())

    const { wrapper } = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '編輯')!.trigger('click')

    await wrapper.find('input[aria-label="編輯顯示名稱"]').setValue('新名稱')
    await wrapper.find('input[aria-label="編輯排序"]').setValue(3)
    await wrapper.find('form[aria-label="編輯規格"]').trigger('submit')
    await flushPromises()

    expect(mockUpdate).toHaveBeenCalledWith('d1', {
      displayNameZhTw: '新名稱',
      isRequired: true,
      sortOrder: 3,
      options: [{ code: 'AM5', displayNameZhTw: 'AM5', sortOrder: 0, isActive: true }],
      rowVersion: 'AAA=',
    })
  })

  /** 受保護規格必須維持必填，所以編輯表單的必填勾選要停用（後端也會擋）。 */
  it('locks the required checkbox for a protected definition', async () => {
    mockList.mockResolvedValue(page([definition({ isProtected: true })]))
    mockListCategories.mockResolvedValue(page([]))

    const { wrapper } = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '編輯')!.trigger('click')

    expect(wrapper.find('input[aria-label="編輯必填"]').attributes('disabled')).toBeDefined()
  })

  /** 非 Option 型別不得帶選項（後端回 specification_invalid），表單也不該顯示選項編輯區。 */
  it('only offers the option editor for an Option definition', async () => {
    mockList.mockResolvedValue(page([]))
    mockListCategories.mockResolvedValue(page([{ publicId: 'cat-1', nameZhTw: 'CPU', code: 'CPU' }]))

    const { wrapper } = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '新增規格')!.trigger('click')

    expect(wrapper.find('fieldset').exists()).toBe(false)
    await wrapper.find('select[aria-label="值型別"]').setValue('Option')
    expect(wrapper.find('fieldset').exists()).toBe(true)
  })

  it('surfaces the API error message when a mutation fails', async () => {
    mockList.mockResolvedValue(page([definition({ isProtected: false })]))
    mockListCategories.mockResolvedValue(page([]))
    mockDisable.mockRejectedValue(new ApiError('conflict', {
      status: 409,
      code: 'specification_definition_referenced',
    }))
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const { wrapper } = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '停用')!.trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('相容性規則')
  })

  /**
   * 組長 PR #77 review item 4：在「只顯示啟用中」的最後一頁停用最後一筆，該頁就不存在了——畫面
   * 停在空白頁，而 EmptyState 又把分頁控制藏掉，使用者沒有回到有效頁面的入口。
   *
   * 這支測試真的走完「翻到第 2 頁 → 該頁最後一筆被停用 → 總頁數掉回 1」的流程；第一版只斷言
   * 「最後一次查詢的頁碼是 1」，但畫面根本沒離開過第 1 頁，拿掉 clamp 也照樣綠（假通過）。
   */
  it('falls back to the last valid page when a mutation empties the current one', async () => {
    mockListCategories.mockResolvedValue(page([]))

    // 一開始有 2 頁；停用之後只剩 1 頁。
    let totalPages = 2
    mockList.mockImplementation(async ({ pageNumber }: { pageNumber: number }) => ({
      items: pageNumber <= totalPages ? [definition({ publicId: `d${pageNumber}` })] : [],
      pageNumber,
      pageSize: 20,
      totalCount: totalPages,
      totalPages,
    }))
    mockDisable.mockImplementation(async () => {
      totalPages = 1
      return definition({ isActive: false })
    })
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const { wrapper } = mountPage()
    await flushPromises()

    await wrapper.findAll('button').find((button) => button.text() === '下一頁')!.trigger('click')
    await flushPromises()
    expect((mockList.mock.calls.at(-1)![0] as { pageNumber: number }).pageNumber).toBe(2)

    // 停用第 2 頁的最後一筆——該頁從此不存在。
    await wrapper.findAll('button').find((button) => button.text() === '停用')!.trigger('click')
    await flushPromises()

    await vi.waitFor(() => {
      expect((mockList.mock.calls.at(-1)![0] as { pageNumber: number }).pageNumber).toBe(1)
    })
    expect(wrapper.text()).not.toContain('沒有符合條件的規格範本')
  })

  /**
   * 組長 PR #77 review item 3：舊版用「目前 code 是否等於某個既有 code」判斷唯讀，新列一打出重複
   * 代碼就被鎖住再也改不掉。新列必須始終可編輯。
   */
  it('keeps a newly added option code editable even when it duplicates an existing one', async () => {
    mockList.mockResolvedValue(page([definition()]))
    mockListCategories.mockResolvedValue(page([]))

    const { wrapper } = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '編輯')!.trigger('click')
    await wrapper.findAll('button').find((button) => button.text() === '新增選項')!.trigger('click')

    // 既有選項的代碼唯讀。
    const existing = wrapper.find('input[aria-label="編輯選項代碼 1"]')
    expect(existing.attributes('readonly')).toBeDefined()

    // 新列打上與既有選項相同的代碼後，仍然必須可以繼續編輯。
    const added = wrapper.find('input[aria-label="編輯選項代碼 2"]')
    await added.setValue('AM5')
    expect(wrapper.find('input[aria-label="編輯選項代碼 2"]').attributes('readonly')).toBeUndefined()
  })

  /** 組長 PR #77 review item 5：目前沒有重新啟用端點，確認文字不可以暗示可以自己開回來。 */
  it('does not promise a re-enable that the API does not offer', async () => {
    mockList.mockResolvedValue(page([definition({ isProtected: false })]))
    mockListCategories.mockResolvedValue(page([]))
    const confirmSpy = vi.spyOn(globalThis, 'confirm').mockReturnValue(false)

    const { wrapper } = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '停用')!.trigger('click')

    const message = confirmSpy.mock.calls.at(-1)![0] as string
    expect(message).not.toContain('只能重新啟用')
    expect(message).toContain('目前沒有提供重新啟用的功能')
  })

  /**
   * 組長 PR #77 round-3 review [P2]：`placeholderData` 會跨分類、關鍵字、只顯示啟用中與頁碼保留上
   * 一組規格，而頁面只看 `isPending`——新請求還在飛的時候畫面上是舊規格，「編輯」「停用」按鈕也
   * 照樣按得下去。管理員因此可能在新條件的畫面下編輯或停用另一組查詢的規格。
   *
   * 這支把第二次查詢卡住，斷言 pending 期間舊列與它的寫入入口都不在畫面上。只斷言「API 被呼叫
   * 兩次」是看不出這件事的。
   */
  it('drops the previous definitions and their write controls while a new filter loads', async () => {
    mockListCategories.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockList.mockResolvedValueOnce(page([definition({ displayNameZhTw: '舊條件規格' })]))

    const { wrapper } = mountPage()
    await flushPromises()
    expect(wrapper.text()).toContain('舊條件規格')
    expect(wrapper.findAll('button').some((button) => button.text() === '停用')).toBe(true)

    let releaseSecond!: (value: unknown) => void
    mockList.mockImplementationOnce(() => new Promise((resolve) => { releaseSecond = resolve }))

    await wrapper.find('input[aria-label="關鍵字"]').setValue('新條件')
    await wrapper.find('form[aria-label="規格範本篩選"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).not.toContain('舊條件規格')
    // 舊列不在畫面上，寫入入口自然也不在。
    expect(wrapper.findAll('button').some((button) => button.text() === '停用')).toBe(false)
    expect(wrapper.findAll('button').some((button) => button.text() === '編輯')).toBe(false)

    releaseSecond(page([definition({ publicId: 'd9', displayNameZhTw: '新條件規格' })]))
    await flushPromises()
    expect(wrapper.text()).toContain('新條件規格')
  })

  /** 換頁同理：頁碼也是 query key 的一部分。 */
  it('drops the previous page rows while the next page is loading', async () => {
    mockListCategories.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0 })
    mockList.mockResolvedValueOnce(
      page([definition({ displayNameZhTw: '第一頁規格' })], { totalCount: 40, totalPages: 2 }))

    const { wrapper } = mountPage()
    await flushPromises()
    expect(wrapper.text()).toContain('第一頁規格')

    let releaseSecond!: (value: unknown) => void
    mockList.mockImplementationOnce(() => new Promise((resolve) => { releaseSecond = resolve }))

    await wrapper.findAll('button').find((button) => button.text() === '下一頁')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).not.toContain('第一頁規格')
    expect(wrapper.findAll('button').some((button) => button.text() === '停用')).toBe(false)

    releaseSecond(page(
      [definition({ publicId: 'd9', displayNameZhTw: '第二頁規格' })],
      { pageNumber: 2, totalCount: 40, totalPages: 2 }))
    await flushPromises()
    expect(wrapper.text()).toContain('第二頁規格')
  })
})

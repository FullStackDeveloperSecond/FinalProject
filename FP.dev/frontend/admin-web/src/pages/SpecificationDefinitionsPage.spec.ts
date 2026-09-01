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
})

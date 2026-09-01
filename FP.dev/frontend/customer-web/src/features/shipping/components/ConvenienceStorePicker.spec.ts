import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { afterEach, describe, expect, it, vi } from 'vitest'

const mockSearch = vi.fn()

vi.mock('../api', () => ({
  getShippingOptions: vi.fn(),
  searchConvenienceStores: (...args: unknown[]) => mockSearch(...args),
}))

const { default: ConvenienceStorePicker } = await import('./ConvenienceStorePicker.vue')

function store(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'st-1',
    providerCode: '7-11',
    storeCode: 'ST-001',
    name: '大安門市',
    city: '台北市',
    district: '大安區',
    address: '某路 1 號',
    isDemoData: true,
    ...overrides,
  }
}

function page(items: unknown[]) {
  return { items, pageNumber: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }
}

function mountPicker(modelValue: string | null = null) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return mount(ConvenienceStorePicker, {
    props: { modelValue },
    global: { plugins: [[VueQueryPlugin, { queryClient }]] },
  })
}

describe('ConvenienceStorePicker', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    mockSearch.mockReset()
  })

  /** 一進結帳頁就打一支沒有條件的門市清單對顧客沒有幫助，也是白費的請求。 */
  it('does not search until the shopper asks for it', async () => {
    mockSearch.mockResolvedValue(page([store()]))

    const wrapper = mountPicker()
    await flushPromises()

    expect(mockSearch).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('請輸入縣市或關鍵字後搜尋門市')
  })

  it('searches with the entered filters and lists the stores', async () => {
    mockSearch.mockResolvedValue(page([store()]))

    const wrapper = mountPicker()
    await wrapper.find('input[aria-label="門市縣市"]').setValue('台北市')
    await wrapper.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()

    expect(mockSearch).toHaveBeenLastCalledWith(expect.objectContaining({ city: '台北市', pageNumber: 1 }))
    expect(wrapper.text()).toContain('大安門市')
    expect(wrapper.text()).toContain('展示資料')
  })

  /** 送給後端的只有門市的 PublicId——名稱／地址是後端建單時自己快照的。 */
  it('emits only the store publicId when one is chosen', async () => {
    mockSearch.mockResolvedValue(page([store()]))

    const wrapper = mountPicker()
    await wrapper.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '選擇')!.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([['st-1']])
  })

  /**
   * 自我審查發現：只記住「這次點選的門市」的話，父層帶進來的 modelValue（回上一步、草稿還原）
   * 永遠不會顯示已選門市。
   */
  it('shows the selected store when the value came from the parent', async () => {
    mockSearch.mockResolvedValue(page([store()]))

    const wrapper = mountPicker('st-1')
    await wrapper.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('已選門市：大安門市')
  })

  it('clears the selection back to null', async () => {
    mockSearch.mockResolvedValue(page([store()]))

    const wrapper = mountPicker('st-1')
    await wrapper.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '重新選擇')!.trigger('click')

    expect(wrapper.emitted('update:modelValue')!.at(-1)).toEqual([null])
  })

  it('shows an empty state when nothing matches', async () => {
    mockSearch.mockResolvedValue(page([]))

    const wrapper = mountPicker()
    await wrapper.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('沒有符合條件的門市')
  })
})

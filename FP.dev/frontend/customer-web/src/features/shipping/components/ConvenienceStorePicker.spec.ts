import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { afterEach, describe, expect, it, vi } from 'vitest'

const mockSearch = vi.fn()

vi.mock('../api', () => ({
  getShippingOptions: vi.fn(),
  searchConvenienceStores: (...args: unknown[]) => mockSearch(...args),
}))

const { default: ConvenienceStorePicker } = await import('./ConvenienceStorePicker.vue')

type ConvenienceStoreOptionDto = import('../types').ConvenienceStoreOptionDto

function store(overrides: Partial<ConvenienceStoreOptionDto> = {}): ConvenienceStoreOptionDto {
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

function mountPicker(
  modelValue: string | null = null,
  selectedSummary: ConvenienceStoreOptionDto | null = null,
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return mount(ConvenienceStorePicker, {
    props: { modelValue, selectedSummary },
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

    // 一併回傳摘要，父層才有東西可以保存並在下次掛載時傳回來。
    expect(wrapper.emitted('update:modelValue')![0][0]).toBe('st-1')
    expect((wrapper.emitted('update:modelValue')![0][1] as { publicId: string }).publicId).toBe('st-1')
  })

  /**
   * 組長 PR #79 round-2 review item 2：靠「目前搜尋結果裡找得到」還原，等於要求父層先搜尋而且該
   * 門市正好在這一頁。改成明確 contract——父層連同摘要一起傳進來，掛載後不搜尋也要顯示得出來。
   */
  it('shows the selected store on mount without searching first', async () => {
    mockSearch.mockResolvedValue(page([]))

    const wrapper = mountPicker('st-1', store())
    await flushPromises()

    expect(mockSearch).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('已選門市：大安門市')
  })

  /** 沒有摘要時仍可從目前結果頁對回來（既有行為不退步）。 */
  it('still resolves the selection from the current result page', async () => {
    mockSearch.mockResolvedValue(page([store()]))

    const wrapper = mountPicker('st-1')
    await wrapper.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('已選門市：大安門市')
  })

  /**
   * 組長 PR #79 round-2 review item 3：切換搜尋條件時不可以還顯示、還能點選上一組條件的門市。
   */
  it('does not offer the previous results while a new search is pending', async () => {
    mockSearch.mockResolvedValueOnce(page([store({ publicId: 'st-1', name: '大安門市' })]))

    const wrapper = mountPicker()
    await wrapper.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('大安門市')

    let releaseSecond!: (value: unknown) => void
    mockSearch.mockImplementationOnce(() => new Promise((resolve) => { releaseSecond = resolve }))

    await wrapper.find('input[aria-label="門市縣市"]').setValue('高雄市')
    await wrapper.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).not.toContain('大安門市')
    expect(wrapper.findAll('button').map((button) => button.text())).not.toContain('選擇')

    releaseSecond(page([store({ publicId: 'st-2', name: '苓雅門市', city: '高雄市' })]))
    await flushPromises()
    expect(wrapper.text()).toContain('苓雅門市')
  })

  it('clears the selection back to null', async () => {
    mockSearch.mockResolvedValue(page([store()]))

    const wrapper = mountPicker('st-1', store())
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '重新選擇')!.trigger('click')

    expect(wrapper.emitted('update:modelValue')!.at(-1)).toEqual([null, null])
  })

  it('shows an empty state when nothing matches', async () => {
    mockSearch.mockResolvedValue(page([]))

    const wrapper = mountPicker()
    await wrapper.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('沒有符合條件的門市')
  })
})

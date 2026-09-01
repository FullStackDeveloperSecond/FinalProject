import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, ref, type PropType } from 'vue'

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

    // 主 model 只帶 PublicId——那是結帳唯一會送出去的欄位。
    expect(wrapper.emitted('update:modelValue')).toEqual([['st-1']])
    // 摘要走自己的具名 model，父層才綁得住（round-3 [P2]）。
    expect((wrapper.emitted('update:selectedSummary')![0][0] as { publicId: string }).publicId)
      .toBe('st-1')
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

    expect(wrapper.emitted('update:modelValue')!.at(-1)).toEqual([null])
    expect(wrapper.emitted('update:selectedSummary')!.at(-1)).toEqual([null])
  })

  it('shows an empty state when nothing matches', async () => {
    mockSearch.mockResolvedValue(page([]))

    const wrapper = mountPicker()
    await wrapper.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('沒有符合條件的門市')
  })

  /**
   * 組長 PR #79 round-3 review [P2]：上一版把摘要放在 `update:modelValue` 的第二個參數，而
   * `v-model` 編譯後只會把第一個 `$event` 寫回 model——第二個參數會被丟掉，父層永遠存不到摘要。
   * 舊測試只直接檢查 emitted arguments，所以完全看不出這件事。
   *
   * 下面兩支改成掛載一個真的用 `v-model` 與 `v-model:selected-summary` 的父元件，走結帳頁真正會
   * 走的路徑。父元件只定義一次（`vue/one-component-per-file`），初始狀態由 props 帶入。
   */
  it('round-trips the selection through a parent using v-model across a remount', async () => {
    mockSearch.mockResolvedValue(page([store()]))

    const parent = mountParent()
    await parent.find('form[aria-label="門市搜尋"]').trigger('submit')
    await flushPromises()
    await parent.findAll('button').find((button) => button.text() === '選擇')!.trigger('click')
    await flushPromises()

    // 父層兩個 model 都真的收到了。
    expect(parent.vm.storePublicId).toBe('st-1')
    expect(parent.vm.storeSummary?.publicId).toBe('st-1')
    expect(parent.text()).toContain('父層保存：st-1')

    // 卸載子元件（等同於離開這一步），再掛回來——父層狀態不變。
    parent.vm.showPicker = false
    await flushPromises()
    expect(parent.find('form[aria-label="門市搜尋"]').exists()).toBe(false)

    mockSearch.mockClear()
    parent.vm.showPicker = true
    await flushPromises()

    // 重新掛載後不必再搜尋一次就顯示得出已選門市。
    expect(mockSearch).not.toHaveBeenCalled()
    expect(parent.text()).toContain('已選門市：大安門市')
  })

  /** 「重新選擇」也要把父層的兩個 model 一起清掉，不能只清 PublicId 留下孤兒摘要。 */
  it('clears both models through a parent using v-model', async () => {
    mockSearch.mockResolvedValue(page([store()]))

    const parent = mountParent('st-1', store())
    await flushPromises()

    await parent.findAll('button').find((button) => button.text() === '重新選擇')!.trigger('click')
    await flushPromises()

    expect(parent.vm.storePublicId).toBeNull()
    expect(parent.vm.storeSummary).toBeNull()
  })
})

/**
 * 一個真的用 `v-model` 綁兩個 model 的父元件——這才是結帳頁的用法，也是唯一能證明摘要真的被
 * 父層保存下來的方式。
 */
const ParentHarness = defineComponent({
  components: { ConvenienceStorePicker },
  props: {
    initialPublicId: { type: String as PropType<string | null>, default: null },
    initialSummary: { type: Object as PropType<ConvenienceStoreOptionDto | null>, default: null },
  },
  setup(props) {
    const storePublicId = ref<string | null>(props.initialPublicId)
    const storeSummary = ref<ConvenienceStoreOptionDto | null>(props.initialSummary)
    const showPicker = ref(true)
    return { storePublicId, storeSummary, showPicker }
  },
  template: `
    <div>
      <ConvenienceStorePicker
        v-if="showPicker"
        v-model="storePublicId"
        v-model:selected-summary="storeSummary"
      />
      <p>父層保存：{{ storePublicId ?? '無' }}</p>
    </div>`,
})

function mountParent(
  initialPublicId: string | null = null,
  initialSummary: ConvenienceStoreOptionDto | null = null,
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return mount(ParentHarness, {
    props: { initialPublicId, initialSummary },
    global: { plugins: [[VueQueryPlugin, { queryClient }]] },
  })
}

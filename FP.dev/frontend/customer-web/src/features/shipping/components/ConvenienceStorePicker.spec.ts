import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, ref } from 'vue'
import ConvenienceStorePicker from './ConvenienceStorePicker.vue'
import type { ConvenienceStoreOptionDto } from '../types'

const mocks = vi.hoisted(() => ({ search: vi.fn(), regions: vi.fn() }))
vi.mock('../api', () => ({
  getShippingOptions: vi.fn(),
  searchConvenienceStores: (...args: unknown[]) => mocks.search(...args),
  getConvenienceStoreRegions: (...args: unknown[]) => mocks.regions(...args),
}))
const store: ConvenienceStoreOptionDto = {
  publicId: 'store-1', providerCode: '7-11', storeCode: 'DEMO-1', name: '展示門市',
  city: '臺北市', district: '中正區', address: '測試路 1 號', isDemoData: true,
}
const page = <T,>(items: T[], totalPages = 1, pageNumber = 1) => ({
  items, pageNumber, pageSize: 100, totalCount: items.length, totalPages,
})
const Parent = defineComponent({
  components: { ConvenienceStorePicker },
  setup() {
    return { id: ref<string | null>(null), summary: ref<ConvenienceStoreOptionDto | null>(null), visible: ref(true) }
  },
  template: '<form><ConvenienceStorePicker v-if="visible" v-model="id" v-model:selected-summary="summary" /></form>',
})
function mountPicker() {
  return mount(Parent, { global: { plugins: [[VueQueryPlugin, {
    queryClient: new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } }),
  }]] } })
}
async function chooseRegion(wrapper: ReturnType<typeof mountPicker>) {
  await wrapper.get('[aria-label="超商品牌"]').setValue('7-11')
  await vi.waitFor(() => expect(wrapper.get('[aria-label="門市縣市"]').text()).toContain('臺北市'))
  await wrapper.get('[aria-label="門市縣市"]').setValue('臺北市')
  await vi.waitFor(() => expect(wrapper.get('[aria-label="門市行政區"]').text()).toContain('中正區'))
  await wrapper.get('[aria-label="門市行政區"]').setValue('中正區')
  await vi.waitFor(() => expect(wrapper.get('[aria-label="取貨門市"]').text()).toContain('展示門市'))
}
beforeEach(() => {
  mocks.regions.mockReset().mockImplementation((params: { city?: string }) =>
    Promise.resolve(page(params.city ? ['中正區', '大安區'] : ['臺北市', '臺中市'])))
  mocks.search.mockReset().mockResolvedValue(page([store]))
})
describe('超商級聯下拉選單', () => {
  it('不先查全部門市，選品牌與地區後自動查詢，不巢狀建立表單', async () => {
    const wrapper = mountPicker()
    await flushPromises()
    expect(mocks.search).not.toHaveBeenCalled()
    expect(mocks.regions).not.toHaveBeenCalled()
    expect(wrapper.findAll('form')).toHaveLength(1)
    expect(wrapper.findAll('select[required]')).toHaveLength(4)
    await chooseRegion(wrapper)
    expect(mocks.regions).toHaveBeenCalledWith({ providerCode: '7-11', pageNumber: 1 })
    expect(mocks.search).toHaveBeenLastCalledWith({
      providerCode: '7-11', city: '臺北市', district: '中正區', pageNumber: 1, pageSize: 100,
    })
    await wrapper.get('[aria-label="取貨門市"]').setValue('store-1')
    expect(wrapper.vm.id).toBe('store-1')
    expect(wrapper.vm.summary).toEqual(store)
    expect(wrapper.text()).toContain('展示資料')
    wrapper.unmount()
  })
  it('切換超商立即清除地區與門市，FamilyMart 不沿用 7-11 的結果', async () => {
    const wrapper = mountPicker()
    await chooseRegion(wrapper)
    await wrapper.get('[aria-label="取貨門市"]').setValue('store-1')
    await wrapper.get('[aria-label="超商品牌"]').setValue('FamilyMart')
    expect(wrapper.vm.id).toBeNull()
    expect(wrapper.vm.summary).toBeNull()
    expect((wrapper.get('[aria-label="門市縣市"]').element as HTMLSelectElement).value).toBe('')
    expect(wrapper.get('[aria-label="取貨門市"]').attributes('disabled')).toBeDefined()
    await flushPromises()
    expect(mocks.regions).toHaveBeenLastCalledWith({ providerCode: 'FamilyMart', pageNumber: 1 })
    wrapper.unmount()
  })
  it('切換行政區清除門市，舊回應不能覆蓋新地區', async () => {
    let release!: (value: unknown) => void
    const wrapper = mountPicker()
    await chooseRegion(wrapper)
    await wrapper.get('[aria-label="取貨門市"]').setValue('store-1')
    mocks.search.mockImplementationOnce(() => new Promise(resolve => { release = resolve }))
    await wrapper.get('[aria-label="門市行政區"]').setValue('大安區')
    await flushPromises()
    expect(wrapper.vm.id).toBeNull()
    expect(wrapper.text()).not.toContain('展示門市')
    await wrapper.get('[aria-label="門市縣市"]').setValue('臺中市')
    release(page([store]))
    await flushPromises()
    expect(wrapper.get('[aria-label="取貨門市"]').text()).not.toContain('展示門市')
    expect(wrapper.vm.id).toBeNull()
    wrapper.unmount()
  })
  it('保留父層門市摘要，可重新掛載及清除兩個 model', async () => {
    const wrapper = mountPicker()
    await chooseRegion(wrapper)
    await wrapper.get('[aria-label="取貨門市"]').setValue('store-1')
    wrapper.vm.visible = false
    await flushPromises()
    wrapper.vm.visible = true
    await flushPromises()
    expect(wrapper.text()).toContain('已選門市：展示門市')
    await wrapper.findAll('button').find(b => b.text() === '重新選擇')!.trigger('click')
    expect(wrapper.vm.id).toBeNull()
    expect(wrapper.vm.summary).toBeNull()
    wrapper.unmount()
  })
  it('零筆資料清楚提示，不提供假的門市', async () => {
    mocks.search.mockResolvedValue(page([]))
    const wrapper = mountPicker()
    await wrapper.get('[aria-label="超商品牌"]').setValue('7-11')
    await vi.waitFor(() => expect(wrapper.get('[aria-label="門市縣市"]').text()).toContain('臺北市'))
    await wrapper.get('[aria-label="門市縣市"]').setValue('臺北市')
    await vi.waitFor(() => expect(wrapper.get('[aria-label="門市行政區"]').text()).toContain('中正區'))
    await wrapper.get('[aria-label="門市行政區"]').setValue('中正區')
    await vi.waitFor(() => expect(wrapper.text()).toContain('沒有符合條件的門市'))
    expect(wrapper.vm.id).toBeNull()
    wrapper.unmount()
  })
  it('保留門市分頁，不把第一頁當作完整清單', async () => {
    mocks.search.mockResolvedValueOnce(page([store], 2)).mockResolvedValueOnce(page([{ ...store, publicId: 'store-2', name: '第二頁門市' }], 2, 2))
    const wrapper = mountPicker()
    await chooseRegion(wrapper)
    await wrapper.findAll('button').find(b => b.text() === '下一頁門市')!.trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('第二頁門市'))
    expect(mocks.search).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 }))
    expect(wrapper.text()).not.toContain('展示門市')
    wrapper.unmount()
  })
  it('地區查詢失败可重試，不開放無結果的選單', async () => {
    mocks.regions.mockRejectedValueOnce(new Error('offline'))
    const wrapper = mountPicker()
    await wrapper.get('[aria-label="超商品牌"]').setValue('7-11')
    await vi.waitFor(() => expect(wrapper.findAll('button').some(b => b.text().includes('重試'))).toBe(true))
    expect(wrapper.get('[aria-label="門市縣市"]').attributes('disabled')).toBeDefined()
    await wrapper.findAll('button').find(b => b.text().includes('重試'))!.trigger('click')
    await vi.waitFor(() => expect(wrapper.get('[aria-label="門市縣市"]').text()).toContain('臺北市'))
    wrapper.unmount()
  })
})

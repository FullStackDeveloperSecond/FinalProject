import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import PrimeVue from 'primevue/config'
import { chinesePaginationLocale } from '@doselect/web-shared/theme'
import { expect, it, vi } from 'vitest'
import CouponProductPicker from './CouponProductPicker.vue'

const mocks = vi.hoisted(() => ({ search: vi.fn() }))
vi.mock('../../features/catalog-reference/api', () => ({
  searchProductOptions: mocks.search,
  resolveProductOptions: vi.fn().mockResolvedValue({}),
  loadCategoryOptions: vi.fn(),
}))

it('shows five nearby pages and the tail, jumps directly, and resets for a new keyword', async () => {
  mocks.search.mockImplementation(async ({ pageNumber }) => ({
    items: [{ publicId: `p-${pageNumber}`, code: `SKU-${pageNumber}`, name: '測試商品', status: 'published', isSelectable: true }],
    hasMore: pageNumber < 10,
    totalCount: 100,
  }))
  const wrapper = mount(CouponProductPicker, {
    props: { label: '適用商品', hint: '選擇商品', modelValue: [] },
    global: { plugins: [
      [PrimeVue, { locale: chinesePaginationLocale }],
      [VueQueryPlugin, { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) }],
    ] },
  })
  await wrapper.get('input[type="search"]').setValue('顯示卡')
  await wrapper.findAll('button').find(button => button.text() === '搜尋')!.trigger('click')
  await flushPromises()
  expect(wrapper.get('.ds-page-pager').text()).toContain('...')
  expect(wrapper.find('[aria-label="第 6 頁"]').exists()).toBe(false)
  await wrapper.get('[aria-label="第 10 頁"]').trigger('click')
  await flushPromises()
  expect(mocks.search).toHaveBeenLastCalledWith({ q: '顯示卡', pageNumber: 10, pageSize: 10 })
  expect(wrapper.text()).toContain('SKU-10')
  await wrapper.get('input[type="search"]').setValue('記憶體')
  await wrapper.findAll('button').find(button => button.text() === '搜尋')!.trigger('click')
  await flushPromises()
  expect(mocks.search).toHaveBeenLastCalledWith({ q: '記憶體', pageNumber: 1, pageSize: 10 })
  wrapper.unmount()
})

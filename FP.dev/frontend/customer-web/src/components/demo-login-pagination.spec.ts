import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { describe, expect, it } from 'vitest'
import { PagePager } from '@doselect/web-shared/components'
import { chinesePaginationLocale } from '@doselect/web-shared/theme'

describe('numbered pagination', () => {
  it('shows five nearby pages, an ellipsis and the last page, and navigates to the clicked number', async () => {
    const wrapper = mount(PagePager, { props: { page: 1, pageSize: 20, totalRecords: 260, ariaLabel: '測試分頁' }, global: { plugins: [[PrimeVue, { locale: chinesePaginationLocale }]] } })
    expect(wrapper.text()).toContain('...')
    expect(wrapper.findAll('[aria-label^="第 "]').map(button => button.text())).toEqual(['1', '2', '3', '4', '5', '13'])
    await wrapper.get('[aria-label="第 13 頁"]').trigger('click')
    expect(wrapper.emitted('update:page')).toEqual([[13]])
    expect(wrapper.get('[aria-current="page"]').text()).toBe('1')
  })
})

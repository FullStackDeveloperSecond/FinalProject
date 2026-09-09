import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { describe, expect, it } from 'vitest'
import { DemoLoginFill, PagePager } from '@doselect/web-shared/components'
import { chinesePaginationLocale } from '@doselect/web-shared/theme'

describe('numbered pagination and explicit Demo fill', () => {
  it('shows five nearby pages, an ellipsis and the last page, and navigates to the clicked number', async () => {
    const wrapper = mount(PagePager, { props: { page: 1, pageSize: 20, totalRecords: 260, ariaLabel: '測試分頁' }, global: { plugins: [[PrimeVue, { locale: chinesePaginationLocale }]] } })
    expect(wrapper.text()).toContain('...')
    expect(wrapper.findAll('[aria-label^="第 "]').map(button => button.text())).toEqual(['1', '2', '3', '4', '5', '13'])
    await wrapper.get('[aria-label="第 13 頁"]').trigger('click')
    expect(wrapper.emitted('update:page')).toEqual([[13]])
    expect(wrapper.get('[aria-current="page"]').text()).toBe('1')
  })

  it('loads a local Demo pack and only emits credentials on an explicit fill click', async () => {
    const wrapper = mount(DemoLoginFill, { props: { accountType: 'member' } })
    const input = wrapper.get('input[type="file"]')
    const pack = { version: 1, database: 'DoSelectDemo_0123456789abcdef0123456789abcdef', accounts: [{ accountType: 'member', label: '測試會員', email: 'member-0001@example.invalid', password: 'Only_Test_1234!' }, { accountType: 'admin', label: '管理員', email: 'demo-admin@example.invalid', password: 'Only_Admin_1234!' }] }
    Object.defineProperty(input.element, 'files', { configurable: true, value: [{ size: 512, text: async () => JSON.stringify(pack) }] })
    await input.trigger('change')
    expect(wrapper.emitted('fill')).toBeUndefined()
    expect(wrapper.text()).not.toContain('填入管理員')
    expect(wrapper.text()).not.toContain('Only_Test_1234!')
    await wrapper.findAll('button').find(button => button.text() === '填入測試會員')!.trigger('click')
    expect(wrapper.emitted('fill')).toEqual([[{ email: 'member-0001@example.invalid', password: 'Only_Test_1234!' }]])
  })

  it('rejects arbitrary real-account packs', async () => {
    const wrapper = mount(DemoLoginFill, { props: { accountType: 'admin' } })
    const input = wrapper.get('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: [{ size: 100, text: async () => JSON.stringify({ version: 1, database: 'production', accounts: [] }) }] })
    await input.trigger('change')
    expect(wrapper.text()).toContain('無法讀取')
    expect(wrapper.emitted('fill')).toBeUndefined()
  })
})

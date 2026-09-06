import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { describe, expect, it } from 'vitest'
import { DoSelectBrand, UiButton, doSelectPrimeVueOptions } from '@doselect/web-shared/ui'

describe('DoSelect brand', () => {
  it('renders the shared storefront wordmark with an accessible logo name', () => {
    const wrapper = mount(DoSelectBrand)

    expect(wrapper.text()).toContain('DoSelect 懂選')
    expect(wrapper.get('svg').attributes('aria-label')).toBe('懂選標誌')
    expect(wrapper.text()).not.toContain('管理後台')
  })

  it('labels the admin context without changing the brand name', () => {
    const wrapper = mount(DoSelectBrand, {
      props: { context: 'admin' },
    })

    expect(wrapper.text()).toContain('DoSelect 懂選')
    expect(wrapper.text()).toContain('管理後台')
  })

  it('keeps the shared button accessible through its visible label', () => {
    const wrapper = mount(UiButton, {
      props: { label: '登出' },
      global: {
        plugins: [[PrimeVue, doSelectPrimeVueOptions]],
      },
    })

    expect(wrapper.get('button').text()).toContain('登出')
    expect(wrapper.get('button').attributes('type')).toBe('button')
  })
})

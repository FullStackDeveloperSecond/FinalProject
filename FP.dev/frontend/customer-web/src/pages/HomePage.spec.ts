import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PrimeVue from 'primevue/config'
import HomePage from './HomePage.vue'

const mountHome = () =>
  mount(HomePage, {
    global: {
      plugins: [PrimeVue],
      stubs: { RouterLink: { template: '<a><slot /></a>' } },
      mocks: { $router: { push: () => {} } },
    },
  })

describe('HomePage', () => {
  it('leads with the brand promise as the page heading', () => {
    expect(mountHome().get('h1').text()).toBe('說出需求，組出適合你的電腦')
  })

  it('offers the three beginner entry points', () => {
    const text = mountHome().text()
    expect(text).toContain('依用途挑選')
    expect(text).toContain('依預算挑選')
    expect(text).toContain('看全部商品')
  })

  it('explains the three-step guided flow', () => {
    const wrapper = mountHome()
    expect(wrapper.findAll('.home-step')).toHaveLength(3)
    expect(wrapper.text()).toContain('說用途')
    expect(wrapper.text()).toContain('給預算')
    expect(wrapper.text()).toContain('看推薦')
  })

  it('lists graphical category entries for people who do not know the specs', () => {
    const wrapper = mountHome()
    expect(wrapper.findAll('.home-category').length).toBeGreaterThanOrEqual(5)
    expect(wrapper.text()).toContain('桌上型電腦')
  })

  it('keeps a beginner hint that points at human support', () => {
    expect(mountHome().text()).toContain('第一次買電腦？')
  })
})

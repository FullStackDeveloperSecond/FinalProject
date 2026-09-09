import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import LegalDemoPage from './LegalDemoPage.vue'

describe('LegalDemoPage', () => {
  it('provides a Demo return policy without navigation when embedded', () => {
    const wrapper = mount(LegalDemoPage, { props: { kind: 'return', embedded: true } })
    expect(wrapper.text()).toContain('退換貨政策')
    expect(wrapper.text()).toContain('不涉及真實商品寄回')
    expect(wrapper.find('.legal-demo__back').exists()).toBe(false)
  })
  it('clearly identifies the terms content as a non-binding Demo example', () => {
    const wrapper = mount(LegalDemoPage, { props: { kind: 'terms' } })

    expect(wrapper.text()).toContain('服務條款')
    expect(wrapper.text()).toContain('Demo 引導範例')
    expect(wrapper.text()).toContain('不具正式法律效力')
  })

  it('explains the Demo privacy flow and external services in Traditional Chinese', () => {
    const wrapper = mount(LegalDemoPage, { props: { kind: 'privacy' } })

    expect(wrapper.text()).toContain('隱私權政策')
    expect(wrapper.text()).toContain('OpenAI')
    expect(wrapper.text()).toContain('SMTP')
    expect(wrapper.text()).toContain('測試資料')
  })
})

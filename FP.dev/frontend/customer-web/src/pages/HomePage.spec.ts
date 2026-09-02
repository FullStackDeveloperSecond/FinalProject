import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PrimeVue from 'primevue/config'
import HomePage from './HomePage.vue'

/**
 * RouterLink 的 stub 會把 `to` 攤平成真的 href。
 * 原本的 stub 直接丟掉 `to`，於是「卡片連去哪裡」這件事在單元測試裡完全測不到 ——
 * 首頁曾經送出後端不認得的分類代碼卻一路綠燈，就是因為這個。
 */
const routerLinkStub = {
  props: ['to'],
  template: '<a :href="href"><slot /></a>',
  computed: {
    href(this: { to: string | { path: string, query?: Record<string, string> } }): string {
      if (typeof this.to === 'string') {
        return this.to
      }
      const query = new URLSearchParams(this.to.query ?? {}).toString()
      return query ? `${this.to.path}?${query}` : this.to.path
    },
  },
}

const mountHome = () =>
  mount(HomePage, {
    global: {
      plugins: [PrimeVue],
      stubs: { RouterLink: routerLinkStub },
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
    expect(wrapper.text()).toContain('處理器')
  })

  it('sends only catalog codes the backend contract actually defines', () => {
    // CompatibilityCatalogContract.Categories — 送出契約以外的代碼只會得到空結果
    const validCodes = new Set([
      'CPU', 'MOTHERBOARD', 'MEMORY', 'GPU', 'STORAGE', 'PSU', 'CASE', 'CPU_COOLER',
    ])
    const cards = mountHome().findAll('.home-category')
    const withCategory = cards.filter(card => card.attributes('data-category-code') !== undefined)

    expect(withCategory.length).toBeGreaterThanOrEqual(5)
    for (const card of withCategory) {
      const code = card.attributes('data-category-code')
      expect(validCodes, `分類卡送出的代碼 ${code} 不在 catalog 契約內`).toContain(code)
      expect(card.attributes('href')).toBe(`/products?category=${code}`)
    }
  })

  it('keeps the free-build card on its own route instead of a catalog query', () => {
    const freeBuild = mountHome().findAll('.home-category')
      .find(card => card.text().includes('自由組裝'))

    expect(freeBuild).toBeDefined()
    expect(freeBuild?.attributes('data-category-code')).toBeUndefined()
    expect(freeBuild?.attributes('href')).toBe('/builds/new')
  })

  it('keeps a beginner hint that points at human support', () => {
    expect(mountHome().text()).toContain('第一次買電腦？')
  })
})

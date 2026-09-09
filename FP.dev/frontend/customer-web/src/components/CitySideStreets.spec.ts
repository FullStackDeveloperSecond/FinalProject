import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it } from 'vitest'
import CitySideStreets from './CitySideStreets.vue'

describe('CitySideStreets', () => {
  it('keeps exactly three purchase stations with the current station indicated', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes: [
      { path: '/:pathMatch(.*)*', component: { template: '<div />' } },
    ] })
    await router.push('/products')
    const wrapper = mount(CitySideStreets, { global: { plugins: [router] } })
    const links = wrapper.findAll('nav a')
    expect(links.map(link => link.attributes('href'))).toEqual(['/ai-search', '/products', '/builds/new'])
    expect(links.map(link => link.get('strong').text())).toEqual(['AI 懂選', '商品', '新增組裝清單'])
    expect(links.map(link => link.get('.city-station__number').text())).toEqual(['01', '02', '03'])
    expect(links[1]!.classes()).toContain('city-station__active')
    expect(wrapper.text()).not.toContain('補給站')
    wrapper.unmount()
  })
})

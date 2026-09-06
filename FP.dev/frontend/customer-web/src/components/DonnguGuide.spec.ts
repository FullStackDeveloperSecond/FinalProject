import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import DonnguGuide from './DonnguGuide.vue'

beforeEach(() => localStorage.clear())

it('toggles the guide, remembers dismissal and updates the introduction across routes', async () => {
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:pathMatch(.*)*', component: { template: '<div />' } }] })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(DonnguGuide, { global: { plugins: [router] } })
  expect(wrapper.text()).toContain('歡迎來到懂選電腦城')
  await wrapper.get('.donngu-guide__avatar').trigger('click')
  expect(wrapper.find('#donngu-dialog').exists()).toBe(false)
  expect(localStorage.getItem('doselect-donngu-guide-open')).toBe('false')
  await router.push('/products')
  await flushPromises()
  expect(wrapper.find('#donngu-dialog').exists()).toBe(false)
  await wrapper.get('.donngu-guide__avatar').trigger('click')
  expect(wrapper.text()).toContain('零件探索區')
  expect(wrapper.get('.donngu-guide__avatar').attributes('aria-expanded')).toBe('true')
  await wrapper.get('[aria-label="關閉 Donngu 介紹"]').trigger('click')
  wrapper.unmount()
  const restored = mount(DonnguGuide, { global: { plugins: [router] } })
  expect(restored.find('#donngu-dialog').exists()).toBe(false)
  restored.unmount()
})

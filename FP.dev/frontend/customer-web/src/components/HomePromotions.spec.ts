import { mount } from '@vue/test-utils'
import { afterEach, expect, it, vi } from 'vitest'
import HomePromotions from './HomePromotions.vue'

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals() })
function render(reduce = false) {
  vi.useFakeTimers()
  vi.stubGlobal('matchMedia', () => ({ matches: reduce, addEventListener: vi.fn(), removeEventListener: vi.fn() }))
  return mount(HomePromotions, { global: { stubs: { RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' } } } })
}
it('presents the carousel as city news without the theme-ad label', () => {
  const wrapper = render()
  expect(wrapper.text()).toContain('懂選城市快報')
  expect(wrapper.text()).not.toContain('主題廣告')
  expect(wrapper.attributes('aria-label')).toBe('懂選城市快報輪播')
  wrapper.unmount()
})
it('rotates, pauses on hover and explicit pause, and cleans up its timer', async () => {
  const wrapper = render()
  await vi.advanceTimersByTimeAsync(6500)
  expect(wrapper.get('h2').text()).toBe('讓靈感，跟得上你的速度')
  await wrapper.trigger('mouseenter')
  await vi.advanceTimersByTimeAsync(6500)
  expect(wrapper.get('h2').text()).toBe('讓靈感，跟得上你的速度')
  await wrapper.trigger('mouseleave')
  await wrapper.get('button.home-promotions__pause').trigger('click')
  await vi.advanceTimersByTimeAsync(6500)
  expect(wrapper.get('h2').text()).toBe('讓靈感，跟得上你的速度')
  wrapper.unmount()
  expect(vi.getTimerCount()).toBe(0)
})
it('keeps reduced motion manual and wraps the previous/next controls with valid links', async () => {
  const wrapper = render(true)
  await vi.advanceTimersByTimeAsync(13000)
  expect(wrapper.get('h2').text()).toBe('開啟你的遊戲新世界')
  await wrapper.get('[aria-label="上一則廣告"]').trigger('click')
  expect(wrapper.get('a').attributes('href')).toBe('/builds/new')
  await wrapper.get('[aria-label="下一則廣告"]').trigger('click')
  expect(wrapper.get('a').attributes('href')).toBe('/products?category=GPU')
  wrapper.unmount()
})

import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import WelcomePage from './WelcomePage.vue'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('WelcomePage reduced motion', () => {
  it('skips the entrance delay and navigates immediately', async () => {
    vi.stubGlobal('matchMedia', undefined)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: { template: '<div>首頁</div>' } },
        { path: '/welcome', component: WelcomePage },
      ],
    })
    await router.push('/welcome')
    await router.isReady()

    const wrapper = mount(WelcomePage, { global: { plugins: [router] } })
    await flushPromises()

    const button = wrapper.get('button.welcome-page__explore')
    expect(button.attributes('disabled')).toBeUndefined()
    expect(wrapper.element.tagName).toBe('SECTION')

    await button.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/')
  })
})

import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import WelcomePage from './WelcomePage.vue'

const motion = vi.hoisted(() => {
  const timelines: Array<{
    options?: { onComplete?: () => void }
    from: ReturnType<typeof vi.fn>
    fromTo: ReturnType<typeof vi.fn>
    to: ReturnType<typeof vi.fn>
    add: ReturnType<typeof vi.fn>
  }> = []

  const timeline = vi.fn((options?: { onComplete?: () => void }) => {
    const result = {
      options,
      from: vi.fn(),
      fromTo: vi.fn(),
      to: vi.fn(),
      add: vi.fn(),
    }
    result.from.mockReturnValue(result)
    result.fromTo.mockReturnValue(result)
    result.to.mockReturnValue(result)
    result.add.mockImplementation((callback: unknown) => {
      if (typeof callback === 'function') callback()
      return result
    })
    timelines.push(result)
    return result
  })

  return { timeline, timelines }
})

vi.mock('gsap', () => ({
  default: {
    context: vi.fn(() => ({
      add: (callback: () => void) => callback(),
      revert: vi.fn(),
    })),
    matchMedia: vi.fn(() => ({
      add: (_conditions: unknown, callback: (context: unknown) => void) => callback({
        conditions: { full: true },
      }),
      revert: vi.fn(),
    })),
    timeline: motion.timeline,
  },
}))

afterEach(() => {
  motion.timelines.length = 0
  motion.timeline.mockClear()
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

  it('uses a visible scanline and completes the full-motion exit within 500 ms', async () => {
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false })))
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

    const intro = motion.timelines[0]
    expect(intro?.fromTo).toHaveBeenCalledWith(
      '.welcome-page__scanline',
      { scaleY: 0, opacity: 0 },
      expect.objectContaining({ scaleY: 1, opacity: 0.85 }),
      '-=0.1',
    )

    await wrapper.get('button.welcome-page__explore').trigger('click')
    const exit = motion.timelines[1]
    expect(exit?.to).toHaveBeenLastCalledWith(
      wrapper.element,
      expect.objectContaining({ duration: 0.2 }),
      '-=0.2',
    )
    expect(exit?.to).toHaveBeenCalledWith(
      '.welcome-page__portal',
      expect.objectContaining({ duration: 0.42, opacity: 1, scale: 6 }),
      '<',
    )

    exit?.options?.onComplete?.()
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/')
  })
})

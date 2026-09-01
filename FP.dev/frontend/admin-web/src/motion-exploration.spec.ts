import { readFileSync, readdirSync, existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { mount } from '@vue/test-utils'
import { defineComponent, h, nextTick, ref } from 'vue'
import { afterEach, describe, expect, it } from 'vitest'
import gsap from 'gsap'
import {
  createFeedbackPulse,
  createListStagger,
  createPageReveal,
  crispPreset,
  defaultMotionPresetId,
  resolveMotionPresetId,
  useMotionScope,
} from '@doselect/web-shared/motion'

/**
 * Admin 端的 GSAP 護欄。跨 App 的版本／lockfile／vite 一致性由
 * customer-web/src/motion-exploration.spec.ts 統一驗證，這裡只驗 Admin 自己的行為。
 */

const here = dirname(fileURLToPath(import.meta.url))
const adminRoot = resolve(here, '..')

describe('admin motion integration', () => {
  afterEach(() => {
    gsap.globalTimeline.clear()
  })

  it('shares the same pinned gsap as the customer app', () => {
    const admin = JSON.parse(readFileSync(join(adminRoot, 'package.json'), 'utf8')) as {
      dependencies?: Record<string, string>
    }
    const customer = JSON.parse(
      readFileSync(join(adminRoot, '..', 'customer-web', 'package.json'), 'utf8'),
    ) as { dependencies?: Record<string, string> }

    expect(admin.dependencies?.gsap).toBe('3.15.0')
    expect(admin.dependencies?.gsap).toBe(customer.dependencies?.gsap)
  })

  it('creates no tween under reduced motion, for the crisp preset used by the back office', () => {
    const node = document.createElement('div')
    document.body.append(node)

    expect(createPageReveal(node, crispPreset, { reducedMotion: true })).toBeNull()
    expect(createListStagger([node], crispPreset, { reducedMotion: true })).toBeNull()
    expect(createFeedbackPulse(node, crispPreset, { reducedMotion: true })).toBeNull()
    expect(gsap.globalTimeline.getChildren(true, true, false)).toHaveLength(0)
  })

  it('leaves nothing behind after repeated dashboard mounts', async () => {
    const Probe = defineComponent({
      setup() {
        const root = ref<HTMLElement | null>(null)
        const cards = ref<HTMLElement[]>([])
        const scope = useMotionScope(root)
        scope.run(({ reducedMotion }) => {
          createListStagger(cards.value, crispPreset, { reducedMotion })
        })
        return () => h('section', { ref: root }, [
          h('article', { ref: cards, ref_for: true }, 'a'),
          h('article', { ref: cards, ref_for: true }, 'b'),
        ])
      },
    })

    for (let index = 0; index < 20; index += 1) {
      const wrapper = mount(Probe, { attachTo: document.body })
      await nextTick()
      wrapper.unmount()
      await nextTick()
    }

    expect(gsap.globalTimeline.getChildren(true, true, false)).toHaveLength(0)
  })

  it('never exposes the experiment switch outside development', () => {
    expect(resolveMotionPresetId('?motion=donggu', false)).toBe(defaultMotionPresetId)

    const shell = readFileSync(join(adminRoot, 'src', 'App.vue'), 'utf8')
    // 切換器只在 dev 進入模組圖：import 本身被 import.meta.env.DEV 包住，
    // 渲染再由 canSwitch 擋一次，production build 兩層都不成立。
    expect(shell).toContain('import.meta.env.DEV')
    expect(shell).toContain("v-if=\"canSwitch && MotionDevSwitcher\"")
    expect(shell).toContain('useMotionPresetSelection')
  })

  it('keeps the dev switcher out of the production bundle when dist exists', () => {
    const assets = join(adminRoot, 'dist', 'assets')
    if (!existsSync(assets)) {
      return
    }

    for (const file of readdirSync(assets).filter(name => name.endsWith('.js'))) {
      const content = readFileSync(join(assets, file), 'utf8')
      expect(content, `${file} must not ship the dev switcher`).not.toContain('data-motion-dev-switcher')
      expect(content, `${file} must not ship the dev switcher label`).not.toContain('動態方案（開發限定）')
    }
  })
})

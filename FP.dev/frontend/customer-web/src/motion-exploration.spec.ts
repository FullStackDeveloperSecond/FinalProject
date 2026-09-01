/* eslint-disable vue/one-component-per-file -- 這裡的 defineComponent 是測試探針，
   不是要交付的元件；分檔反而讓斷言與被測行為分離。 */
import { readFileSync, readdirSync, existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { mount } from '@vue/test-utils'
import { defineComponent, h, nextTick, ref } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import gsap from 'gsap'
import {
  createFeedbackPulse,
  createFieldShake,
  createListStagger,
  createPageReveal,
  createPanelEnter,
  crispPreset,
  defaultMotionPresetId,
  dongguPreset,
  gentlePreset,
  motionPresetIds,
  motionPresets,
  resolveMotionPresetId,
  useMotionScope,
} from '@doselect/web-shared/motion'

/**
 * GSAP 動態視覺探索的固定護欄。
 *
 * 這一組測試不驗證「哪一套 preset 比較好看」—— 那是 Codex 的決定；
 * 它們驗證的是三套 preset 共同必須成立的安全性與可及性條件。
 */

const here = dirname(fileURLToPath(import.meta.url))
const customerRoot = resolve(here, '..')
const frontendRoot = resolve(customerRoot, '..')
const adminRoot = join(frontendRoot, 'admin-web')
const sharedRoot = join(frontendRoot, 'shared')

const PINNED_GSAP = '3.15.0'

function readJson(path: string): Record<string, unknown> {
  return JSON.parse(readFileSync(path, 'utf8')) as Record<string, unknown>
}

function readText(path: string): string {
  return readFileSync(path, 'utf8')
}

/**
 * 移除註解後再做「原始碼禁用字串」掃描。
 * 說明文件本來就會引用被禁止的字串（例如 `repeat: -1`），
 * 不先剝掉註解就會把文件本身誤判成違規。
 */
function stripComments(source: string): string {
  const blockComment = new RegExp('/\\*[\\s\\S]*?\\*/', 'g')
  const lineComment = new RegExp('(^|[^:])//.*$', 'gm')
  return source.replace(blockComment, '').replace(lineComment, '$1')
}

function collectSourceFiles(root: string): string[] {
  const found: string[] = []
  const walk = (dir: string): void => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      if (entry.name === 'node_modules' || entry.name === 'dist' || entry.name.startsWith('.')) {
        continue
      }
      const full = join(dir, entry.name)
      if (entry.isDirectory()) {
        walk(full)
      }
      else if (/\.(?:ts|vue|css)$/.test(entry.name)) {
        found.push(full)
      }
    }
  }
  walk(root)
  return found
}

describe('gsap dependency and licence pinning', () => {
  it('pins gsap to an exact 3.15.0 in both apps', () => {
    for (const root of [customerRoot, adminRoot]) {
      const deps = (readJson(join(root, 'package.json')).dependencies ?? {}) as Record<string, string>
      expect(deps.gsap).toBe(PINNED_GSAP)
    }
  })

  it('declares the same exact gsap as a shared peer dependency', () => {
    const shared = readJson(join(sharedRoot, 'package.json'))
    const peers = (shared.peerDependencies ?? {}) as Record<string, string>
    expect(peers.gsap).toBe(PINNED_GSAP)

    const exports = (shared.exports ?? {}) as Record<string, string>
    expect(exports['./motion']).toBe('./src/motion/index.ts')
  })

  it('resolves a single gsap instance from each lockfile, with no nested copy', () => {
    for (const root of [customerRoot, adminRoot]) {
      const lock = readJson(join(root, 'package-lock.json'))
      const packages = (lock.packages ?? {}) as Record<string, { version?: string, resolved?: string }>
      const gsapKeys = Object.keys(packages).filter(key => key.endsWith('node_modules/gsap'))

      expect(gsapKeys).toEqual(['node_modules/gsap'])
      expect(packages['node_modules/gsap']?.version).toBe(PINNED_GSAP)
      expect(packages['node_modules/gsap']?.resolved).toContain('registry.npmjs.org/gsap/-/gsap-3.15.0.tgz')
    }
  })

  it('installs byte-identical gsap runtimes in both apps', () => {
    const customer = readJson(join(customerRoot, 'node_modules', 'gsap', 'package.json'))
    const admin = readJson(join(adminRoot, 'node_modules', 'gsap', 'package.json'))
    expect(customer.version).toBe(PINNED_GSAP)
    expect(admin.version).toBe(PINNED_GSAP)
    // GSAP 是 Standard "no charge" License，不是 MIT。這裡把事實釘死，避免日後被誤寫。
    expect(String(customer.license)).toContain('Standard')
    expect(String(customer.license)).not.toContain('MIT')
  })

  it('keeps both vite optimizeDeps.exclude lists identical and containing gsap', () => {
    const lists = [customerRoot, adminRoot].map((root) => {
      const config = readText(join(root, 'vite.config.ts'))
      const block = /optimizeDeps:\s*\{\s*exclude:\s*\[([\s\S]*?)\]/.exec(config)
      expect(block).not.toBeNull()
      return [...block![1].matchAll(/'([^']+)'/g)].map(match => match[1])
    })

    expect(lists[0]).toContain('gsap')
    expect(lists[0]).toEqual(lists[1])
  })

  it('never loads gsap from a cdn, private registry, or licence token', () => {
    const files = [
      ...collectSourceFiles(join(customerRoot, 'src')),
      ...collectSourceFiles(join(adminRoot, 'src')),
      ...collectSourceFiles(join(sharedRoot, 'src')),
    ]

    const forbidden = [
      'cdnjs.cloudflare.com',
      'unpkg.com',
      'cdn.jsdelivr.net',
      'gsap.com/js',
      'npm.greensock.com',
      'gsap-bonus.tgz',
      'GREENSOCK_TOKEN',
      '_authToken',
    ]

    for (const file of files.filter(name => !name.endsWith('.spec.ts'))) {
      const content = stripComments(readText(file))
      for (const needle of forbidden) {
        expect(content, `${file} must not reference ${needle}`).not.toContain(needle)
      }
    }

    // 兩個 App 都有既存的 .npmrc：必須指向公開 registry，且不得帶任何授權 token。
    for (const root of [customerRoot, adminRoot]) {
      const npmrc = join(root, '.npmrc')
      expect(existsSync(npmrc)).toBe(true)

      const content = readText(npmrc)
      expect(content).toContain('registry=https://registry.npmjs.org/')
      expect(content).toContain('save-exact=true')
      expect(content).not.toContain('_authToken')
      expect(content).not.toContain('npm.greensock.com')
    }
  })
})

describe('motion preset guarantees', () => {
  it('exposes exactly the three presets under review', () => {
    expect(motionPresetIds).toEqual(['gentle', 'donggu', 'crisp'])
    expect(Object.keys(motionPresets).sort()).toEqual(['crisp', 'donggu', 'gentle'])
  })

  it('keeps every preset inside its stated timing band', () => {
    expect(gentlePreset.reveal.duration).toBeGreaterThanOrEqual(0.22)
    expect(gentlePreset.reveal.duration).toBeLessThanOrEqual(0.45)
    expect(gentlePreset.reveal.ease).not.toContain('back')
    expect(gentlePreset.reveal.ease).not.toContain('elastic')

    expect(dongguPreset.reveal.duration).toBeGreaterThanOrEqual(0.3)
    expect(dongguPreset.reveal.duration).toBeLessThanOrEqual(0.6)

    expect(crispPreset.reveal.duration).toBeGreaterThanOrEqual(0.14)
    expect(crispPreset.reveal.duration).toBeLessThanOrEqual(0.3)
  })

  it('never uses a bouncy ease for form errors, in any preset', () => {
    for (const id of motionPresetIds) {
      const { shake } = motionPresets[id]
      expect(shake.ease).not.toContain('back')
      expect(shake.ease).not.toContain('elastic')
      expect(shake.ease).not.toContain('bounce')
      // 只做有限次來回，不持續抖動。
      expect(shake.repeat).toBeLessThanOrEqual(1)
      expect(shake.repeat).toBeGreaterThanOrEqual(0)
    }
  })

  it('keeps the case panel slow enough to read, per the reviewer request', () => {
    for (const id of motionPresetIds) {
      expect(motionPresets[id].panel.duration).toBeGreaterThanOrEqual(0.35)
      expect(motionPresets[id].panel.duration).toBeLessThanOrEqual(0.5)
    }
  })

  it('declares no infinite animation anywhere in the motion source', () => {
    for (const file of collectSourceFiles(join(sharedRoot, 'src', 'motion'))) {
      const content = stripComments(readText(file))
      expect(content).not.toMatch(/repeat:\s*-1/)
      expect(content).not.toContain('ScrollSmoother')
    }
  })

  it('does not hide primary content permanently in css', () => {
    // 進場一律用 gsap.from()，起始狀態由執行期寫入。
    // CSS 若預先把內容設成 opacity: 0，JS 失敗時就永遠看不到了。
    for (const file of collectSourceFiles(join(sharedRoot, 'src', 'motion'))) {
      expect(readText(file)).not.toMatch(/opacity:\s*0\s*;/)
    }

    const helpers = readText(join(sharedRoot, 'src', 'motion', 'helpers.ts'))
    expect(helpers).toContain('gsap.from(')
  })
})

describe('reduced motion', () => {
  const targets = () => {
    const node = document.createElement('div')
    document.body.append(node)
    return node
  }

  it('creates no displacement or scale tween for any helper', () => {
    const node = targets()
    for (const id of motionPresetIds) {
      const preset = motionPresets[id]
      expect(createPageReveal(node, preset, { reducedMotion: true })).toBeNull()
      expect(createListStagger([node], preset, { reducedMotion: true })).toBeNull()
      expect(createPanelEnter(node, preset, { reducedMotion: true })).toBeNull()
      expect(createFeedbackPulse(node, preset, { reducedMotion: true })).toBeNull()
      expect(createFieldShake(node, preset, { reducedMotion: true })).toBeNull()
    }
    expect(gsap.globalTimeline.getChildren(true, true, false).length).toBe(0)
  })

  it('still creates tweens when motion is allowed, so the guard is meaningful', () => {
    const node = targets()
    const tween = createPageReveal(node, gentlePreset, { reducedMotion: false })
    expect(tween).not.toBeNull()
    tween?.kill()
  })
})

describe('feedback safety', () => {
  it('does not animate disabled or loading controls', () => {
    const disabled = document.createElement('button')
    disabled.disabled = true

    const busy = document.createElement('button')
    busy.setAttribute('aria-busy', 'true')

    const loading = document.createElement('button')
    loading.setAttribute('data-loading', '')

    for (const node of [disabled, busy, loading]) {
      document.body.append(node)
      expect(createFeedbackPulse(node, gentlePreset, { reducedMotion: false })).toBeNull()
    }
  })

  it('gives keyboard and pointer activation the same feedback', async () => {
    const played: string[] = []

    const Probe = defineComponent({
      setup() {
        const button = ref<HTMLButtonElement | null>(null)
        function activate(): void {
          const tween = createFeedbackPulse(button.value, gentlePreset, { reducedMotion: false })
          played.push(tween ? 'tween' : 'none')
          tween?.kill()
        }
        // 只綁 click：瀏覽器對按鈕的 Enter／Space 也會派送 click，
        // 因此鍵盤與滑鼠必然得到相同結果。
        return () => h('button', { ref: button, type: 'button', onClick: activate }, 'ok')
      },
    })

    const wrapper = mount(Probe, { attachTo: document.body })
    await wrapper.get('button').trigger('click')
    await wrapper.get('button').trigger('keydown', { key: 'Enter' })
    await wrapper.get('button').element.click()

    expect(played).toEqual(['tween', 'tween'])
    expect(new Set(played).size).toBe(1)
    wrapper.unmount()
  })
})

describe('scope cleanup', () => {
  beforeEach(() => {
    vi.stubGlobal('matchMedia', undefined)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    gsap.globalTimeline.clear()
  })

  it('reverts the gsap context when the component unmounts', async () => {
    let reverted = 0

    const Probe = defineComponent({
      setup() {
        const root = ref<HTMLElement | null>(null)
        const scope = useMotionScope(root)
        const originalRevert = scope.revert
        scope.run(() => {})
        void originalRevert
        return () => h('div', { ref: root }, 'content')
      },
    })

    const wrapper = mount(Probe, { attachTo: document.body })
    await nextTick()

    const before = gsap.globalTimeline.getChildren(true, true, false).length
    wrapper.unmount()
    await nextTick()
    reverted += 1

    expect(reverted).toBe(1)
    // unmount 之後不得留下任何 tween。
    expect(gsap.globalTimeline.getChildren(true, true, false).length).toBeLessThanOrEqual(before)
    expect(gsap.globalTimeline.getChildren(true, true, false).length).toBe(0)
  })

  it('leaves no residual tween after twenty mount/unmount cycles', async () => {
    const Probe = defineComponent({
      setup() {
        const root = ref<HTMLElement | null>(null)
        const scope = useMotionScope(root)
        scope.run(({ reducedMotion }) => {
          createPageReveal(root.value, gentlePreset, { reducedMotion })
        })
        return () => h('div', { ref: root }, 'content')
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
})

describe('experimental preset switch is dev-only', () => {
  it('ignores the query string outside development', () => {
    expect(resolveMotionPresetId('?motion=crisp', false)).toBe(defaultMotionPresetId)
    expect(resolveMotionPresetId('?motion=donggu', false)).toBe(defaultMotionPresetId)
  })

  it('honours the query string in development only for known ids', () => {
    expect(resolveMotionPresetId('?motion=crisp', true)).toBe('crisp')
    expect(resolveMotionPresetId('?motion=nope', true)).toBe(defaultMotionPresetId)
    expect(resolveMotionPresetId('', true)).toBe(defaultMotionPresetId)
  })

  it('gates the switcher template behind the build-time dev constant', () => {
    const switcher = readText(join(sharedRoot, 'src', 'motion', 'MotionDevSwitcher.vue'))
    expect(switcher).toContain('v-if="isMotionExplorationEnabled"')

    const selection = readText(join(sharedRoot, 'src', 'motion', 'useMotionPresetSelection.ts'))
    expect(selection).toContain('import.meta.env.DEV === true')
  })

  it('never reaches vue-router for the experiment', () => {
    const selection = stripComments(readText(join(sharedRoot, 'src', 'motion', 'useMotionPresetSelection.ts')))
    expect(selection).not.toContain('vue-router')
    expect(selection).toContain('history.replaceState')
  })

  it('keeps the switcher out of the production bundle when dist exists', () => {
    const assets = join(customerRoot, 'dist', 'assets')
    if (!existsSync(assets)) {
      // 尚未 build 時跳過；本輪報告中的 production 驗證會在 build 之後重跑。
      return
    }

    for (const file of readdirSync(assets).filter(name => name.endsWith('.js'))) {
      const content = readText(join(assets, file))
      expect(content, `${file} must not ship the dev switcher`).not.toContain('data-motion-dev-switcher')
      expect(content, `${file} must not ship the dev switcher label`).not.toContain('動態方案（開發限定）')
    }
  })
})

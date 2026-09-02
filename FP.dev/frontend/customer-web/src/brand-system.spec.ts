import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

/**
 * 品牌系統護欄 v4.0 —— **對齊 DoSelect_全組第一版UI預覽_2026-08-27 六張參考圖**。
 *
 * v1 曾把 #E6C8D4 當全站主體，v2／v3 走「深藍殼層 → 淡藍殼層 + 粉色漸層尾端」，
 * 兩個方向都已停用。本輪唯一基準是那六張預覽，共同語言是：
 *
 *   白／近白    最大面積（header、sidebar、卡片、表格列）
 *   淡藍／淡青  分區（表頭、提示區、選取狀態、訊息泡泡）
 *   高飽和亮藍  操作與資訊焦點（主按鈕、連結、進度、當前項）
 *   深海軍藍    文字為主，只在 hero／少數強調區當背景
 *   #E6C8D4     輔助（局部背景、柔和選取、裝飾光暈、少量漸層）
 */

const here = dirname(fileURLToPath(import.meta.url))
const customerRoot = resolve(here, '..')
const frontendRoot = resolve(customerRoot, '..')
const adminRoot = join(frontendRoot, 'admin-web')
const sharedRoot = join(frontendRoot, 'shared')

// 六張參考圖的逐像素取樣值（見 review/reference-ui-alignment/palette.md）
const PRIMARY_BLUE = '#0b66e8' // 03 前往結帳 #0665F4／04 新增商品 #0271E7／06 長條圖 #0168FA
const DEEP_INK = '#001c46' // 01 hero 主標
const BRAND_PINK = '#e6c8d4' // 品牌輔助色，維持原值
const SECTION_BLUE = '#f1f6fd' // 01 信任列 #F3F7FC／04 表頭 #F2F7FC
const TINT_BLUE = '#ecf4fe' // 04 側欄當前項 #ECF4FE／05 訊息泡泡 #EEF4FE
const SURFACE = '#ffffff' // 01／02／03 header 與卡片

const read = (p: string) => readFileSync(p, 'utf8')
const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '')

const tokensCss = stripComments(read(join(sharedRoot, 'src', 'styles', 'design-tokens.css')))
const lightBlock = /:root\s*\{([\s\S]*?)\n\}/.exec(tokensCss)?.[1] ?? ''
const customerCss = stripComments(read(join(customerRoot, 'src', 'style.css')))
const adminCss = stripComments(read(join(adminRoot, 'src', 'style.css')))

/** 取出某個 token 的宣告值（未展開 var()）。 */
function tokenValue(name: string): string {
  return new RegExp(`${name}:\\s*([^;]+);`).exec(lightBlock)?.[1].trim() ?? ''
}

/** 遞迴展開 var() 別名，得到實際色值。 */
function resolveToken(name: string, depth = 0): string {
  const raw = tokenValue(name)
  const alias = /^var\((--[a-z0-9-]+)\)$/.exec(raw)
  if (alias && depth < 8) {
    return resolveToken(alias[1], depth + 1)
  }
  return raw.toLowerCase()
}

const srgb = (c: number) => {
  const v = c / 255
  return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4
}

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map(i => srgb(parseInt(hex.slice(i, i + 2), 16)))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}

/**
 * 取出某個選擇器**最後一次**宣告的內容。
 * 兩支 style.css 都是「基礎規則 + 檔尾 v4 覆寫層」的結構，
 * 決定實際外觀的是最後一筆，所以護欄一律檢查最後一筆。
 */
function lastRuleBody(css: string, selectorPattern: string, mustDeclare?: string): string {
  const all = [...css.matchAll(new RegExp(`${selectorPattern}\\s*\\{([^}]*)\\}`, 'g'))]
    .map(m => m[1])
    .filter(body => mustDeclare === undefined || body.includes(`${mustDeclare}:`))
  return all.length ? all[all.length - 1] : ''
}

describe('1. 參考圖取樣值進入 token，作為唯一來源', () => {
  it('把六張預覽的取樣色寫進 primitive 層', () => {
    expect(resolveToken('--brand-blue-600')).toBe(PRIMARY_BLUE)
    expect(resolveToken('--brand-ink-900')).toBe(DEEP_INK)
    expect(resolveToken('--brand-blue-100')).toBe(SECTION_BLUE)
    expect(resolveToken('--brand-blue-200')).toBe(TINT_BLUE)
    expect(resolveToken('--brand-pink-500')).toBe(BRAND_PINK)
  })

  it('把使用者指定的語意角色都建出來', () => {
    for (const name of [
      '--color-page', '--color-surface', '--color-section',
      '--color-primary', '--color-primary-hover', '--color-ink',
      '--color-border', '--color-brand-pink', '--color-accent-teal',
    ]) {
      expect(tokenValue(name), `${name} 必須存在`).not.toBe('')
    }
  })

  it('保留舊 token 名稱為別名，既有元件不需要一次改名', () => {
    expect(resolveToken('--color-bg')).toBe(resolveToken('--color-page'))
    expect(resolveToken('--color-primary-dark')).toBe(resolveToken('--color-primary-hover'))
  })
})

describe('2. 白／近白是最大面積', () => {
  it('把畫布與表面綁在白／近白，不綁在殼層漸層', () => {
    expect(resolveToken('--color-surface')).toBe(SURFACE)
    expect(resolveToken('--color-page')).toBe('#f7fafe')
    expect(luminance(resolveToken('--color-page'))).toBeGreaterThan(0.9)
  })

  it('兩支 App 的 header 都是白色實心，不是漸層殼層', () => {
    for (const css of [customerCss, adminCss]) {
      expect(css).toMatch(/\.site-header\s*\{[^}]*background:\s*var\(--color-surface\)/)
      expect(css).toMatch(/\.site-header\s*\{[^}]*border-bottom:\s*1px solid var\(--color-border-line\)/)
      // header 不得再套任何漸層
      expect(css).not.toMatch(/\.site-header\s*\{[^}]*background:[^;]*gradient/)
    }
  })

  it('Admin sidebar 是白底細右框，不是整片粉色也不是大片漸層', () => {
    expect(adminCss).toMatch(/\.admin-sidebar\s*\{[^}]*background:\s*var\(--color-surface\)/)
    expect(adminCss).toMatch(/\.admin-sidebar\s*\{[^}]*border-right:\s*1px solid var\(--color-border-line\)/)
    expect(adminCss).not.toMatch(/\.admin-sidebar\s*\{[^}]*background:[^;]*gradient/)
    expect(adminCss).not.toMatch(/\.admin-sidebar\s*\{[^}]*background:[^;]*pink/)
  })

  it('卡片是白底＋細邊＋輕陰影', () => {
    for (const css of [customerCss, adminCss]) {
      expect(css).toMatch(/\.card[^{]*\{[^}]*background:\s*var\(--color-surface\)/)
      expect(css).toMatch(/\.card[^{]*\{[^}]*border:\s*1px solid var\(--color-border-soft\)/)
      expect(css).toMatch(/\.card[^{]*\{[^}]*box-shadow:\s*var\(--shadow-sm\)/)
    }
  })
})

describe('3. 淡藍／淡青只負責分區', () => {
  it('表頭用 --color-section，不是白色也不是深色', () => {
    expect(customerCss).toMatch(/table thead th\s*\{[^}]*background:\s*var\(--color-section\)/)
    expect(adminCss).toMatch(/\.site-main thead th\s*\{[^}]*background:\s*var\(--color-section\)/)
    expect(resolveToken('--color-section')).toBe(SECTION_BLUE)
  })

  it('選取狀態用淡藍 tint', () => {
    expect(resolveToken('--color-primary-soft')).toBe(TINT_BLUE)
    for (const css of [customerCss, adminCss]) {
      expect(css).toMatch(/tr\[aria-selected="true"\][^{]*\{[^}]*background:\s*var\(--color-primary-soft\)/)
    }
  })

  it('分區底色與內文的對比全部過 AA', () => {
    for (const bg of [SURFACE, resolveToken('--color-page'), SECTION_BLUE, TINT_BLUE]) {
      expect(contrast(DEEP_INK, bg), `ink on ${bg}`).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('淡色分區上禁止白字', () => {
    for (const bg of [SECTION_BLUE, TINT_BLUE, BRAND_PINK]) {
      expect(contrast('#ffffff', bg), `white on ${bg}`).toBeLessThan(3)
    }
    for (const css of [customerCss, adminCss]) {
      expect(css).not.toMatch(/background:\s*var\(--color-section\)[^}]*color:\s*(#fff|white)/i)
      expect(css).not.toMatch(/background:\s*var\(--color-primary-soft\)[^}]*color:\s*(#fff|white)/i)
    }
  })
})

describe('4. 高飽和亮藍負責操作與資訊焦點', () => {
  it('主操作、品牌、focus 全部走亮藍', () => {
    expect(resolveToken('--color-primary')).toBe(PRIMARY_BLUE)
    expect(resolveToken('--color-brand')).toBe(PRIMARY_BLUE)
    expect(resolveToken('--color-focus-ring')).toBe(PRIMARY_BLUE)
    expect(resolveToken('--color-border-strong')).toBe(PRIMARY_BLUE)
  })

  it('亮藍在白底與淡藍分區上都過 AA，白字放在亮藍上也過 AA', () => {
    for (const bg of [SURFACE, resolveToken('--color-page'), SECTION_BLUE, TINT_BLUE]) {
      expect(contrast(PRIMARY_BLUE, bg), `primary on ${bg}`).toBeGreaterThanOrEqual(4.5)
    }
    expect(contrast('#ffffff', PRIMARY_BLUE)).toBeGreaterThanOrEqual(4.5)
    expect(contrast('#ffffff', resolveToken('--color-primary-hover'))).toBeGreaterThanOrEqual(4.5)
  })

  it('Customer 當前導覽項用亮藍字＋亮藍底線，不是深藍膠囊', () => {
    expect(customerCss).toMatch(
      /\.primary-nav a\[aria-current="page"\]\s*\{[^}]*color:\s*var\(--color-primary\)[^}]*box-shadow:\s*inset 0 -3px 0 var\(--color-primary\)/,
    )
  })

  it('Admin 當前側欄項用淡藍膠囊＋亮藍左指示條＋亮藍字', () => {
    const rule = lastRuleBody(adminCss, '\\.admin-sidebar a\\[aria-current="page"\\]')
    expect(rule).toContain('background: var(--color-primary-soft)')
    expect(rule).toContain('border-left-color: var(--color-primary)')
    expect(rule).toContain('color: var(--color-primary)')
  })

  it('次要按鈕的唯一定義在 shared AppButton，App 層不得覆寫', () => {
    // 兩支 App 曾各自複製一份亮藍覆寫，scoped 特異性又蓋過共用元件，
    // 造成「元件寫深藍、實際畫出來卻是亮藍」，而兩邊的字串測試都各自綠燈。
    for (const css of [customerCss, adminCss]) {
      expect(css).not.toContain('.ds-app-button--secondary')
    }
    const button = read(join(sharedRoot, 'src', 'components', 'AppButton.vue'))
    const rule = /\.ds-app-button--secondary\s*\{([^}]*)\}/.exec(button)?.[1] ?? ''
    expect(rule).toContain('border: 1px solid var(--color-primary)')
    expect(rule).toContain('color: var(--color-primary)')
    expect(rule).not.toContain('--color-navy')
  })
})

describe('5. 深海軍藍只做文字與少數強調區', () => {
  it('內文與標題綁在深海軍藍', () => {
    expect(resolveToken('--color-ink')).toBe(DEEP_INK)
    expect(resolveToken('--color-text')).toBe(DEEP_INK)
    expect(contrast(DEEP_INK, SURFACE)).toBeGreaterThanOrEqual(7)
  })

  it('header／footer／登入背景都不是深海軍藍實心', () => {
    expect(luminance(DEEP_INK)).toBeLessThan(0.4)
    expect(luminance(resolveToken('--color-surface'))).toBeGreaterThan(0.9)
    for (const name of ['--gradient-footer', '--gradient-auth', '--gradient-hero']) {
      const value = tokenValue(name).toLowerCase()
      expect(value, `${name} 不得含深海軍藍`).not.toContain(DEEP_INK)
      expect(value, `${name} 不得含深藍 #063d8f`).not.toContain('#063d8f')
    }
  })

  it('深強調漸層仍然保留，供 hero／CTA 等少數區塊使用', () => {
    const deep = tokenValue('--gradient-deep').toLowerCase()
    expect(deep).toContain(DEEP_INK)
    expect(resolveToken('--color-on-deep')).toBe('#ffffff')
    expect(contrast('#ffffff', DEEP_INK)).toBeGreaterThanOrEqual(4.5)
  })
})

describe('6. #E6C8D4 是輔助色：保留但不支配殼層', () => {
  it('粉色仍是 primitive 與公開語意 token', () => {
    expect(resolveToken('--brand-pink-500')).toBe(BRAND_PINK)
    expect(resolveToken('--color-brand-pink')).toBe(BRAND_PINK)
    expect(resolveToken('--color-surface-pink')).toBe('#faf1f5')
  })

  it('粉色出現在 hero 光暈、footer 尾端與登入外圈這三處局部裝飾', () => {
    expect(tokenValue('--gradient-hero-glow').toLowerCase()).toContain('230 200 212')
    expect(tokenValue('--gradient-footer').toLowerCase()).toContain('#faf1f5')
    expect(tokenValue('--gradient-auth').toLowerCase()).toContain('#f7eef3')
  })

  it('粉色不得回到 header、sidebar 或全域邊框', () => {
    for (const css of [customerCss, adminCss]) {
      expect(css).not.toMatch(/\.site-header\s*\{[^}]*background:[^;]*pink/)
    }
    expect(adminCss).not.toMatch(/\.admin-sidebar\s*\{[^}]*pink/)
    for (const name of ['--color-border', '--color-border-soft', '--color-border-line']) {
      expect(tokenValue(name)).not.toContain('pink')
    }
    // 粉色邊框仍可用，但必須是自己的專用 token
    expect(tokenValue('--color-border-pink')).toContain('pink')
  })

  it('粉底一律深海軍藍字（白字只有 1.55:1）', () => {
    expect(resolveToken('--color-on-pink')).toBe(DEEP_INK)
    expect(contrast(DEEP_INK, BRAND_PINK)).toBeGreaterThanOrEqual(4.5)
    expect(contrast('#ffffff', BRAND_PINK)).toBeLessThan(2)
  })
})

describe('7. 圖表系列可區分，語意色不被藍色吃掉', () => {
  const series = ['--chart-1', '--chart-2', '--chart-3', '--chart-4', '--chart-5', '--chart-6']

  it('六個系列都是不同色值，不是同一個藍', () => {
    const values = series.map(n => resolveToken(n))
    expect(new Set(values).size).toBe(series.length)
    for (const v of values) { expect(v).toMatch(/^#[0-9a-f]{6}$/) }
  })

  it('涵蓋藍、淺藍、青綠、黃橘、粉、紫多個色相家族', () => {
    const hue = (hex: string) => {
      const [r, g, b] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
      const max = Math.max(r, g, b)
      const min = Math.min(r, g, b)
      const d = max - min
      if (d === 0) { return 0 }
      const h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4
      return (h * 60 + 360) % 360
    }
    const families = new Set(series.map(n => Math.round(hue(resolveToken(n)) / 30)))
    expect(families.size).toBeGreaterThanOrEqual(4)
  })

  it('相鄰系列的亮度差足夠，單色列印或色覺障礙下仍可分辨', () => {
    for (let i = 0; i < series.length - 1; i++) {
      const r = contrast(resolveToken(series[i]), resolveToken(series[i + 1]))
      expect(r, `${series[i]} vs ${series[i + 1]} = ${r.toFixed(2)}`).toBeGreaterThanOrEqual(1.35)
    }
  })

  it('語意色保留原意，不被粉紅化也不被藍色吃掉', () => {
    expect(resolveToken('--color-success')).toBe('#0d7050')
    expect(resolveToken('--color-warning')).toBe('#8a5a12')
    expect(resolveToken('--color-danger')).toBe('#c02434')
    expect(resolveToken('--color-info')).toBe('#04716f')
    for (const name of ['--color-success', '--color-warning', '--color-danger', '--color-info']) {
      expect(tokenValue(name)).not.toContain('pink')
      expect(tokenValue(name)).not.toContain('blue')
    }
  })

  it('每個語意色在自己的底色上都過 AA', () => {
    const pairs: [string, string][] = [
      ['--color-success', '--color-success-bg'],
      ['--color-warning', '--color-warning-bg'],
      ['--color-danger', '--color-danger-bg'],
      ['--color-info', '--color-info-bg'],
    ]
    for (const [fg, bg] of pairs) {
      const r = contrast(resolveToken(fg), resolveToken(bg))
      expect(r, `${fg} on ${bg} = ${r.toFixed(2)}`).toBeGreaterThanOrEqual(4.5)
    }
  })
})

describe('8. 首頁保留九個語意圖示，並補上流程導覽感', () => {
  const homeSource = read(join(customerRoot, 'src', 'pages', 'HomePage.vue'))
  const iconSource = read(join(sharedRoot, 'src', 'components', 'BrandIcon.vue'))

  it('三個步驟都有圖示，型別與資料結構不倒退', () => {
    const steps = [...homeSource.matchAll(/\{ step: '[^']+', title: '[^']+', body: '[^']+', icon: '([a-z-]+)' \}/g)]
    expect(steps).toHaveLength(3)
    expect(steps.map(m => m[1])).toEqual(['purpose', 'budget', 'recommend'])
    expect(homeSource).toContain('interface HomeGuideItem')
    expect(homeSource).toContain('icon: BrandIconName')
  })

  it('六個分類都有圖示', () => {
    const cats = [...homeSource.matchAll(/\{ title: '[^']+', body: '[^']+', icon: '([a-z-]+)', categoryCode/g)]
    expect(cats).toHaveLength(6)
    expect(cats.map(m => m[1])).toEqual(['cpu', 'motherboard', 'memory', 'gpu', 'storage', 'case'])
    expect(homeSource).toContain('interface HomeCategoryItem')
  })

  it('分類圖示名稱與正式 catalog code 一一對應', () => {
    // 圖示名稱刻意等於 catalog code 的小寫，避免圖示與查詢條件各說各話
    const cats = [...homeSource.matchAll(/icon: '([a-z-]+)', categoryCode: '([A-Z_]+)'/g)]
    expect(cats).toHaveLength(6)
    for (const [, icon, code] of cats) {
      expect(icon, `${code} 的圖示名稱應是代碼小寫`).toBe(code.toLowerCase())
    }
  })

  it('每個圖示名稱都對到真的 path，執行期仍有 fallback', () => {
    const names = [...iconSource.matchAll(/^\s*\|\s*'([a-z-]+)'$/gm)].map(m => m[1])
    expect(names).toHaveLength(10)
    for (const name of names) {
      const key = /^[a-z]+$/.test(name) ? name : `'${name}'`
      expect(iconSource, `${name} needs paths`).toContain(`  ${key}: [`)
    }
    expect(iconSource).toContain('ICON_PATHS[props.name] ?? ICON_PATHS.purpose')
  })

  it('十個圖示同一套 stroke／fill／線寬規則', () => {
    expect(iconSource).toContain('viewBox="0 0 24 24"')
    expect(iconSource).toContain('stroke-width="1.75"')
    expect(iconSource).toContain('stroke="currentColor"')
    expect(iconSource).toContain('fill="none"')
  })

  it('圖示是裝飾，文字標籤全部保留，不使用 Emoji', () => {
    expect(iconSource).toContain(`:aria-hidden="decorative ? 'true' : undefined"`)
    expect(homeSource).toContain('{{ item.title }}')
    expect(homeSource).toContain('{{ item.body }}')
    expect(homeSource).toContain('{{ item.step }}')
    expect(homeSource).not.toMatch(/\p{Extended_Pictographic}/u)
    expect(iconSource).not.toMatch(/\p{Extended_Pictographic}/u)
  })

  it('三步驟有編號圓標與流程箭頭，箭頭是純裝飾的 ::before/::after', () => {
    expect(homeSource).toContain('class="home-step__marker"')
    expect(homeSource).toMatch(/class="home-step__marker"[\s\S]{0,80}aria-hidden="true"/)
    expect(customerCss).toMatch(/\.home-step__marker\s*\{[^}]*background:\s*var\(--color-primary\)/)
    expect(customerCss).toMatch(/\.home-step::after\s*\{/)
    expect(customerCss).toMatch(/\.home-step:last-child::after[\s\S]{0,80}content: none/)
  })

  it('分類圖示夠大，足以當辨識入口', () => {
    const rule = lastRuleBody(customerCss, '\\.home-category__icon', 'width')
    expect(Number(/width:\s*(\d+)px/.exec(rule)?.[1])).toBeGreaterThanOrEqual(32)
    const step = lastRuleBody(customerCss, '\\.home-step__icon', 'width')
    expect(Number(/width:\s*(\d+)px/.exec(step)?.[1])).toBeGreaterThanOrEqual(40)
  })

  it('圖示顏色在自己的底板上超過 3:1', () => {
    expect(contrast(PRIMARY_BLUE, TINT_BLUE)).toBeGreaterThanOrEqual(3)
  })

  it('整張分類卡片可點擊', () => {
    expect(homeSource).toMatch(/<RouterLink[\s\S]{0,260}:to="item\.to \?\? \{ path: '\/products', query: \{ category: item\.categoryCode \} \}"/)
  })

  it('圖示描繪只做一次，沒有無限動畫，CSS 不預先藏線條', () => {
    const helpers = read(join(sharedRoot, 'src', 'motion', 'helpers.ts'))
    expect(helpers).toContain('export function createIconDraw')
    expect(stripComments(helpers)).not.toMatch(/repeat:\s*-1/)
    expect(homeSource).toContain('createIconDraw(iconHosts.value')
    expect(customerCss).not.toMatch(/stroke-dasharray/)
  })

  it('reduced motion 下完全不建立描繪動畫，也取消 hover 位移', () => {
    const helpers = read(join(sharedRoot, 'src', 'motion', 'helpers.ts'))
    const fn = helpers.slice(helpers.indexOf('export function createIconDraw'))
    expect(fn).toContain('options.reducedMotion')
    expect(fn.slice(0, fn.indexOf('const strokes'))).toContain('return null')
    expect(customerCss).toMatch(/@media \(prefers-reduced-motion: reduce\)[\s\S]{0,600}transform: none/)
  })

  it('Windows 高對比模式下圖示容器仍有邊界', () => {
    expect(customerCss).toMatch(/@media \(forced-colors: active\)[\s\S]{0,400}CanvasText/)
    expect(iconSource).toContain('forced-colors: active')
  })

  it('Hero 素材是真的檔案，不是 base64 也不是 CDN', () => {
    const art = join(customerRoot, 'public', 'brand', 'donggu-hero-wave.png')
    expect(existsSync(art)).toBe(true)
    expect(statSync(art).size).toBeLessThan(400 * 1024)
    expect(homeSource).toContain('import.meta.env.BASE_URL')
    expect(homeSource).toContain('brand/donggu-hero-wave.png')
    expect(homeSource).toContain('loading="lazy"')
    expect(homeSource).toMatch(/class="home-hero__art"[\s\S]{0,120}aria-hidden="true"/)
  })

  it('375px 隱藏 Hero 裝飾，CTA 保持完整', () => {
    expect(customerCss).toMatch(/@media \(max-width: 640px\)\s*\{[^}]*\.home-hero__art\s*\{[^}]*display: none/)
  })
})

describe('9. 邊框是低對比中性藍灰', () => {
  it('全域邊框 token 不落在粉色家族', () => {
    expect(tokenValue('--color-border')).toBe('var(--slate-400)')
    expect(tokenValue('--color-border-soft')).toBe('var(--slate-200)')
    expect(tokenValue('--color-border-line')).toBe('var(--slate-300)')
  })

  it('表單控制項過 3:1，表格分隔刻意低對比', () => {
    expect(contrast(resolveToken('--color-border'), SURFACE)).toBeGreaterThanOrEqual(3)
    expect(contrast(resolveToken('--color-border-soft'), SURFACE)).toBeLessThan(3)
  })

  it('高密度後台表格走中性分隔', () => {
    expect(adminCss).toMatch(/\.site-main th,\s*\.site-main td\s*\{[^}]*border-color:\s*var\(--color-border-soft\)/)
    expect(adminCss).not.toMatch(/\.site-main table\s*\{[^}]*border:[^;]*var\(--color-border-pink\)/)
  })
})

describe('10. 高密度表格在窄容器仍可掃讀（參考圖 04／05）', () => {
  it('兩支 App 的 grid 殼層都夾住軌道，超寬內容不會把整頁推寬', () => {
    for (const css of [customerCss, adminCss]) {
      expect(lastRuleBody(css, '\\.app-shell', 'grid-template-columns')).toContain('minmax(0, 1fr)')
    }
  })

  it('.table-scroll 是真的捲動容器，而且不會讓表頭浮起來', () => {
    for (const css of [customerCss, adminCss]) {
      expect(css).toMatch(/\.table-scroll\s*\{[^}]*overflow-x:\s*auto/)
    }
    // 橫向捲動容器裡的 position: sticky 會改以容器為基準，表頭會浮在資料列中間
    expect(adminCss).toMatch(/\.table-scroll thead th\s*\{[^}]*position:\s*static/)
  })

  it('兩張最寬的後台表格都放在捲動容器裡', () => {
    const products = read(join(adminRoot, 'src', 'pages', 'ProductsPage.vue'))
    const returns = read(join(adminRoot, 'src', 'pages', 'returns', 'AdminReturnQueuePage.vue'))
    expect(products).toMatch(/class="table-scroll"[\s\S]{0,120}<table class="products-table"/)
    expect(returns).toMatch(/class="table-scroll"[\s\S]{0,120}<table class="admin-returns__table"/)
  })

  it('客服案件列表依「欄寬」而非視窗寬度切換堆疊版型', () => {
    const layout = read(join(customerRoot, 'src', 'components', 'layout', 'CaseSplitLayout.vue'))
    const list = read(join(customerRoot, 'src', 'pages', 'support', 'SupportTicketListPage.vue'))
    expect(layout).toContain('container-type: inline-size')
    expect(layout).toContain('container-name: case-list')
    expect(list).toContain('@container case-list (max-width: 640px)')
    // 面板比例本身不動：桌面仍是 2fr : 3fr
    expect(layout).toMatch(/\.case-split\[data-detail-open='true'\]\s*\{[^}]*grid-template-columns:\s*2fr 3fr/)
  })

  it('報表頁在 375px 收成單欄，圖表列不再用固定軌道', () => {
    const report = read(join(adminRoot, 'src', 'pages', 'OperationalReportPage.vue'))
    expect(report).toMatch(/@media \(max-width: 560px\)[\s\S]{0,320}\.report-filters \{ grid-template-columns: minmax\(0, 1fr\)/)
    expect(report).toMatch(/@media \(max-width: 560px\)[\s\S]{0,320}\.report-chart__row \{ grid-template-columns: minmax\(0, 1fr\) auto/)
  })

  it('報表圖表讀 --chart-* token，表頭讀 --color-section', () => {
    const report = read(join(adminRoot, 'src', 'pages', 'OperationalReportPage.vue'))
    expect(report).toContain('background: var(--chart-1)')
    expect(report).toContain('background: var(--chart-track)')
    expect(report).toMatch(/\.report-table th \{ background: var\(--color-section\)/)
  })
})

describe('11. 既有保證全部保留', () => {
  it('兩支 App 共用同一份 Token 與同一個 PrimeVue preset', () => {
    for (const root of [customerRoot, adminRoot]) {
      const main = read(join(root, 'src', 'main.ts'))
      expect(main).toContain('@doselect/web-shared/styles/design-tokens.css')
      expect(main).toContain('DoSelectPreset')
      expect(main).toMatch(/darkModeSelector:\s*false/)
    }
    const preset = read(join(sharedRoot, 'src', 'theme', 'doselect-preset.ts'))
    expect(stripComments(preset)).not.toMatch(/#[0-9a-f]{3,8}\b/i)
  })

  it('維持 Light-only', () => {
    expect(tokensCss).not.toMatch(/prefers-color-scheme/i)
    expect(tokensCss).toContain(':root[data-theme="dark"]')
  })

  it('兩支 App 都帶最佳化後的正式標記衍生檔，檔名固定、體積受限', () => {
    // 唯一來源是「DoSelect 懂選 正式商標2」；1x/2x/3x 各一個 WebP 與 PNG 後備。
    const ALLOWED = [
      'doselect-mark-40.webp', 'doselect-mark-80.webp', 'doselect-mark-120.webp',
      'doselect-mark-40.png', 'doselect-mark-80.png', 'doselect-mark-120.png',
    ]
    // header 只顯示 40px，任何一個衍生檔都不該超過 40 KB（舊的 1.2 MB Logo 是被換掉的原因）
    const CEILING = 40 * 1024

    for (const root of [customerRoot, adminRoot]) {
      const dir = join(root, 'public', 'brand')
      for (const file of ALLOWED) {
        const p = join(dir, file)
        expect(existsSync(p), `${p} must exist`).toBe(true)
        const size = statSync(p).size
        expect(size, `${file} 太小，可能不是有效圖檔`).toBeGreaterThan(256)
        expect(size, `${file} = ${(size / 1024).toFixed(1)} KB，超過 ${CEILING / 1024} KB 上限`)
          .toBeLessThanOrEqual(CEILING)
      }
      // 不得殘留任何舊的大型 Logo
      for (const stale of ['doselect-logo-horizontal.png', 'doselect-logo-badge.png']) {
        expect(existsSync(join(dir, stale)), `${stale} 應已刪除`).toBe(false)
      }
      expect(read(join(root, 'src', 'App.vue'))).toContain('BrandMark')
    }
  })

  it('BrandMark 每個 viewport 只建立一個 <img>，不用 CSS 隱藏第二張', () => {
    const mark = read(join(sharedRoot, 'src', 'components', 'BrandMark.vue'))
    const template = /<template>([\s\S]*?)<\/template>/.exec(mark)?.[1] ?? ''

    // 關鍵不變式：整個 template 只有一個 <img>
    expect([...template.matchAll(/<img\b/g)]).toHaveLength(1)
    // 由 <picture> + srcset 讓瀏覽器只挑一個資源，而不是放兩張再 display:none
    expect(template).toContain('<picture')
    expect(template).toContain('type="image/webp"')
    expect(template).toMatch(/:srcset="pngSrcset"/)
    expect(mark).toContain('import.meta.env.BASE_URL')
    // 舊 Logo 完全不再被引用
    expect(mark).not.toContain('doselect-logo-horizontal')
    expect(mark).not.toContain('doselect-logo-badge')
    // 比例、替代文字與錯誤後備
    expect(mark).toContain('aspect-ratio: 1 / 1')
    expect(mark).toContain('object-fit: contain')
    expect(mark).toContain('alt="DoSelect 懂選"')
    expect(mark).toContain('@error="markAvailable = false"')
    expect(mark).toMatch(/v-else[\s\S]{0,160}aria-label="DoSelect 懂選（正式 Logo 尚未匯入）"/)
  })

  it('全前端沒有任何舊 Logo 檔名的殘留引用', () => {
    const collect = (dir: string): string[] => {
      const out: string[] = []
      for (const entry of readdirSync(dir, { withFileTypes: true })) {
        if (entry.name === 'node_modules' || entry.name === 'dist') { continue }
        const full = join(dir, entry.name)
        if (entry.isDirectory()) { out.push(...collect(full)) }
        // 排除 .spec.ts：護欄本身就必須寫出那些被禁止的檔名
        else if (/\.(?:vue|ts|css|md|html)$/.test(entry.name) && !entry.name.endsWith('.spec.ts')) { out.push(full) }
      }
      return out
    }
    const roots = [
      join(customerRoot, 'src'), join(customerRoot, 'public'), join(customerRoot, 'e2e'),
      join(adminRoot, 'src'), join(adminRoot, 'public'),
      join(sharedRoot, 'src'),
    ]
    for (const root of roots) {
      if (!existsSync(root)) { continue }
      for (const file of collect(root)) {
        const source = read(file)
        // 找的是「真的會去載入」的路徑形式（brand/xxx），
        // README 用散文說明「舊檔已刪除」不算引用。
        expect(source, `${file} 仍引用舊的橫式 Logo`).not.toContain('brand/doselect-logo-horizontal')
        expect(source, `${file} 仍引用舊的方形 Logo`).not.toContain('brand/doselect-logo-badge')
      }
    }
  })

  it('品牌 README 與 BrandMark 實作一致', () => {
    const mark = read(join(sharedRoot, 'src', 'components', 'BrandMark.vue'))
    for (const root of [customerRoot, adminRoot]) {
      const readme = read(join(root, 'public', 'brand', 'README.md'))
      // 唯一來源要寫清楚
      expect(readme).toContain('DoSelect 懂選 正式商標2')
      // README 列出的檔名必須真的被 BrandMark 引用
      for (const file of ['doselect-mark-40.webp', 'doselect-mark-80.webp', 'doselect-mark-120.webp']) {
        expect(readme, `README 應列出 ${file}`).toContain(file)
      }
      expect(mark).toContain('doselect-mark-40.webp')
      // 舊的「放進去就自動生效」說明必須移除
      expect(readme).not.toContain('donggu-mark.png')
      expect(readme).not.toContain('不需要改任何程式碼')
    }
  })

  it('正式動效決策不變', () => {
    const presets = read(join(sharedRoot, 'src', 'motion', 'presets.ts'))
    expect(presets).toMatch(/customerDefaultMotionPresetId: MotionPresetId = 'donggu'/)
    expect(presets).toMatch(/adminDefaultMotionPresetId: MotionPresetId = 'crisp'/)
    expect(presets).toMatch(/sensitiveFlowMotionPresetId: MotionPresetId = 'gentle'/)
    expect(read(join(customerRoot, 'src', 'App.vue'))).toContain('useMotionPresetSelection(customerDefaultMotionPresetId)')
    expect(read(join(adminRoot, 'src', 'App.vue'))).toContain('useMotionPresetSelection(adminDefaultMotionPresetId)')
    expect(read(join(customerRoot, 'src', 'components', 'layout', 'CaseSplitLayout.vue'))).toContain('useSensitiveMotionPreset()')
  })

  it('Admin 375px 溢位修正仍在', () => {
    expect(adminCss).toMatch(/\.app-shell--bare\s*\{[^}]*grid-template-columns:\s*minmax\(0, 1fr\)/)
  })

  it('絕不把 Logo 內嵌成大型 base64', () => {
    const collect = (dir: string): string[] => {
      const out: string[] = []
      for (const entry of readdirSync(dir, { withFileTypes: true })) {
        if (entry.name === 'node_modules' || entry.name === 'dist') { continue }
        const full = join(dir, entry.name)
        if (entry.isDirectory()) { out.push(...collect(full)) }
        else if (/\.(?:vue|ts|css)$/.test(entry.name)) { out.push(full) }
      }
      return out
    }
    for (const root of [customerRoot, adminRoot, sharedRoot]) {
      for (const file of collect(join(root, 'src'))) {
        expect(read(file), `${file} must not inline a base64 image`).not.toMatch(/data:image\/(png|jpe?g);base64/)
      }
    }
  })
})

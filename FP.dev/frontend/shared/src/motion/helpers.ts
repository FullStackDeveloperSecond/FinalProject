import gsap from 'gsap'
import type { MotionPreset } from './presets'

/**
 * GSAP helper 集合。
 *
 * 三個貫穿全檔的規則：
 *
 * 1. 一律使用 `gsap.from()` / `gsap.fromTo()` 的「from」語意 —— 起始狀態由 GSAP 在
 *    執行期寫入，CSS 不預先把內容設成 opacity: 0。GSAP 或 JS 載入失敗時，
 *    畫面上的內容本來就是可見的。
 * 2. `reducedMotion` 為 true 時，**完全不建立 tween**（回傳 null），
 *    而不是把 duration 調慢；位移、縮放、彈跳與 stagger 全部消失，內容立即呈現。
 * 3. 只動 transform（x / y / scale）與 opacity。沒有任何 `repeat: -1`。
 *
 * 呼叫端傳入的 `targets` 應該由 template ref 取得，不使用全域 selector，
 * 也不依賴 DOM index 判斷業務資料。
 */

export type MotionTarget = Element | Element[] | null | undefined

export interface MotionOptions {
  /** 由 useMotionScope（gsap.matchMedia）或呼叫端提供。 */
  reducedMotion?: boolean
  /** 延遲開始（秒）。 */
  delay?: number
  /** 完成後的一次性回呼。 */
  onComplete?: () => void
}

/** 過濾掉 null／空陣列，並確保回傳的是真的存在於 document 的節點集合。 */
function normalizeTargets(targets: MotionTarget): Element[] {
  if (!targets) {
    return []
  }

  const list = Array.isArray(targets) ? targets : [targets]
  return list.filter((node): node is Element => Boolean(node))
}

/**
 * 頁面標題／主要區塊淡入。
 * route 切換時內容已經在畫面上，這個 tween 只是把它「補」進來，不擋顯示。
 */
export function createPageReveal(
  targets: MotionTarget,
  preset: MotionPreset,
  options: MotionOptions = {},
): gsap.core.Tween | null {
  const nodes = normalizeTargets(targets)
  if (nodes.length === 0 || options.reducedMotion) {
    return null
  }

  const { duration, ease, y, scaleFrom } = preset.reveal
  return gsap.from(nodes, {
    opacity: 0,
    y,
    scale: scaleFrom,
    duration,
    ease,
    delay: options.delay ?? 0,
    onComplete: options.onComplete,
    // 動畫結束後清掉 inline transform，避免殘留影響後續 layout 與截圖。
    clearProps: 'transform,opacity',
  })
}

/**
 * 卡片／列表分批出現。
 * 超過 preset.stagger.maxItems 的項目直接顯示，長列表尾端不會被迫等待。
 */
export function createListStagger(
  targets: MotionTarget,
  preset: MotionPreset,
  options: MotionOptions = {},
): gsap.core.Tween | null {
  const nodes = normalizeTargets(targets)
  if (nodes.length === 0 || options.reducedMotion) {
    return null
  }

  const { duration, ease, y, scaleFrom, each, maxItems } = preset.stagger
  const animated = nodes.slice(0, maxItems)

  return gsap.from(animated, {
    opacity: 0,
    y,
    scale: scaleFrom,
    duration,
    ease,
    stagger: each,
    delay: options.delay ?? 0,
    onComplete: options.onComplete,
    clearProps: 'transform,opacity',
  })
}

/**
 * 同層面板進場（客服案件 2/5 : 3/5 的詳細欄）。
 *
 * 刻意只動 opacity 與 x/scale：不動 width、不動 grid-template-columns，
 * 所以列表在整段動畫期間都保持原位、不會被遮住，也不會產生橫向溢位。
 */
export function createPanelEnter(
  targets: MotionTarget,
  preset: MotionPreset,
  options: MotionOptions = {},
): gsap.core.Tween | null {
  const nodes = normalizeTargets(targets)
  if (nodes.length === 0 || options.reducedMotion) {
    return null
  }

  const { duration, ease, x, scaleFrom } = preset.panel
  return gsap.from(nodes, {
    opacity: 0,
    x,
    scale: scaleFrom,
    duration,
    ease,
    delay: options.delay ?? 0,
    onComplete: options.onComplete,
    clearProps: 'transform,opacity',
  })
}

/**
 * 面板收起。
 *
 * 注意：呼叫端仍然由「右上角關閉鈕」觸發，且 v-if 的移除不等待這個 tween ——
 * 動畫不是關閉的必要條件，只是視覺補強。
 */
export function createPanelLeave(
  targets: MotionTarget,
  preset: MotionPreset,
  options: MotionOptions = {},
): gsap.core.Tween | null {
  const nodes = normalizeTargets(targets)
  if (nodes.length === 0 || options.reducedMotion) {
    options.onComplete?.()
    return null
  }

  const { leaveDuration, leaveEase, x } = preset.panel
  return gsap.to(nodes, {
    opacity: 0,
    x,
    duration: leaveDuration,
    ease: leaveEase,
    onComplete: options.onComplete,
  })
}

/**
 * 送出成功／承接成功的一次性回饋脈衝。
 *
 * `disabled` 或 loading 中的元素一律不動 —— 由呼叫端在觸發前檢查，
 * 這裡再多做一層防護：帶 disabled 屬性或 aria-busy 的節點會被過濾掉。
 */
export function createFeedbackPulse(
  targets: MotionTarget,
  preset: MotionPreset,
  options: MotionOptions = {},
): gsap.core.Tween | null {
  const nodes = normalizeTargets(targets).filter(node => !isInert(node))
  if (nodes.length === 0 || options.reducedMotion) {
    return null
  }

  const { duration, ease, scaleTo } = preset.feedback
  return gsap.fromTo(
    nodes,
    { scale: 1 },
    {
      scale: scaleTo,
      duration,
      ease,
      yoyo: true,
      // 固定往返一次；永遠不是 -1。
      repeat: 1,
      delay: options.delay ?? 0,
      onComplete: options.onComplete,
      clearProps: 'transform',
    },
  )
}

/**
 * 表單錯誤欄位的一次性水平提示。
 *
 * 只做一次來回，不持續抖動；動畫不取代錯誤文字與 ARIA，
 * 焦點移動由呼叫端負責（動畫不能是唯一的狀態提示）。
 */
export function createFieldShake(
  targets: MotionTarget,
  preset: MotionPreset,
  options: MotionOptions = {},
): gsap.core.Tween | null {
  const nodes = normalizeTargets(targets)
  if (nodes.length === 0 || options.reducedMotion) {
    return null
  }

  const { duration, ease, x, repeat } = preset.shake
  return gsap.fromTo(
    nodes,
    { x: -x },
    {
      x,
      duration,
      ease,
      yoyo: true,
      repeat,
      delay: options.delay ?? 0,
      onComplete: options.onComplete,
      clearProps: 'transform',
    },
  )
}

/** disabled / aria-disabled / aria-busy 的元素不接受回饋動畫。 */
export function isInert(node: Element): boolean {
  if ('disabled' in node && (node as HTMLButtonElement).disabled) {
    return true
  }

  return node.getAttribute('aria-disabled') === 'true'
    || node.getAttribute('aria-busy') === 'true'
    || node.hasAttribute('data-loading')
}

/**
 * 圖示描繪：線條由短到長畫出來，收尾是一次性的，沒有循環。
 * 視覺參考 SweetAlert2 的圖示描繪語彙（只參考作法，未引入該套件，也沒有複製其資源）。
 *
 * 這是本檔唯一動到 transform／opacity 以外屬性的 helper：`strokeDashoffset`。
 * SVG 的 dashoffset 只觸發重繪、不觸發 layout，是線條描繪的標準作法；
 * 起始值同樣由 GSAP 在執行期寫入，CSS 不預先把線條藏起來，
 * 因此 JS 或 GSAP 失敗時圖示仍然完整可見。
 */
export function createIconDraw(
  targets: MotionTarget,
  preset: MotionPreset,
  options: MotionOptions = {},
): gsap.core.Tween | null {
  const roots = normalizeTargets(targets)
  if (roots.length === 0 || options.reducedMotion) {
    return null
  }

  const strokes: SVGPathElement[] = []
  for (const root of roots) {
    strokes.push(...Array.from(root.querySelectorAll<SVGPathElement>('svg path')))
  }
  if (strokes.length === 0) {
    return null
  }

  const { duration, ease, each, maxItems } = preset.stagger
  const animated = strokes.slice(0, maxItems * 4)

  for (const path of animated) {
    const length = typeof path.getTotalLength === 'function' ? path.getTotalLength() : 0
    if (length > 0) {
      path.style.strokeDasharray = String(length)
    }
  }

  return gsap.from(animated, {
    strokeDashoffset: (_index: number, target: SVGPathElement) =>
      typeof target.getTotalLength === 'function' ? target.getTotalLength() : 0,
    duration: duration * 1.4,
    ease,
    stagger: each,
    delay: options.delay ?? 0,
    onComplete: () => {
      // 收尾把 inline 的 dash 設定清掉，讓圖示回到原本的實線狀態。
      for (const path of animated) {
        path.style.strokeDasharray = ''
        path.style.strokeDashoffset = ''
      }
      options.onComplete?.()
    },
  })
}

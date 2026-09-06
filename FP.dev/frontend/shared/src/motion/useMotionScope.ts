import gsap from 'gsap'
import { getCurrentInstance, onUnmounted, ref, toValue, type MaybeRefOrGetter, type Ref } from 'vue'

/**
 * 把 GSAP 綁進 Vue component 生命週期。
 *
 * - 用 `gsap.context(fn, scope)` 讓所有 tween 記在同一個 context 上。
 * - 用 `gsap.matchMedia()` 處理 `(prefers-reduced-motion: reduce)`，
 *   由 GSAP 自己在偏好變更時 revert 舊的、重建新的。
 * - `onUnmounted` 一定 `revert()`：tween、inline style 與 matchMedia listener 全部歸零。
 *
 * reduced motion 的定義是「不建立位移／縮放 timeline」，不是把動畫放慢，
 * 因此 builder 收到 `reducedMotion: true` 時，helpers 會直接回傳 null。
 */

const REDUCE_QUERY = '(prefers-reduced-motion: reduce)'
const FULL_QUERY = '(prefers-reduced-motion: no-preference)'

export interface MotionScopeContext {
  /** 目前是否為 reduced motion。 */
  reducedMotion: boolean
  /** 這次 run 的 scope 元素，可能為 null。 */
  scope: Element | null
}

export type MotionScopeBuilder = (context: MotionScopeContext) => void

export interface UseMotionScopeResult {
  /** 建立（或重建）這個 scope 的動畫。重複呼叫會先 revert 前一次。 */
  run: (builder: MotionScopeBuilder) => void
  /** 手動清除；`onUnmounted` 也會自動呼叫。 */
  revert: () => void
  /** 目前偵測到的 reduced motion 狀態，供樣板顯示或測試斷言。 */
  reducedMotion: Ref<boolean>
}

/** 讀取一次系統偏好；環境沒有 matchMedia 時一律視為 reduced（保守：不動畫）。 */
export function detectReducedMotion(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return true
  }

  try {
    return window.matchMedia(REDUCE_QUERY).matches
  }
  catch {
    return true
  }
}

export function useMotionScope(
  scope?: MaybeRefOrGetter<Element | null | undefined>,
): UseMotionScopeResult {
  let context: gsap.Context | null = null
  let matchMedia: ReturnType<typeof gsap.matchMedia> | null = null
  const reducedMotion = ref(detectReducedMotion())

  function revert(): void {
    matchMedia?.revert()
    matchMedia = null
    context?.revert()
    context = null
  }

  function run(builder: MotionScopeBuilder): void {
    revert()

    const element = toValue(scope) ?? null
    context = gsap.context(() => {}, element ?? undefined)

    // 沒有 matchMedia 的環境（jsdom、SSR、極舊瀏覽器）不建立 gsap.matchMedia：
    // 直接以保守的 reduced 走一次 builder，helpers 便不會產生任何 tween，
    // 內容維持原本就可見的狀態。
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      reducedMotion.value = true
      context.add(() => {
        builder({ reducedMotion: true, scope: element })
      })
      return
    }

    matchMedia = gsap.matchMedia(element ?? undefined)

    matchMedia.add(
      { reduce: REDUCE_QUERY, full: FULL_QUERY },
      (mediaContext) => {
        const conditions = mediaContext.conditions as { reduce?: boolean, full?: boolean } | undefined
        // 兩個查詢都沒命中時（極少數環境），保守地當成 reduced。
        const isReduced = conditions?.full === true ? false : true
        reducedMotion.value = isReduced

        context?.add(() => {
          builder({ reducedMotion: isReduced, scope: element })
        })
      },
      element ?? undefined,
    )
  }

  // 允許在 component 之外呼叫（例如原型頁），此時由呼叫端自行 revert。
  if (getCurrentInstance()) {
    onUnmounted(revert)
  }

  return { run, revert, reducedMotion }
}

/**
 * 只需要知道偏好、不需要建立 tween 時使用（例如決定要不要顯示「已略過動畫」說明）。
 * 會在 component unmount 時移除 listener。
 */
export function useMotionPreference(): Ref<boolean> {
  const prefersReduced = ref(detectReducedMotion())

  if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
    const query = window.matchMedia(REDUCE_QUERY)
    const onChange = (event: MediaQueryListEvent): void => {
      prefersReduced.value = event.matches
    }

    query.addEventListener?.('change', onChange)

    if (getCurrentInstance()) {
      onUnmounted(() => {
        query.removeEventListener?.('change', onChange)
      })
    }
  }

  return prefersReduced
}

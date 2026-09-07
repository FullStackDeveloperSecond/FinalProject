/**
 * DoSelect Motion Presets（GSAP 動態視覺探索 v1）
 *
 * 三套完整參數，供 Codex 一次比較後擇一定案。這裡只放「數值」，
 * 不放任何 DOM 操作 —— helpers.ts 才負責把數值變成 tween。
 *
 * 共同規則（三套都遵守）：
 * - 只動 transform（x / y / scale）與 opacity，必要時才碰 CSS custom property。
 * - 不動 width / height / top / left / margin / box-shadow / filter。
 * - 沒有任何 repeat: -1 的無限動畫。
 * - reduced motion 由 helpers 直接不建立 tween，不是把這裡的 duration 調慢。
 */

/** 三套 preset 的識別字。 */
export type MotionPresetId = 'gentle' | 'donggu' | 'crisp'

export interface RevealSpec {
  /** 秒。 */
  duration: number
  ease: string
  /** 進場前的 y 位移（px），正值代表由下往上。 */
  y: number
  /** 進場前的 scale，1 表示不縮放。 */
  scaleFrom: number
}

export interface StaggerSpec extends RevealSpec {
  /** 每個項目之間的間隔（秒）。 */
  each: number
  /** 一次進場的最大項目數；超過的直接顯示，避免長列表尾端等太久。 */
  maxItems: number
}

export interface PanelSpec {
  duration: number
  ease: string
  /** 由右側滑入的水平位移（px）。 */
  x: number
  scaleFrom: number
  /** 收起時的時間通常比展開短。 */
  leaveDuration: number
  leaveEase: string
}

export interface FeedbackSpec {
  duration: number
  ease: string
  /** 一次性脈衝的最大 scale。 */
  scaleTo: number
}

export interface ShakeSpec {
  duration: number
  ease: string
  /** 水平提示幅度（px）。 */
  x: number
  /** 來回次數；固定為有限次，永遠不會是 -1。 */
  repeat: number
}

export interface MotionPreset {
  id: MotionPresetId
  label: string
  description: string
  /** 頁面標題／主要區塊淡入。 */
  reveal: RevealSpec
  /** 卡片、列表分批出現。 */
  stagger: StaggerSpec
  /** 案件詳細等同層面板進出。 */
  panel: PanelSpec
  /** 送出成功／承接成功的一次性回饋。 */
  feedback: FeedbackSpec
  /** 表單錯誤欄位的一次性水平提示。 */
  shake: ShakeSpec
}

/**
 * Preset A — Gentle Guidance
 * 新手友善、柔和、低干擾。不使用任何 overshoot。
 */
export const gentlePreset: MotionPreset = {
  id: 'gentle',
  label: 'A — Gentle Guidance',
  description: '柔和、低干擾。不使用 overshoot，適合資訊型頁面與需要專心閱讀的流程。',
  reveal: { duration: 0.34, ease: 'power2.out', y: 10, scaleFrom: 1 },
  stagger: { duration: 0.32, ease: 'power2.out', y: 12, scaleFrom: 0.98, each: 0.045, maxItems: 12 },
  panel: { duration: 0.42, ease: 'power2.out', x: 14, scaleFrom: 1, leaveDuration: 0.24, leaveEase: 'power1.inOut' },
  feedback: { duration: 0.28, ease: 'power1.inOut', scaleTo: 1.02 },
  shake: { duration: 0.22, ease: 'power2.out', x: 5, repeat: 1 },
}

/**
 * Preset B — Donggu Friendly
 * 親切活潑。彈性只出現在品牌、導購與「完成」狀態。
 * 危險／退款／檢舉與表單錯誤一律不套彈性（shake 仍用 power2.out）。
 */
export const dongguPreset: MotionPreset = {
  id: 'donggu',
  label: 'B — Donggu Friendly',
  description: '親切活潑，帶受控 overshoot。僅用於品牌、導購與完成狀態；錯誤與危險操作不套彈性。',
  reveal: { duration: 0.42, ease: 'back.out(1.25)', y: 12, scaleFrom: 0.96 },
  stagger: { duration: 0.46, ease: 'back.out(1.25)', y: 14, scaleFrom: 0.94, each: 0.055, maxItems: 12 },
  panel: { duration: 0.48, ease: 'back.out(1.1)', x: 16, scaleFrom: 0.98, leaveDuration: 0.26, leaveEase: 'power2.inOut' },
  feedback: { duration: 0.5, ease: 'elastic.out(1, 0.55)', scaleTo: 1.06 },
  // 錯誤提示刻意與 preset A 相同：不對表單錯誤使用幽默或彈跳。
  shake: { duration: 0.22, ease: 'power2.out', x: 5, repeat: 1 },
}

/**
 * Preset C — Crisp Tech
 * 快速、精準，方向明確，幾乎不使用 bounce。主要供後台。
 */
export const crispPreset: MotionPreset = {
  id: 'crisp',
  label: 'C — Crisp Tech',
  description: '快速精準、方向明確。幾乎不使用 bounce，適合高資訊密度的管理介面。',
  reveal: { duration: 0.2, ease: 'power3.out', y: 6, scaleFrom: 1 },
  stagger: { duration: 0.18, ease: 'power3.out', y: 8, scaleFrom: 1, each: 0.022, maxItems: 16 },
  // 案件面板刻意不縮到 0.14–0.3：使用者明確要求 2/5:3/5 面板「稍微放慢」。
  panel: { duration: 0.38, ease: 'power3.out', x: 10, scaleFrom: 1, leaveDuration: 0.2, leaveEase: 'power3.in' },
  feedback: { duration: 0.16, ease: 'power3.out', scaleTo: 1.03 },
  shake: { duration: 0.16, ease: 'power3.out', x: 4, repeat: 1 },
}

export const motionPresets: Record<MotionPresetId, MotionPreset> = {
  gentle: gentlePreset,
  donggu: dongguPreset,
  crisp: crispPreset,
}

export const motionPresetIds: readonly MotionPresetId[] = ['gentle', 'donggu', 'crisp']

/**
 * 正式採用決定（品牌改版 v1.0）：
 *
 *   Customer 前台        → B Donggu Friendly（親切、導購與完成狀態有記憶點）
 *   Admin 後台           → C Crisp Tech（快速、方向明確，配合高資訊密度）
 *   客服長對話／案件訊息／退貨申請／錯誤鄰近流程 → A Gentle Guidance
 *     這些畫面靠近售後與錯誤，不使用 back／elastic overshoot。
 *
 * dev 的 ?motion= 仍可覆寫，供比較用；production 沒有切換介面。
 */
export const customerDefaultMotionPresetId: MotionPresetId = 'donggu'
export const adminDefaultMotionPresetId: MotionPresetId = 'crisp'
export const sensitiveFlowMotionPresetId: MotionPresetId = 'gentle'

/** 沒有指定 App 時的保守後備（等同售後流程用的柔和方案）。 */
export const defaultMotionPresetId: MotionPresetId = sensitiveFlowMotionPresetId

export function isMotionPresetId(value: unknown): value is MotionPresetId {
  return typeof value === 'string' && (motionPresetIds as readonly string[]).includes(value)
}

export function resolveMotionPreset(id: MotionPresetId | undefined): MotionPreset {
  return motionPresets[id ?? defaultMotionPresetId]
}

import { computed, inject, ref, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import { defaultMotionPresetId, isMotionPresetId, resolveMotionPreset, sensitiveFlowMotionPresetId, type MotionPreset, type MotionPresetId } from './presets'

/**
 * 本輪 A／B／C 比較用的方案選擇 —— **只在 dev 有效**。
 *
 * 邊界：
 * - 不碰 vue-router：只讀 `location.search`，不註冊 route、不改 Router 契約。
 * - 不是正式產品功能：`import.meta.env.DEV` 為 false 時，
 *   `readPresetFromLocation` 根本不會被呼叫，永遠回傳 `defaultMotionPresetId`。
 * - production build 中 `isMotionExplorationEnabled` 是常數 false，
 *   Vite 會把整條讀取路徑與 dev 切換介面一起 tree-shake 掉。
 */

/** 打包期就能決定的常數，讓 production 直接 dead-code-eliminate 實驗路徑。 */
export const isMotionExplorationEnabled: boolean = import.meta.env.DEV === true

/** dev 專用的 query 參數名。正式功能不得依賴這個字串。 */
export const MOTION_QUERY_KEY = 'motion'

/**
 * 純函式版本，方便測試：給定 search 字串與是否為 dev，回傳應該使用的 preset id。
 * dev 為 false 時一律回傳預設值，即使 search 帶了參數。
 */
export function resolveMotionPresetId(search: string, dev: boolean): MotionPresetId {
  if (!dev) {
    return defaultMotionPresetId
  }

  try {
    const value = new URLSearchParams(search).get(MOTION_QUERY_KEY)
    return isMotionPresetId(value) ? value : defaultMotionPresetId
  }
  catch {
    return defaultMotionPresetId
  }
}

function readPresetFromLocation(appDefault: MotionPresetId): MotionPresetId {
  if (typeof window === 'undefined') {
    return appDefault
  }

  const fromQuery = resolveMotionPresetId(window.location.search, true)
  // resolveMotionPresetId 找不到有效值時回傳中性後備，這裡再退回該 App 的正式方案。
  return fromQuery === defaultMotionPresetId && !window.location.search.includes(MOTION_QUERY_KEY)
    ? appDefault
    : fromQuery
}

export interface MotionPresetSelection {
  presetId: Ref<MotionPresetId>
  preset: ComputedRef<MotionPreset>
  /** dev 才會是 true；production build 固定 false。 */
  canSwitch: boolean
  /** dev 專用：切換方案並更新網址（不觸發 route 導覽）。 */
  select: (id: MotionPresetId) => void
}

/**
 * @param appDefault 該 App 正式採用的方案（Customer = donggu、Admin = crisp）。
 *   dev 的 ?motion= 可覆寫它，供三方案比較；production 永遠使用 appDefault。
 */
export function useMotionPresetSelection(
  appDefault: MotionPresetId = defaultMotionPresetId,
): MotionPresetSelection {
  const presetId = ref<MotionPresetId>(
    isMotionExplorationEnabled ? readPresetFromLocation(appDefault) : appDefault,
  )

  function select(id: MotionPresetId): void {
    if (!isMotionExplorationEnabled) {
      return
    }

    presetId.value = id

    if (typeof window === 'undefined' || typeof window.history?.replaceState !== 'function') {
      return
    }

    // 用 history.replaceState 而不是 router.replace：不進入 vue-router 的歷史堆疊，
    // 也不影響瀏覽器返回行為。
    const url = new URL(window.location.href)
    url.searchParams.set(MOTION_QUERY_KEY, id)
    window.history.replaceState(window.history.state, '', url)
  }

  return {
    presetId,
    preset: computed(() => resolveMotionPreset(presetId.value)),
    canSwitch: isMotionExplorationEnabled,
    select,
  }
}

/**
 * App.vue 提供、頁面注入。沒有 provider 時（例如單元測試單獨掛載頁面）
 * 退回預設 preset，頁面不會因此壞掉。
 */
export const motionPresetKey: InjectionKey<ComputedRef<MotionPreset>> = Symbol('doselect-motion-preset')

export function useInjectedMotionPreset(): ComputedRef<MotionPreset> {
  return inject(motionPresetKey, computed(() => resolveMotionPreset(defaultMotionPresetId)))
}

/**
 * 售後與錯誤鄰近流程專用：一律回傳 Gentle Guidance，不受 App 預設或
 * dev 覆寫影響 —— 客服長對話、案件訊息列表與退貨申請不使用 back／elastic overshoot。
 */
export function useSensitiveMotionPreset(): ComputedRef<MotionPreset> {
  return computed(() => resolveMotionPreset(sensitiveFlowMotionPresetId))
}

export {
  createFeedbackPulse,
  createFieldShake,
  createIconDraw,
  createListStagger,
  createPageReveal,
  createPanelEnter,
  createPanelLeave,
  isInert,
  type MotionOptions,
  type MotionTarget,
} from './helpers'

export {
  crispPreset,
  defaultMotionPresetId,
  dongguPreset,
  gentlePreset,
  isMotionPresetId,
  motionPresetIds,
  motionPresets,
  resolveMotionPreset,
  type FeedbackSpec,
  type MotionPreset,
  type MotionPresetId,
  type PanelSpec,
  type RevealSpec,
  type ShakeSpec,
  type StaggerSpec,
} from './presets'

export {
  detectReducedMotion,
  useMotionPreference,
  useMotionScope,
  type MotionScopeBuilder,
  type MotionScopeContext,
  type UseMotionScopeResult,
} from './useMotionScope'

// MotionDevSwitcher 刻意「不」從這裡再匯出：它必須由呼叫端以
// import.meta.env.DEV 包住的動態 import 取得，production build 才能整個移除。

export {
  isMotionExplorationEnabled,
  MOTION_QUERY_KEY,
  resolveMotionPresetId,
  useMotionPresetSelection,
  type MotionPresetSelection,
} from './useMotionPresetSelection'

export {
  motionPresetKey,
  useInjectedMotionPreset,
} from './useMotionPresetSelection'

export {
  adminDefaultMotionPresetId,
  customerDefaultMotionPresetId,
  sensitiveFlowMotionPresetId,
} from './presets'

export { useSensitiveMotionPreset } from './useMotionPresetSelection'

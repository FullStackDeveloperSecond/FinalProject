/**
 * 組長 PR #35 round-3 review, P2-6: both the customer-web build-list/share pages and the
 * admin-web rule-management test tool used to render `finding.messageKey` (e.g.
 * "compatibility.cpu_socket_mismatch") straight to the user — an internal identifier, not
 * something a shopper or admin can act on. Centralized here (not duplicated per app) since both
 * apps render the same `CompatibilityFindingDto` shape from the same backend.
 *
 * Keys mirror every `messageKey` the canonical evaluator can emit
 * (`DoSelect.Domain.Builds.CompatibilityEvaluator`) plus the disabled-rule relabeling
 * (`EfCompatibilityCheckService.ApplyDisabledRules`). A message this module doesn't recognize
 * falls back to the raw key (still better than a blank string, and visibly flags a real gap here
 * to fix) rather than throwing or hiding the finding.
 */

type CompatibilityFacts = Record<string, unknown>

function stringFact(facts: CompatibilityFacts | undefined, key: string): string | null {
  const value = facts?.[key]
  return typeof value === 'string' ? value : null
}

function numberFact(facts: CompatibilityFacts | undefined, key: string): number | null {
  const value = facts?.[key]
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value
  }
  if (typeof value === 'string' && value.trim() !== '' && Number.isFinite(Number(value))) {
    return Number(value)
  }
  return null
}

// Only these specific, already-whitelisted fact keys are ever read and interpolated — never the
// whole `facts` object, and never anything not explicitly named below.
const FACT_TEMPLATES: Record<string, (facts: CompatibilityFacts) => string | null> = {
  'compatibility.cpu_socket_mismatch': (facts) => {
    const cpuSocket = stringFact(facts, 'cpuSocket')
    const boardSocket = stringFact(facts, 'boardSocket')
    return cpuSocket && boardSocket ? `CPU 腳位（${cpuSocket}）與主機板腳位（${boardSocket}）不符。` : null
  },
  'compatibility.memory_type_mismatch': (facts) => {
    const boardType = stringFact(facts, 'boardMemoryType')
    return boardType ? `記憶體類型與主機板支援的 ${boardType} 不符。` : null
  },
  'compatibility.memory_slots_exceeded': (facts) => {
    const available = numberFact(facts, 'availableSlots')
    const used = numberFact(facts, 'usedSlots')
    return available !== null && used !== null
      ? `記憶體需要 ${used} 個插槽，但主機板只有 ${available} 個。`
      : null
  },
  'compatibility.memory_capacity_exceeded': (facts) => {
    const maximum = numberFact(facts, 'maximumGb')
    const selected = numberFact(facts, 'selectedGb')
    return maximum !== null && selected !== null
      ? `記憶體總容量 ${selected}GB 超過主機板支援的 ${maximum}GB 上限。`
      : null
  },
  'compatibility.psu_capacity_insufficient': (facts) => {
    const required = numberFact(facts, 'requiredWatts')
    const rated = numberFact(facts, 'ratedWatts')
    return required !== null && rated !== null
      ? `目前組裝建議至少 ${required}W，但電源供應器額定僅 ${rated}W。`
      : null
  },
  'compatibility.psu_reserve_low': (facts) => {
    const draw = numberFact(facts, 'estimatedDrawWatts')
    const rated = numberFact(facts, 'ratedWatts')
    return draw !== null && rated !== null
      ? `預估用電 ${draw}W，電源供應器額定 ${rated}W，升級空間有限。`
      : null
  },
  'compatibility.gpu_too_long': (facts) => {
    const selected = numberFact(facts, 'selectedMm')
    const maximum = numberFact(facts, 'maximumMm')
    return selected !== null && maximum !== null
      ? `顯示卡長度 ${selected}mm 超過機殼可容納的 ${maximum}mm。`
      : null
  },
  'compatibility.cooler_too_tall': (facts) => {
    const selected = numberFact(facts, 'selectedMm')
    const maximum = numberFact(facts, 'maximumMm')
    return selected !== null && maximum !== null
      ? `散熱器高度 ${selected}mm 超過機殼可容納的 ${maximum}mm。`
      : null
  },
}

const PLAIN_LABELS: Record<string, string> = {
  // Every key in FACT_TEMPLATES must ALSO have a plain label here: a fact template returns null
  // whenever the facts it needs are missing or the wrong type, and without a fallback entry
  // describeCompatibilityMessage would drop through to returning the raw messageKey — exactly the
  // failure this module exists to prevent. cpu_socket_mismatch was the one key missing it.
  'compatibility.cpu_socket_mismatch': 'CPU 腳位與主機板腳位不符。',
  'compatibility.required_component_invalid': '此分類缺少必要元件，或選擇的數量不正確。',
  'compatibility.required_component_missing': '缺少必要元件，無法完整判斷相容性。',
  'compatibility.cpu_generation_not_supported': '主機板晶片組不支援此 CPU 世代。',
  'compatibility.chipset_mapping_missing': '找不到此 CPU 世代與晶片組的相容性資料，需人工確認。',
  'compatibility.bios_update_may_be_required': '此組合可能需要先更新主機板 BIOS 才能支援。',
  'compatibility.memory_type_mismatch': '記憶體規格與主機板支援的類型不符。',
  'compatibility.memory_slots_exceeded': '記憶體數量超過主機板插槽數。',
  'compatibility.memory_slots_low': '安裝後剩餘記憶體插槽數量偏低。',
  'compatibility.memory_capacity_exceeded': '記憶體總容量超過主機板支援上限。',
  'compatibility.motherboard_form_factor_unsupported': '主機板尺寸不受此機殼支援。',
  'compatibility.gpu_too_long': '顯示卡長度超過機殼可容納空間。',
  'compatibility.gpu_clearance_low': '顯示卡安裝後的剩餘空間偏低。',
  'compatibility.cooler_socket_unsupported': '散熱器不支援此 CPU 腳位。',
  'compatibility.cooler_too_tall': '散熱器高度超過機殼可容納空間。',
  'compatibility.cooler_clearance_low': '散熱器安裝後的剩餘空間偏低。',
  'compatibility.psu_form_factor_unsupported': '電源供應器尺寸不受此機殼支援。',
  'compatibility.storage_ports_exceeded': '儲存裝置數量超過主機板可用埠數。',
  'compatibility.storage_ports_low': '安裝後剩餘可用儲存埠數量偏低。',
  'compatibility.psu_capacity_insufficient': '電源供應器瓦數不足以支援目前組裝。',
  'compatibility.psu_reserve_low': '電源供應器餘裕偏低，建議升級以留有未來升級空間。',
  'compatibility.psu_connectors_insufficient': '電源供應器的供電接頭數量或種類不足。',
  'compatibility.required_data_missing': '缺少計算所需的規格資料，無法判斷相容性。',
  'compatibility.rule_disabled': '此規則目前已由管理員停用，結果僅供參考，仍需人工確認。',
}

/**
 * Translates a finding's `messageKey` (optionally using its `facts`, if a fact-aware template
 * exists and the specific facts it needs are present and well-typed) into a zh-TW message a user
 * can actually act on. Falls back to the plain label when no fact template applies, and to the
 * raw key itself when the key is entirely unrecognized.
 */
export function describeCompatibilityMessage(messageKey: string, facts?: CompatibilityFacts): string {
  const templated = facts ? FACT_TEMPLATES[messageKey]?.(facts) : null
  if (templated) {
    return templated
  }
  return PLAIN_LABELS[messageKey] ?? messageKey
}

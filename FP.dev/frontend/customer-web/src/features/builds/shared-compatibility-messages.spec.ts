import { describeCompatibilityMessage } from '@doselect/web-shared/compatibility'
import { describe, expect, it } from 'vitest'

/**
 * 組長 PR #35 round-3 review, P2-6: CompatibilityFindingsList.vue／CompatibilityRulesPage.vue's
 * test tool both used to render `finding.messageKey` (an internal identifier like
 * "compatibility.cpu_socket_mismatch") straight to the user. These tests cover one representative
 * finding from each severity CompatibilityEvaluator can emit (blocked／warning／insufficientData／
 * ruleDisabled), confirming a translated, user-actionable zh-TW message is shown instead of the
 * raw key — plus the fallback behaviour when a key or its facts aren't recognized.
 */
describe('describeCompatibilityMessage', () => {
  it('translates a blocked finding using its facts (CPU socket mismatch)', () => {
    const message = describeCompatibilityMessage('compatibility.cpu_socket_mismatch', {
      cpuSocket: 'AM5',
      boardSocket: 'LGA1700',
    })

    expect(message).toBe('CPU 腳位（AM5）與主機板腳位（LGA1700）不符。')
    expect(message).not.toContain('compatibility.')
  })

  it('translates a warning finding using its facts (PSU reserve low)', () => {
    const message = describeCompatibilityMessage('compatibility.psu_reserve_low', {
      estimatedDrawWatts: 550,
      ratedWatts: 600,
    })

    expect(message).toBe('預估用電 550W，電源供應器額定 600W，升級空間有限。')
  })

  it('translates an insufficientData finding to its plain label (no fact template exists)', () => {
    const message = describeCompatibilityMessage('compatibility.required_component_missing')

    expect(message).toBe('缺少必要元件，無法完整判斷相容性。')
  })

  it('translates a ruleDisabled finding to its plain label', () => {
    const message = describeCompatibilityMessage('compatibility.rule_disabled')

    expect(message).toBe('此規則目前已由管理員停用，結果僅供參考，仍需人工確認。')
  })

  it('falls back to the plain label when a fact template exists but the required facts are missing or malformed', () => {
    // memory_type_mismatch 同時有 FACT_TEMPLATES 與 PLAIN_LABELS 兩種版本——facts 缺漏或型別不對
    // 時應該退回 PLAIN_LABELS，而不是丟出例外或顯示帶著 undefined 的殘缺模板字串。
    const noFacts = describeCompatibilityMessage('compatibility.memory_type_mismatch')
    const emptyFacts = describeCompatibilityMessage('compatibility.memory_type_mismatch', {})
    const malformedFacts = describeCompatibilityMessage('compatibility.memory_type_mismatch', {
      boardMemoryType: 42,
    })

    expect(noFacts).toBe('記憶體規格與主機板支援的類型不符。')
    expect(emptyFacts).toBe('記憶體規格與主機板支援的類型不符。')
    expect(malformedFacts).toBe('記憶體規格與主機板支援的類型不符。')
  })

  it('falls back to the raw key itself when the key is entirely unrecognized', () => {
    const message = describeCompatibilityMessage('compatibility.brand_new_unmapped_key')

    expect(message).toBe('compatibility.brand_new_unmapped_key')
  })

  /**
   * Self-review finding: a key that has a fact template but NO plain label leaks the raw
   * messageKey to the user whenever its facts are missing or malformed — the exact failure this
   * module exists to prevent. `compatibility.cpu_socket_mismatch` was shipped in that state.
   * This covers every key the backend can emit (CompatibilityEvaluator + ApplyDisabledRules) so a
   * future key added to only one of the two maps fails here instead of in front of a shopper.
   */
  it.each([
    'compatibility.required_component_invalid',
    'compatibility.required_component_missing',
    'compatibility.cpu_socket_mismatch',
    'compatibility.cpu_generation_not_supported',
    'compatibility.chipset_mapping_missing',
    'compatibility.bios_update_may_be_required',
    'compatibility.memory_type_mismatch',
    'compatibility.memory_slots_exceeded',
    'compatibility.memory_slots_low',
    'compatibility.memory_capacity_exceeded',
    'compatibility.motherboard_form_factor_unsupported',
    'compatibility.gpu_too_long',
    'compatibility.gpu_clearance_low',
    'compatibility.cooler_socket_unsupported',
    'compatibility.cooler_too_tall',
    'compatibility.cooler_clearance_low',
    'compatibility.psu_form_factor_unsupported',
    'compatibility.storage_ports_exceeded',
    'compatibility.storage_ports_low',
    'compatibility.psu_capacity_insufficient',
    'compatibility.psu_reserve_low',
    'compatibility.psu_connectors_insufficient',
    'compatibility.required_data_missing',
    'compatibility.rule_disabled',
  ])('never leaks the raw key %s, with or without facts', (messageKey) => {
    expect(describeCompatibilityMessage(messageKey)).not.toBe(messageKey)
    expect(describeCompatibilityMessage(messageKey, {})).not.toBe(messageKey)
    // Facts present but of the wrong runtime type — the fact template must decline, and the
    // plain label must still cover it.
    expect(describeCompatibilityMessage(messageKey, {
      cpuSocket: 1, boardSocket: 1, boardMemoryType: 1, availableSlots: 'x', usedSlots: 'x',
      maximumGb: 'x', selectedGb: 'x', requiredWatts: 'x', ratedWatts: 'x',
      estimatedDrawWatts: 'x', selectedMm: 'x', maximumMm: 'x',
    })).not.toBe(messageKey)
  })
})

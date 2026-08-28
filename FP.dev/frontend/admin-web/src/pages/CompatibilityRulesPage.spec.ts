import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { CompatibilityRuleListDto } from '../features/compatibilityRules/types'

const mockListCompatibilityRules = vi.fn()
const mockUpdateWarningSetting = vi.fn()
const mockSetRuleActivation = vi.fn()
const mockTestCompatibilityRules = vi.fn()

vi.mock('../features/compatibilityRules/api', () => ({
  listCompatibilityRules: () => mockListCompatibilityRules(),
  updateWarningSetting: (...args: unknown[]) => mockUpdateWarningSetting(...args),
  setRuleActivation: (...args: unknown[]) => mockSetRuleActivation(...args),
  testCompatibilityRules: (...args: unknown[]) => mockTestCompatibilityRules(...args),
}))

const { default: CompatibilityRulesPage } = await import('./CompatibilityRulesPage.vue')

function ruleList(): CompatibilityRuleListDto {
  return {
    settingsVersion: 7,
    rules: [
      {
        ruleCode: 'CPU_SOCKET',
        isActive: true,
        activationRowVersion: 'ACT-CPU-1',
        warningSetting: null,
      },
      {
        ruleCode: 'PSU_CAPACITY',
        isActive: true,
        activationRowVersion: 'ACT-PSU-1',
        warningSetting: {
          settingCode: 'PsuReserveWarningPercent', value: 35, minValue: 30, maxValue: 50, defaultValue: 35,
          rowVersion: 'WARN-PSU-1',
        },
      },
    ],
  }
}

function mountPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return mount(CompatibilityRulesPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
}

beforeEach(() => {
  mockListCompatibilityRules.mockReset()
  mockUpdateWarningSetting.mockReset()
  mockSetRuleActivation.mockReset()
  mockTestCompatibilityRules.mockReset()
  mockListCompatibilityRules.mockResolvedValue(ruleList())
})

describe('CompatibilityRulesPage — RowVersion concurrency (DEC-BATCH-026 DEC-P309/P311)', () => {
  it('submits the specific rule\'s own warningSetting.rowVersion, not the whole-ruleset settingsVersion', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    const reasonInput = wrapper.find('input[aria-label="調整理由"]')
    await reasonInput.setValue('季節性調整')
    const updateButton = wrapper.findAll('button').find((button) => button.text() === '更新門檻')
    await updateButton!.trigger('click')

    await vi.waitFor(() => expect(mockUpdateWarningSetting).toHaveBeenCalledWith(
      'PSU_CAPACITY',
      expect.objectContaining({ rowVersion: 'WARN-PSU-1', reason: '季節性調整' }),
    ))
    const [, request] = mockUpdateWarningSetting.mock.calls[0]!
    expect(request).not.toHaveProperty('settingsVersion')
  })

  it('submits the specific rule\'s own activationRowVersion captured when the dialog opened, not the whole-ruleset settingsVersion', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU_SOCKET'))

    const toggleButtons = wrapper.findAll('button').filter((button) => button.text() === '停用')
    await toggleButtons[0]!.trigger('click')

    const reasonBox = wrapper.find('#confirm-dialog-reason')
    await reasonBox.setValue('規則暫時停用以配合活動')
    const confirmButton = wrapper.findAll('button').find((button) => button.text() === '確認')
    await confirmButton!.trigger('click')

    await vi.waitFor(() => expect(mockSetRuleActivation).toHaveBeenCalledWith(
      'CPU_SOCKET',
      expect.objectContaining({ isActive: false, rowVersion: 'ACT-CPU-1', reason: '規則暫時停用以配合活動' }),
    ))
    const [, request] = mockSetRuleActivation.mock.calls[0]!
    expect(request).not.toHaveProperty('settingsVersion')
  })

  /**
   * 組長 PR #35 round-2 review, P2-7: the confirmation dialog used to claim a disabled rule
   * "不會再擋下購買" (won't block purchase anymore) — but BuildDetailPage.vue's cartBlockReason and
   * SharedBuildPage's canAddToCart both treat a `ruleDisabled` finding as a blocking condition
   * (需先確認狀態才能加入購物車), the opposite of what this text told the admin. An admin relying on
   * this description before disabling a high-risk rule would be misled about the real effect.
   */
  it('does not claim a disabled rule stops blocking purchase — it still blocks pending manual confirmation', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU_SOCKET'))

    const toggleButtons = wrapper.findAll('button').filter((button) => button.text() === '停用')
    await toggleButtons[0]!.trigger('click')

    expect(wrapper.text()).not.toContain('不會再擋下購買')
    expect(wrapper.text()).toContain('仍會')
    expect(wrapper.text()).toContain('人工確認')
  })
})

describe('CompatibilityRulesPage — test-tool rule selection (組長 PR #35 review, item 6)', () => {
  it('passes only the checked rule codes to the test call, and null when none are checked', async () => {
    mockTestCompatibilityRules.mockResolvedValue({
      overall: 'compatible', results: [], settingsVersion: 7, evaluatedAtUtc: new Date().toISOString(),
    })
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('限定測試規則'))

    const skuInput = wrapper.find('input[aria-label="SKU PublicId"]')
    await skuInput.setValue('11111111-1111-1111-1111-111111111111')
    const addItemButton = wrapper.findAll('button').find((button) => button.text() === '加入項目')
    await addItemButton!.trigger('click')

    const ruleCheckbox = wrapper
      .findAll('.compatibility-rules-page__rule-select input[type="checkbox"]')[0]
    await ruleCheckbox.setValue(true)

    const runButton = wrapper.findAll('button').find((button) => button.text() === '執行測試')
    await runButton!.trigger('click')

    await vi.waitFor(() => expect(mockTestCompatibilityRules).toHaveBeenCalledWith(
      expect.objectContaining({ ruleCodes: ['CPU_SOCKET'] }),
    ))
  })

  it('caps the test item quantity input at 8, matching the 1–8 contract (not the old 99)', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU_SOCKET'))

    const quantityInput = wrapper.find('.compatibility-rules-page__add-row input[type="number"]')
    expect(quantityInput.attributes('max')).toBe('8')
  })
})

describe('CompatibilityRulesPage — draft warning-setting input (組長 PR #35 review, item 6)', () => {
  it('applies the rule\'s own min/max as constraints on the draft threshold input', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    await wrapper.find('.compatibility-rules-page__checkbox input').setValue(true)
    await flushPromises()

    const draftInput = wrapper.find('.compatibility-rules-page__draft-settings input')
    expect(draftInput.attributes('min')).toBe('30')
    expect(draftInput.attributes('max')).toBe('50')
  })

  it('drops a cleared/non-numeric draft threshold value instead of sending NaN', async () => {
    mockTestCompatibilityRules.mockResolvedValue({
      overall: 'compatible', results: [], settingsVersion: 7, evaluatedAtUtc: new Date().toISOString(),
    })
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    await wrapper.find('.compatibility-rules-page__checkbox input').setValue(true)
    await flushPromises()

    const draftInput = wrapper.find('.compatibility-rules-page__draft-settings input')
    await draftInput.setValue('')
    await draftInput.trigger('input')

    const skuInput = wrapper.find('input[aria-label="SKU PublicId"]')
    await skuInput.setValue('11111111-1111-1111-1111-111111111111')
    const addItemButton = wrapper.findAll('button').find((button) => button.text() === '加入項目')
    await addItemButton!.trigger('click')

    const runButton = wrapper.findAll('button').find((button) => button.text() === '執行測試')
    await runButton!.trigger('click')

    await vi.waitFor(() => expect(mockTestCompatibilityRules).toHaveBeenCalled())
    const [request] = mockTestCompatibilityRules.mock.calls[0]!
    const draftSettings = (request as { draftWarningSettings: Record<string, number> | null }).draftWarningSettings
    expect(draftSettings).not.toBeNull()
    expect(Object.values(draftSettings!).every((value) => Number.isFinite(value))).toBe(true)
  })

  /**
   * 組長 PR #35 round-2 review, P2-8: dropped a cleared/non-numeric value, but never checked the
   * rule's own min/max — an in-range-looking but out-of-bounds number (e.g. above the max) still
   * reached draftWarningSettings and, from there, the request body.
   */
  it('drops an out-of-range draft threshold value (finite, but outside min/max)', async () => {
    mockTestCompatibilityRules.mockResolvedValue({
      overall: 'compatible', results: [], settingsVersion: 7, evaluatedAtUtc: new Date().toISOString(),
    })
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    await wrapper.find('.compatibility-rules-page__checkbox input').setValue(true)
    await flushPromises()

    // PSU_CAPACITY's range is 30–50; 999 is finite but well outside it.
    const draftInput = wrapper.find('.compatibility-rules-page__draft-settings input')
    await draftInput.setValue('999')
    await draftInput.trigger('input')

    const skuInput = wrapper.find('input[aria-label="SKU PublicId"]')
    await skuInput.setValue('11111111-1111-1111-1111-111111111111')
    const addItemButton = wrapper.findAll('button').find((button) => button.text() === '加入項目')
    await addItemButton!.trigger('click')

    const runButton = wrapper.findAll('button').find((button) => button.text() === '執行測試')
    await runButton!.trigger('click')

    await vi.waitFor(() => expect(mockTestCompatibilityRules).toHaveBeenCalled())
    const [request] = mockTestCompatibilityRules.mock.calls[0]!
    const draftSettings = (request as { draftWarningSettings: Record<string, number> | null }).draftWarningSettings
    expect(Object.values(draftSettings ?? {})).not.toContain(999)
  })
})

describe('CompatibilityRulesPage — front-end bounds validation before submit (組長 PR #35 round-2 review, P2-8)', () => {
  it('disables "加入項目" for a non-integer or out-of-1–8-range quantity, and does not add the item', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU_SOCKET'))

    const skuInput = wrapper.find('input[aria-label="SKU PublicId"]')
    await skuInput.setValue('11111111-1111-1111-1111-111111111111')
    const quantityInput = wrapper.find('.compatibility-rules-page__add-row input[type="number"]')
    const addItemButton = wrapper.findAll('button').find((button) => button.text() === '加入項目')!

    await quantityInput.setValue('9')
    expect(addItemButton.attributes('disabled')).toBeDefined()
    await addItemButton.trigger('click')
    expect(wrapper.find('.compatibility-rules-page__add-row').text()).toContain('數量須為 1–8 之間的整數')

    await quantityInput.setValue('3.5')
    expect(addItemButton.attributes('disabled')).toBeDefined()

    await quantityInput.setValue('4')
    expect(addItemButton.attributes('disabled')).toBeUndefined()
    await addItemButton.trigger('click')

    const runButton = wrapper.findAll('button').find((button) => button.text() === '執行測試')!
    mockTestCompatibilityRules.mockResolvedValue({
      overall: 'compatible', results: [], settingsVersion: 7, evaluatedAtUtc: new Date().toISOString(),
    })
    await runButton.trigger('click')
    await vi.waitFor(() => expect(mockTestCompatibilityRules).toHaveBeenCalledWith(
      expect.objectContaining({ items: [{ skuPublicId: '11111111-1111-1111-1111-111111111111', quantity: 4 }] }),
    ))
  })

  it('disables "更新門檻" for an out-of-range formal threshold value, even with a reason filled in', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    const valueInput = wrapper.find('input[aria-label="警告門檻數值"]')
    const reasonInput = wrapper.find('input[aria-label="調整理由"]')
    await reasonInput.setValue('季節性調整')
    const updateButton = wrapper.findAll('button').find((button) => button.text() === '更新門檻')!

    // PSU_CAPACITY's range is 30–50.
    await valueInput.setValue('999')
    await valueInput.trigger('input')
    expect(updateButton.attributes('disabled')).toBeDefined()
    await updateButton.trigger('click')
    expect(mockUpdateWarningSetting).not.toHaveBeenCalled()

    await valueInput.setValue('')
    await valueInput.trigger('input')
    expect(updateButton.attributes('disabled')).toBeDefined()

    await valueInput.setValue('40')
    await valueInput.trigger('input')
    expect(updateButton.attributes('disabled')).toBeUndefined()
    await updateButton.trigger('click')
    await vi.waitFor(() => expect(mockUpdateWarningSetting).toHaveBeenCalledWith(
      'PSU_CAPACITY', expect.objectContaining({ value: 40 }),
    ))
  })
})

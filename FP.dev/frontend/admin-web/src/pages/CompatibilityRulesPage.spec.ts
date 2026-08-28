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
})

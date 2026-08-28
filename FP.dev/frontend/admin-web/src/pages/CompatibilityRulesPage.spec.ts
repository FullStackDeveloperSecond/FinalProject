import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'
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

/**
 * 組長 PR #35 round-3 review, P2-3: the page now reads the signed-in administrator's roles to
 * decide whether to offer the SuperAdmin-only activation controls, so every mount needs a real
 * Pinia with a seeded session. Defaults to SuperAdmin so the pre-existing tests (which all
 * exercise behaviour unrelated to role gating) keep covering what they were written for; the
 * role-gating tests below pass an explicit role instead.
 */
function mountPage(roles: string[] = ['SuperAdmin']) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const pinia = createPinia()
  setActivePinia(pinia)
  useAdminAuthStore().session = {
    isAuthenticated: true,
    user: {
      publicId: 'admin-1',
      displayName: '測試管理員',
      emailMasked: 'a***@example.test',
      emailVerified: true,
      locale: 'zh-TW',
      roles,
    },
    expiresAtUtc: null,
    requiresTwoFactor: false,
  }
  const wrapper = mount(CompatibilityRulesPage, { global: { plugins: [[VueQueryPlugin, { queryClient }], pinia] } })
  // Returned so a test can drive a background refetch the same way the real app does — any
  // mutation's onSuccess invalidates this key (useCompatibilityRules.ts), so this is the actual
  // production path by which a newer rowVersion arrives mid-edit, not a synthetic one.
  return Object.assign(wrapper, {
    refetchRules: () => queryClient.invalidateQueries({ queryKey: ['compatibility-rules'] }),
  })
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
   * 組長 PR #35 round-2 review, P2-8 established that an out-of-range draft threshold must not
   * reach the request body. Round-3 review, P2-4 tightened the *mechanism*: silently dropping it
   * meant the admin pressed 執行測試, got a result computed with the rule's stored value instead of
   * the number they typed, and had no way to tell the difference. It now blocks the run outright
   * and says why, rather than quietly running something other than what was asked for.
   */
  it('blocks 執行測試 (rather than silently dropping the value) for an out-of-range draft threshold', async () => {
    mockTestCompatibilityRules.mockResolvedValue({
      overall: 'compatible', results: [], settingsVersion: 7, evaluatedAtUtc: new Date().toISOString(),
    })
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    await wrapper.find('.compatibility-rules-page__checkbox input').setValue(true)
    await flushPromises()

    const skuInput = wrapper.find('input[aria-label="SKU PublicId"]')
    await skuInput.setValue('11111111-1111-1111-1111-111111111111')
    const addItemButton = wrapper.findAll('button').find((button) => button.text() === '加入項目')
    await addItemButton!.trigger('click')

    const runButton = () => wrapper.findAll('button').find((button) => button.text() === '執行測試')!
    expect(runButton().attributes('disabled')).toBeUndefined()

    // PSU_CAPACITY's range is 30–50; 999 is finite but well outside it.
    const draftInput = wrapper.find('.compatibility-rules-page__draft-settings input')
    await draftInput.setValue('999')
    await draftInput.trigger('input')

    expect(runButton().attributes('disabled')).toBeDefined()
    await runButton().trigger('click')
    await flushPromises()
    expect(mockTestCompatibilityRules).not.toHaveBeenCalled()

    // Correcting the value back into range re-enables the run.
    await draftInput.setValue('40')
    await draftInput.trigger('input')
    expect(runButton().attributes('disabled')).toBeUndefined()

    await runButton().trigger('click')
    await vi.waitFor(() => expect(mockTestCompatibilityRules).toHaveBeenCalledTimes(1))
    const [request] = mockTestCompatibilityRules.mock.calls[0]!
    const draftSettings = (request as { draftWarningSettings: Record<string, number> | null }).draftWarningSettings
    expect(Object.values(draftSettings ?? {})).toContain(40)
    expect(Object.values(draftSettings ?? {})).not.toContain(999)
  })

  /**
   * 組長 PR #35 round-3 review, P2-4: the draft input was uncontrolled, so a rejected value was
   * dropped from state while the box kept displaying it — the admin saw "999" on screen and had no
   * signal that it wasn't what would actually be tested. It is now controlled and shows the
   * validation error inline.
   */
  it('keeps showing the rejected value the admin actually typed, with an inline error', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    await wrapper.find('.compatibility-rules-page__checkbox input').setValue(true)
    await flushPromises()

    const draftInput = wrapper.find('.compatibility-rules-page__draft-settings input')
    await draftInput.setValue('999')
    await draftInput.trigger('input')

    expect((draftInput.element as HTMLInputElement).value).toBe('999')
    expect(wrapper.find('.compatibility-rules-page__draft-settings').text()).toContain('30')
    expect(wrapper.find('.compatibility-rules-page__draft-settings').text()).toContain('50')
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

describe('CompatibilityRulesPage — warning-draft RowVersion staleness (組長 PR #35 round-3 review, P1-1)', () => {
  /**
   * The draft used to hold only the edited value, while `rowVersion` was read fresh off the
   * currently-rendered rule at submit time. After a background refetch brought a newer
   * rowVersion, an admin who had already typed a value would submit THEIR stale number paired
   * with the SERVER'S fresh rowVersion — which looks like a perfectly legitimate update to the
   * backend, so optimistic concurrency silently fails to protect anything and the other admin's
   * change is overwritten. The draft now records the rowVersion it was based on.
   */
  it('silently resyncs a clean draft when a refetch brings a newer rowVersion', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    const refreshed = ruleList()
    refreshed.rules[1]!.warningSetting = {
      settingCode: 'PsuReserveWarningPercent', value: 45, minValue: 30, maxValue: 50, defaultValue: 35,
      rowVersion: 'WARN-PSU-2',
    }
    mockListCompatibilityRules.mockResolvedValue(refreshed)
    await wrapper.refetchRules()

    // Untouched draft: no conflict warning, and the newer server value is simply adopted.
    await vi.waitFor(() => expect(
      (wrapper.find('input[aria-label="警告門檻數值"]').element as HTMLInputElement).value).toBe('45'))
    expect(wrapper.text()).not.toContain('已被其他管理員更新')

    const reasonInput = wrapper.find('input[aria-label="調整理由"]')
    await reasonInput.setValue('季節性調整')
    const updateButton = wrapper.findAll('button').find((button) => button.text() === '更新門檻')!
    await updateButton.trigger('click')

    // Submits against the FRESH rowVersion, because the draft was rebased onto it.
    await vi.waitFor(() => expect(mockUpdateWarningSetting).toHaveBeenCalledWith(
      'PSU_CAPACITY', expect.objectContaining({ rowVersion: 'WARN-PSU-2', value: 45 }),
    ))
  })

  it('surfaces a conflict (and blocks submit) when a refetch brings a newer rowVersion while the draft is dirty', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    // The admin edits first — this draft is now based on WARN-PSU-1.
    const valueInput = wrapper.find('input[aria-label="警告門檻數值"]')
    await valueInput.setValue('42')
    await valueInput.trigger('input')
    await wrapper.find('input[aria-label="調整理由"]').setValue('季節性調整')

    const refreshed = ruleList()
    refreshed.rules[1]!.warningSetting = {
      settingCode: 'PsuReserveWarningPercent', value: 48, minValue: 30, maxValue: 50, defaultValue: 35,
      rowVersion: 'WARN-PSU-2',
    }
    mockListCompatibilityRules.mockResolvedValue(refreshed)
    await wrapper.refetchRules()

    // The dirty edit is NOT silently rebased onto the new rowVersion — that is exactly the
    // stale-value/fresh-rowVersion pairing this fix exists to prevent.
    await vi.waitFor(() => expect(wrapper.text()).toContain('已被其他管理員更新'))
    expect((wrapper.find('input[aria-label="警告門檻數值"]').element as HTMLInputElement).value).toBe('42')

    const updateButton = wrapper.findAll('button').find((button) => button.text() === '更新門檻')!
    expect(updateButton.attributes('disabled')).toBeDefined()
    await updateButton.trigger('click')
    expect(mockUpdateWarningSetting).not.toHaveBeenCalled()
  })

  it('lets the admin reload the latest value, discarding the conflicted draft and re-enabling submit', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    const valueInput = wrapper.find('input[aria-label="警告門檻數值"]')
    await valueInput.setValue('42')
    await valueInput.trigger('input')

    const refreshed = ruleList()
    refreshed.rules[1]!.warningSetting = {
      settingCode: 'PsuReserveWarningPercent', value: 48, minValue: 30, maxValue: 50, defaultValue: 35,
      rowVersion: 'WARN-PSU-2',
    }
    mockListCompatibilityRules.mockResolvedValue(refreshed)
    await wrapper.refetchRules()
    await vi.waitFor(() => expect(wrapper.text()).toContain('已被其他管理員更新'))

    const reloadButton = wrapper.findAll('button').find((button) => button.text() === '重新載入最新值')!
    await reloadButton.trigger('click')
    await flushPromises()

    expect(wrapper.text()).not.toContain('已被其他管理員更新')
    expect((wrapper.find('input[aria-label="警告門檻數值"]').element as HTMLInputElement).value).toBe('48')

    await wrapper.find('input[aria-label="調整理由"]').setValue('季節性調整')
    const updateButton = wrapper.findAll('button').find((button) => button.text() === '更新門檻')!
    expect(updateButton.attributes('disabled')).toBeUndefined()
    await updateButton.trigger('click')
    await vi.waitFor(() => expect(mockUpdateWarningSetting).toHaveBeenCalledWith(
      'PSU_CAPACITY', expect.objectContaining({ rowVersion: 'WARN-PSU-2', value: 48 }),
    ))
  })
})

describe('CompatibilityRulesPage — activation role gating (組長 PR #35 round-3 review, P2-3)', () => {
  /**
   * 相容性規則後台設計.md: 規則整體啟停只允許 SuperAdmin. A CatalogManager could previously see and
   * click the 啟用／停用 button; the backend Policy would reject it, but offering a control that
   * always fails is confusing UX at best. Defense in depth alongside the route guard — neither
   * replaces the backend Policy.
   */
  it('offers the activation control to a SuperAdmin', async () => {
    const wrapper = mountPage(['SuperAdmin'])
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU_SOCKET'))

    expect(wrapper.findAll('button').some((button) => button.text() === '停用')).toBe(true)
  })

  it('hides the activation control from a CatalogManager and says why', async () => {
    const wrapper = mountPage(['CatalogManager'])
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU_SOCKET'))

    expect(wrapper.findAll('button').some((button) => button.text() === '停用')).toBe(false)
    expect(wrapper.findAll('button').some((button) => button.text() === '啟用')).toBe(false)
    expect(wrapper.text()).toContain('僅 SuperAdmin')
    expect(mockSetRuleActivation).not.toHaveBeenCalled()
  })

  it('still lets a CatalogManager edit warning thresholds (the gate is activation-only)', async () => {
    const wrapper = mountPage(['CatalogManager'])
    await vi.waitFor(() => expect(wrapper.text()).toContain('PSU_CAPACITY'))

    await wrapper.find('input[aria-label="調整理由"]').setValue('季節性調整')
    const updateButton = wrapper.findAll('button').find((button) => button.text() === '更新門檻')!
    expect(updateButton.attributes('disabled')).toBeUndefined()
    await updateButton.trigger('click')
    await vi.waitFor(() => expect(mockUpdateWarningSetting).toHaveBeenCalled())
  })
})

describe('CompatibilityRulesPage — human-readable finding messages (組長 PR #35 round-3 review, P2-6)', () => {
  it('renders a translated message for a test-result finding, not the raw messageKey', async () => {
    mockTestCompatibilityRules.mockResolvedValue({
      overall: 'blocked',
      settingsVersion: 7,
      evaluatedAtUtc: new Date().toISOString(),
      results: [{
        ruleCode: 'CPU_SOCKET',
        severity: 'blocked',
        messageKey: 'compatibility.cpu_socket_mismatch',
        subjectSkuPublicIds: [],
        facts: { cpuSocket: 'AM5', boardSocket: 'LGA1700' },
      }],
    })
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU_SOCKET'))

    await wrapper.find('input[aria-label="SKU PublicId"]').setValue('11111111-1111-1111-1111-111111111111')
    await wrapper.findAll('button').find((button) => button.text() === '加入項目')!.trigger('click')
    await wrapper.findAll('button').find((button) => button.text() === '執行測試')!.trigger('click')

    await vi.waitFor(() => expect(wrapper.find('.compatibility-rules-page__test-result').exists()).toBe(true))
    const resultText = wrapper.find('.compatibility-rules-page__test-result').text()
    expect(resultText).toContain('CPU 腳位（AM5）與主機板腳位（LGA1700）不符。')
    expect(resultText).not.toContain('compatibility.cpu_socket_mismatch')
  })
})

describe('CompatibilityRulesPage — test-tool item count ceiling (送出前文件核對發現)', () => {
  /**
   * 相容性規則後台設計.md documents the test tool's `items` as 1～20 筆 — the same bound
   * customer-web already enforces (features/builds/types.ts) and the backend rejects past. Only
   * the 1–8 per-item quantity was enforced here, so an admin could add a 21st row and only find
   * out when the whole run was rejected server-side.
   */
  async function addItems(wrapper: ReturnType<typeof mountPage>, count: number) {
    for (let index = 0; index < count; index += 1) {
      await wrapper.find('input[aria-label="SKU PublicId"]')
        .setValue(`1111111${index.toString().padStart(4, '0')}-1111-1111-1111-111111111111`)
      await wrapper.findAll('button').find((button) => button.text() === '加入項目')!.trigger('click')
    }
  }

  it('stops accepting new test items at 20 and says why', async () => {
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU_SOCKET'))

    await addItems(wrapper, 20)
    expect(wrapper.findAll('.compatibility-rules-page__test-items tbody tr')).toHaveLength(20)

    const addButton = wrapper.findAll('button').find((button) => button.text() === '加入項目')!
    expect(addButton.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('最多 20 筆')

    // A 21st attempt is a no-op, not a silently-oversized request.
    await addItems(wrapper, 1)
    expect(wrapper.findAll('.compatibility-rules-page__test-items tbody tr')).toHaveLength(20)
  })

  it('still allows 執行測試 at exactly 20 items (the boundary is inclusive)', async () => {
    mockTestCompatibilityRules.mockResolvedValue({
      overall: 'compatible', results: [], settingsVersion: 7, evaluatedAtUtc: new Date().toISOString(),
    })
    const wrapper = mountPage()
    await vi.waitFor(() => expect(wrapper.text()).toContain('CPU_SOCKET'))

    await addItems(wrapper, 20)
    const runButton = wrapper.findAll('button').find((button) => button.text() === '執行測試')!
    expect(runButton.attributes('disabled')).toBeUndefined()

    await runButton.trigger('click')
    await vi.waitFor(() => expect(mockTestCompatibilityRules).toHaveBeenCalledTimes(1))
    const [request] = mockTestCompatibilityRules.mock.calls[0]!
    expect((request as { items: unknown[] }).items).toHaveLength(20)
  })
})

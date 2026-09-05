import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'

const mockListReconciliationCases = vi.fn()
const mockAcknowledgeReconciliationCase = vi.fn()
const mockCloseReconciliationCase = vi.fn()

vi.mock('../features/inventory/api', () => ({
  listBalances: vi.fn(),
  listMovements: vi.fn(),
  listReservations: vi.fn(),
  releaseReservation: vi.fn(),
  listReconciliationCases: mockListReconciliationCases,
  acknowledgeReconciliationCase: mockAcknowledgeReconciliationCase,
  closeReconciliationCase: mockCloseReconciliationCase,
}))

const { default: InventoryReconciliationCasesPage } = await import('./InventoryReconciliationCasesPage.vue')

function reconciliationCase(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'c1',
    sku: { publicId: 's1', skuCode: 'SKU-1', nameZhTw: 'Widget' },
    status: 'Open',
    expectedOnHand: 8,
    actualOnHand: 0,
    expectedReserved: 0,
    actualReserved: 0,
    detectedAtUtc: '2026-09-05T00:00:00Z',
    acknowledgedBy: null,
    resolvedBy: null,
    resolutionMovementPublicId: null,
    resolutionReason: null,
    resolvedAtUtc: null,
    rowVersion: 'AAA=',
    ...overrides,
  }
}

function page(items: ReturnType<typeof reconciliationCase>[], overrides: Record<string, unknown> = {}) {
  return { items, pageNumber: 1, pageSize: 20, totalCount: items.length, totalPages: 1, ...overrides }
}

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const wrapper = mount(InventoryReconciliationCasesPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
  return { wrapper, queryClient }
}

function buttons(wrapper: ReturnType<typeof mountPage>['wrapper'], text: string) {
  return wrapper.findAll('button').filter((button) => button.text() === text)
}

describe('InventoryReconciliationCasesPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    mockListReconciliationCases.mockReset()
    mockAcknowledgeReconciliationCase.mockReset()
    mockCloseReconciliationCase.mockReset()
  })

  it('renders the cases with the Balance → ledger quantities', async () => {
    mockListReconciliationCases.mockResolvedValue(page([reconciliationCase({ expectedReserved: 2, actualReserved: 2 })]))

    const { wrapper } = mountPage()
    await flushPromises()

    const row = wrapper.find('tr[data-case-id="c1"]')
    expect(row.text()).toContain('SKU-1')
    expect(row.text()).toContain('8 → 0')
    // Equal quantities are shown once, not as "2 → 2".
    expect(row.text()).not.toContain('2 → 2')
    expect(mockListReconciliationCases).toHaveBeenCalledWith({ status: undefined, pageNumber: 1, pageSize: 20 })
  })

  /** 裁定 C1 鏡射到畫面：acknowledge 只有 Open；駁回／修正 Open 與 Acknowledged；已結案沒有動作。 */
  it('offers acknowledge only for Open cases and close actions only for Open or Acknowledged cases', async () => {
    mockListReconciliationCases.mockResolvedValue(page([
      reconciliationCase({ publicId: 'open' }),
      reconciliationCase({ publicId: 'ack', status: 'Acknowledged' }),
      reconciliationCase({ publicId: 'resolved', status: 'Resolved', resolutionReason: '實點確認為 0' }),
    ]))

    const { wrapper } = mountPage()
    await flushPromises()

    const open = wrapper.find('tr[data-case-id="open"]')
    expect(open.findAll('button').map((button) => button.text())).toEqual(['確認受理', '駁回', '修正庫存'])
    const acknowledged = wrapper.find('tr[data-case-id="ack"]')
    expect(acknowledged.findAll('button').map((button) => button.text())).toEqual(['駁回', '修正庫存'])
    const resolved = wrapper.find('tr[data-case-id="resolved"]')
    expect(resolved.findAll('button')).toHaveLength(0)
    expect(resolved.text()).toContain('實點確認為 0')
  })

  it('acknowledges with the row RowVersion', async () => {
    mockListReconciliationCases.mockResolvedValue(page([reconciliationCase({ rowVersion: 'BBB=' })]))
    mockAcknowledgeReconciliationCase.mockResolvedValueOnce(undefined)

    const { wrapper } = mountPage()
    await flushPromises()
    await buttons(wrapper, '確認受理')[0]!.trigger('click')
    await flushPromises()

    expect(mockAcknowledgeReconciliationCase).toHaveBeenCalledWith('c1', 'BBB=')
  })

  /** 裁定 D1：駁回只能選 false_positive／system_error／other；原因與說明都必填；送出前二次確認。 */
  it('dismisses with a whitelisted reason, a required note and a confirmation', async () => {
    mockListReconciliationCases.mockResolvedValue(page([reconciliationCase()]))
    mockCloseReconciliationCase.mockResolvedValueOnce(undefined)
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const { wrapper } = mountPage()
    await flushPromises()
    await buttons(wrapper, '駁回')[0]!.trigger('click')

    const options = wrapper.findAll('select[aria-label="原因代碼"] option').map((option) => option.attributes('value'))
    expect(options).toEqual(['', 'false_positive', 'system_error', 'other'])
    const confirmButton = () => buttons(wrapper, '確認駁回')[0]!
    expect(confirmButton().attributes('disabled')).toBeDefined()

    await wrapper.find('select[aria-label="原因代碼"]').setValue('false_positive')
    await wrapper.find('input[aria-label="說明"]').setValue('盤點基準用錯批號')
    expect(confirmButton().attributes('disabled')).toBeUndefined()
    await confirmButton().trigger('click')
    await flushPromises()

    expect(globalThis.confirm).toHaveBeenCalled()
    expect(mockCloseReconciliationCase).toHaveBeenCalledWith('c1', 'dismiss', {
      reasonCode: 'false_positive',
      note: '盤點基準用錯批號',
      rowVersion: 'AAA=',
    })
    // The form closes after a successful close.
    expect(wrapper.find('select[aria-label="原因代碼"]').exists()).toBe(false)
  })

  /** 修正庫存用的是另一組白名單（count_verified 開頭），取消確認對話就不送出。 */
  it('resolves with the resolve whitelist and does nothing when the confirmation is dismissed', async () => {
    mockListReconciliationCases.mockResolvedValue(page([reconciliationCase({ status: 'Acknowledged' })]))
    vi.spyOn(globalThis, 'confirm').mockReturnValue(false)

    const { wrapper } = mountPage()
    await flushPromises()
    await buttons(wrapper, '修正庫存')[0]!.trigger('click')

    const options = wrapper.findAll('select[aria-label="原因代碼"] option').map((option) => option.attributes('value'))
    expect(options).toEqual(['', 'count_verified', 'system_error', 'other'])
    await wrapper.find('select[aria-label="原因代碼"]').setValue('count_verified')
    await wrapper.find('input[aria-label="說明"]').setValue('實點確認為 0')
    await buttons(wrapper, '確認修正庫存')[0]!.trigger('click')
    await flushPromises()

    expect(globalThis.confirm).toHaveBeenCalledWith(expect.stringContaining('8 → 0'))
    expect(mockCloseReconciliationCase).not.toHaveBeenCalled()
  })

  it('shows the catalogued message when the ledger is inconsistent and keeps the form open', async () => {
    mockListReconciliationCases.mockResolvedValue(page([reconciliationCase()]))
    mockCloseReconciliationCase.mockRejectedValueOnce(new ApiError('inconsistent', {
      status: 409,
      code: 'inventory_reconciliation_ledger_inconsistent',
      correlationId: 'corr-1',
    }))
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const { wrapper } = mountPage()
    await flushPromises()
    await buttons(wrapper, '修正庫存')[0]!.trigger('click')
    await wrapper.find('select[aria-label="原因代碼"]').setValue('count_verified')
    await wrapper.find('input[aria-label="說明"]').setValue('n/a')
    await buttons(wrapper, '確認修正庫存')[0]!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('帳本重算後保留數大於在庫數')
    expect(wrapper.find('select[aria-label="原因代碼"]').exists()).toBe(true)
  })

  it('pages with pageNumber and resets to the first page when the status filter is applied', async () => {
    mockListReconciliationCases.mockResolvedValue(page([reconciliationCase()], { totalPages: 3 }))

    const { wrapper } = mountPage()
    await flushPromises()
    await buttons(wrapper, '下一頁')[0]!.trigger('click')
    await flushPromises()
    expect(mockListReconciliationCases).toHaveBeenLastCalledWith({ status: undefined, pageNumber: 2, pageSize: 20 })

    // Changing the select alone fires nothing; submitting applies the status and goes back to page 1.
    const callsBefore = mockListReconciliationCases.mock.calls.length
    await wrapper.find('select[aria-label="狀態"]').setValue('Acknowledged')
    await flushPromises()
    expect(mockListReconciliationCases.mock.calls.length).toBe(callsBefore)
    await wrapper.find('form[aria-label="對帳篩選"]').trigger('submit')
    await flushPromises()
    expect(mockListReconciliationCases).toHaveBeenLastCalledWith({ status: 'Acknowledged', pageNumber: 1, pageSize: 20 })
  })
})

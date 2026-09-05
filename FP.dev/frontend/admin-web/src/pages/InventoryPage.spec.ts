import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { describe, expect, it, vi } from 'vitest'

const mockListBalances = vi.fn()
const mockListMovements = vi.fn()

vi.mock('../features/inventory/api', () => ({
  listBalances: mockListBalances,
  listMovements: mockListMovements,
  listReservations: vi.fn(),
  releaseReservation: vi.fn(),
}))

const { default: InventoryPage } = await import('./InventoryPage.vue')
const { startOfLocalDay, endOfLocalDayExclusiveBoundary } = await import('../features/inventory/dateRange')

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(InventoryPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
}

const emptyMovements = { items: [], pageNumber: 1, pageSize: 20, totalCount: 0 }

describe('InventoryPage', () => {
  it('renders the loaded balance list', async () => {
    mockListBalances.mockResolvedValue({
      items: [{ skuPublicId: 's1', skuCode: 'SKU-1', skuNameZhTw: 'Widget', onHand: 50, reserved: 10, available: 40, lowStockThreshold: 5, rowVersion: 'AAA=' }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
    })
    mockListMovements.mockResolvedValue(emptyMovements)

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('SKU-1')
    expect(wrapper.text()).toContain('Widget')
  })

  /** A-11: 低庫存與缺貨要能被辨識（用於畫面標示） — 這裡驗證缺貨列被套上正確的樣式 class。 */
  it('marks an out-of-stock row distinctly from a normal row', async () => {
    mockListBalances.mockResolvedValue({
      items: [{ skuPublicId: 's1', skuCode: 'SKU-1', skuNameZhTw: 'Widget', onHand: 0, reserved: 0, available: 0, lowStockThreshold: 5, rowVersion: 'AAA=' }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
    })
    mockListMovements.mockResolvedValue(emptyMovements)

    const wrapper = mountPage()
    await flushPromises()

    const row = wrapper.find('tbody tr')
    expect(row.classes()).toContain('inventory-table__row--out-of-stock')
  })

  it('sends the stockState filter when searching', async () => {
    mockListBalances.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })
    mockListMovements.mockResolvedValue(emptyMovements)

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.find('select[aria-label="庫存狀態"]').setValue('low_stock')
    await wrapper.find('form[aria-label="庫存餘額篩選"]').trigger('submit')
    await flushPromises()

    expect(mockListBalances).toHaveBeenLastCalledWith(expect.objectContaining({ stockState: 'low_stock', pageNumber: 1 }))
  })

  /** 組長 PR #37 round-2 review, item 1: CostChange joined the official vocabulary (PR #36 A1)
   * and the API accepts it as a filter value, so the admin must be able to select it. */
  it('offers CostChange as a movement type and sends it when searching', async () => {
    mockListBalances.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })
    mockListMovements.mockResolvedValue(emptyMovements)

    const wrapper = mountPage()
    await flushPromises()

    const checkbox = wrapper.find('input[type="checkbox"][value="CostChange"]')
    expect(checkbox.exists()).toBe(true)
    await checkbox.setValue(true)
    await wrapper.find('form[aria-label="異動明細篩選"]').trigger('submit')
    await flushPromises()

    expect(mockListMovements).toHaveBeenLastCalledWith(expect.objectContaining({ movementTypes: ['CostChange'] }))
  })

  it('sends selected movement types when searching movements', async () => {
    mockListBalances.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })
    mockListMovements.mockResolvedValue(emptyMovements)

    const wrapper = mountPage()
    await flushPromises()

    const checkbox = wrapper.find('input[type="checkbox"][value="StockIn"]')
    await checkbox.setValue(true)
    await wrapper.find('form[aria-label="異動明細篩選"]').trigger('submit')
    await flushPromises()

    expect(mockListMovements).toHaveBeenLastCalledWith(expect.objectContaining({ movementTypes: ['StockIn'] }))
  })

  /** 組長 PR #37 round-2 review, item 3: A-11's inputs bind to a draft — changing them must not
   * fire a query until 搜尋 submits the whole condition set atomically. */
  it('does not query with half-updated movement filters before the form is submitted', async () => {
    mockListBalances.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })
    mockListMovements.mockResolvedValue(emptyMovements)

    const wrapper = mountPage()
    await flushPromises()
    const callsBefore = mockListMovements.mock.calls.length

    await wrapper.find('input[type="checkbox"][value="StockIn"]').setValue(true)
    await wrapper.find('input[aria-label="起始日期"]').setValue('2026-08-25')
    await flushPromises()

    // Nothing fired: the draft is not part of the query key.
    expect(mockListMovements.mock.calls.length).toBe(callsBefore)

    await wrapper.find('form[aria-label="異動明細篩選"]').trigger('submit')
    await flushPromises()
    expect(mockListMovements).toHaveBeenLastCalledWith(expect.objectContaining({
      movementTypes: ['StockIn'],
      pageNumber: 1,
    }))
  })

  /**
   * Regression test (組長 PR #37 review, item 2): `to` used to be `new Date('2026-08-25').toISOString()`,
   * which is UTC midnight of that date — combined with the backend's inclusive `<= To` comparison,
   * that excluded nearly the entire day the admin actually selected. Asserts the sent `to` is the
   * *end* of the selected local day (via the same helper the fix uses), not its UTC-midnight start.
   */
  it('sends a whole-local-day [from, to) range when filtering movements by date', async () => {
    mockListBalances.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })
    mockListMovements.mockResolvedValue(emptyMovements)

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.find('input[aria-label="起始日期"]').setValue('2026-08-25')
    await wrapper.find('input[aria-label="結束日期"]').setValue('2026-08-25')
    await wrapper.find('form[aria-label="異動明細篩選"]').trigger('submit')
    await flushPromises()

    expect(mockListMovements).toHaveBeenLastCalledWith(expect.objectContaining({
      from: startOfLocalDay('2026-08-25').toISOString(),
      to: endOfLocalDayExclusiveBoundary('2026-08-25').toISOString(),
    }))
    // The old bug's exact output — must NOT be what gets sent.
    expect(mockListMovements).not.toHaveBeenLastCalledWith(expect.objectContaining({
      to: new Date('2026-08-25').toISOString(),
    }))
  })
})

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
})

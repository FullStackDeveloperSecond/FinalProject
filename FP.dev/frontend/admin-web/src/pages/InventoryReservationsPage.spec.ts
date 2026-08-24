import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { afterEach, describe, expect, it, vi } from 'vitest'

const mockListReservations = vi.fn()
const mockReleaseReservation = vi.fn()

vi.mock('../features/inventory/api', () => ({
  listBalances: vi.fn(),
  listMovements: vi.fn(),
  listReservations: mockListReservations,
  releaseReservation: mockReleaseReservation,
}))

const { default: InventoryReservationsPage } = await import('./InventoryReservationsPage.vue')

function reservation(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'r1',
    order: { publicId: 'o1', orderNumber: 'ORD-1' },
    sku: { publicId: 's1', skuCode: 'SKU-1', nameZhTw: 'Widget' },
    quantity: 2,
    status: 'Active',
    expiresAtUtc: '2026-09-01T00:00:00Z',
    createdAtUtc: '2026-08-24T00:00:00Z',
    availableActions: ['release'],
    rowVersion: 'AAA=',
    ...overrides,
  }
}

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(InventoryReservationsPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
}

describe('InventoryReservationsPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    mockListReservations.mockReset()
    mockReleaseReservation.mockReset()
  })

  it('renders the loaded reservation queue', async () => {
    mockListReservations.mockResolvedValue({ items: [reservation()], nextCursor: null, hasMore: false })

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('ORD-1')
    expect(wrapper.text()).toContain('SKU-1')
  })

  it('only shows the release button for a reservation with the release action available', async () => {
    mockListReservations.mockResolvedValue({
      items: [
        reservation({ publicId: 'r1', availableActions: ['release'] }),
        reservation({ publicId: 'r2', status: 'Consumed', availableActions: [] }),
      ],
      nextCursor: null,
      hasMore: false,
    })

    const wrapper = mountPage()
    await flushPromises()

    const releaseButtons = wrapper.findAll('button').filter((button) => button.text() === '釋放')
    expect(releaseButtons).toHaveLength(1)
  })

  /** A-12: 人工釋放需要理由與備註，且送出前有二次確認（globalThis.confirm）。 */
  it('requires a reason and note, confirms, then releases with the reservation RowVersion', async () => {
    mockListReservations.mockResolvedValue({ items: [reservation()], nextCursor: null, hasMore: false })
    mockReleaseReservation.mockResolvedValueOnce(undefined)
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('button').find((button) => button.text() === '釋放')!.trigger('click')
    const confirmButton = wrapper.findAll('button').find((button) => button.text() === '確認釋放')
    expect(confirmButton!.attributes('disabled')).toBeDefined()

    await wrapper.find('input[aria-label="原因代碼"]').setValue('customer_cancelled')
    await wrapper.find('input[aria-label="備註"]').setValue('客戶取消訂單')
    expect(confirmButton!.attributes('disabled')).toBeUndefined()

    await confirmButton!.trigger('click')
    await flushPromises()

    expect(globalThis.confirm).toHaveBeenCalled()
    expect(mockReleaseReservation).toHaveBeenCalledWith('r1', {
      reasonCode: 'customer_cancelled',
      note: '客戶取消訂單',
      rowVersion: 'AAA=',
    })
  })

  it('does not release when the confirmation dialog is dismissed', async () => {
    mockListReservations.mockResolvedValue({ items: [reservation()], nextCursor: null, hasMore: false })
    vi.spyOn(globalThis, 'confirm').mockReturnValue(false)

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('button').find((button) => button.text() === '釋放')!.trigger('click')
    await wrapper.find('input[aria-label="原因代碼"]').setValue('customer_cancelled')
    await wrapper.find('input[aria-label="備註"]').setValue('客戶取消訂單')
    await wrapper.findAll('button').find((button) => button.text() === '確認釋放')!.trigger('click')
    await flushPromises()

    expect(mockReleaseReservation).not.toHaveBeenCalled()
  })

  it('appends items when loading more instead of replacing the list', async () => {
    mockListReservations.mockResolvedValueOnce({
      items: [reservation({ publicId: 'r1' })],
      nextCursor: 'cursor-2',
      hasMore: true,
    })

    const wrapper = mountPage()
    await flushPromises()
    expect(wrapper.findAll('tbody > tr').length).toBeGreaterThanOrEqual(1)

    mockListReservations.mockResolvedValueOnce({
      items: [reservation({ publicId: 'r2', order: { publicId: 'o2', orderNumber: 'ORD-2' } })],
      nextCursor: null,
      hasMore: false,
    })
    const loadMoreButton = wrapper.findAll('button').find((button) => button.text() === '載入更多')
    await loadMoreButton!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('ORD-1')
    expect(wrapper.text()).toContain('ORD-2')
  })
})

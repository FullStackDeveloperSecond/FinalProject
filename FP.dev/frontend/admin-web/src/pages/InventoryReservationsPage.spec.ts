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
  const wrapper = mount(InventoryReservationsPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
  return { wrapper, queryClient }
}

describe('InventoryReservationsPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    mockListReservations.mockReset()
    mockReleaseReservation.mockReset()
  })

  it('renders the loaded reservation queue', async () => {
    mockListReservations.mockResolvedValue({ items: [reservation()], nextCursor: null, hasMore: false })

    const { wrapper } = mountPage()
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

    const { wrapper } = mountPage()
    await flushPromises()

    const releaseButtons = wrapper.findAll('button').filter((button) => button.text() === '釋放')
    expect(releaseButtons).toHaveLength(1)
  })

  /** A-12: 人工釋放需要理由與備註，且送出前有二次確認（globalThis.confirm）。 */
  it('requires a reason and note, confirms, then releases with the reservation RowVersion', async () => {
    mockListReservations.mockResolvedValue({ items: [reservation()], nextCursor: null, hasMore: false })
    mockReleaseReservation.mockResolvedValueOnce(undefined)
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const { wrapper } = mountPage()
    await flushPromises()

    await wrapper.findAll('button').find((button) => button.text() === '釋放')!.trigger('click')
    const confirmButton = wrapper.findAll('button').find((button) => button.text() === '確認釋放')
    expect(confirmButton!.attributes('disabled')).toBeDefined()

    await wrapper.find('select[aria-label="原因代碼"]').setValue('customer_cancelled')
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

    const { wrapper } = mountPage()
    await flushPromises()

    await wrapper.findAll('button').find((button) => button.text() === '釋放')!.trigger('click')
    await wrapper.find('select[aria-label="原因代碼"]').setValue('customer_cancelled')
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

    const { wrapper } = mountPage()
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

  /**
   * Regression test (組長 PR #37 review, item 3): a background refetch of an already-loaded page
   * (window refocus, reconnect, TanStack Query's own retry) used to re-append that page's items
   * on top of themselves, since the watcher only checked "is cursor.value set", not "have I
   * already loaded this page's items".
   */
  it('does not duplicate rows when a refetch replays the loaded pages with the same items', async () => {
    // The infinite query refetches EVERY loaded page on invalidate, so the mock must answer by
    // cursor instead of by call order.
    const page1 = { items: [reservation({ publicId: 'r1' })], nextCursor: 'cursor-2', hasMore: true }
    const page2 = {
      items: [reservation({ publicId: 'r2', order: { publicId: 'o2', orderNumber: 'ORD-2' } })],
      nextCursor: null,
      hasMore: false,
    }
    mockListReservations.mockImplementation(async ({ cursor }) => (cursor === 'cursor-2' ? page2 : page1))

    const { wrapper, queryClient } = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '載入更多')!.trigger('click')
    await flushPromises()
    expect(wrapper.findAll('tbody > tr').length).toBe(2)

    // A background refetch (refocus/invalidate) replays both pages with unchanged content — the
    // list must not grow.
    await queryClient.invalidateQueries()
    await flushPromises()

    expect(wrapper.findAll('tbody > tr').length).toBe(2)
  })

  /** 組長 PR #37 round-2 review, item 2: a refetched row with the same publicId may carry a newer
   * Status/RowVersion/expiry — the merge must upsert it, not drop it, or the admin keeps acting on
   * a stale RowVersion. */
  it('updates an already-loaded row in place when a refetch returns it with newer content', async () => {
    const page1 = { items: [reservation({ publicId: 'r1' })], nextCursor: 'cursor-2', hasMore: true }
    let page2 = {
      items: [reservation({ publicId: 'r2', order: { publicId: 'o2', orderNumber: 'ORD-2' } })],
      nextCursor: null as string | null,
      hasMore: false,
    }
    mockListReservations.mockImplementation(async ({ cursor }) => (cursor === 'cursor-2' ? page2 : page1))

    const { wrapper, queryClient } = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '載入更多')!.trigger('click')
    await flushPromises()
    expect(wrapper.findAll('tbody > tr').length).toBe(2)

    // The same r2 comes back from a background refetch, but its status has moved on.
    page2 = {
      items: [reservation({
        publicId: 'r2',
        order: { publicId: 'o2', orderNumber: 'ORD-2' },
        status: 'Consumed',
        rowVersion: 'BBB=',
        availableActions: [],
      })],
      nextCursor: null,
      hasMore: false,
    }
    await queryClient.invalidateQueries()
    await flushPromises()

    expect(wrapper.findAll('tbody > tr').length).toBe(2)
    // The refreshed status is rendered in the ROW — asserting on wrapper.text() would false-pass
    // because the status <select> also contains the literal 'Consumed' as an option.
    const r2Row = wrapper.findAll('tbody > tr').find((row) => row.text().includes('ORD-2'))!
    expect(r2Row.text()).toContain('Consumed')
    // And its stale release button is gone: availableActions came back empty.
    expect(r2Row.findAll('button').filter((button) => button.text() === '釋放')).toHaveLength(0)
  })

  /** 組長 PR #37 round-3 review (P2): a refresh must re-validate EVERY loaded page, not only the
   * current cursor's. The old self-accumulated list only re-ran the latest page's query, so a
   * page-1 row whose Status/RowVersion/expiry changed server-side stayed stale forever. */
  it('refreshes previously loaded pages so an updated page-1 row does not stay stale', async () => {
    let page1 = { items: [reservation({ publicId: 'r1' })], nextCursor: 'cursor-2' as string | null, hasMore: true }
    const page2 = {
      items: [reservation({ publicId: 'r2', order: { publicId: 'o2', orderNumber: 'ORD-2' } })],
      nextCursor: null,
      hasMore: false,
    }
    mockListReservations.mockImplementation(async ({ cursor }) => (cursor === 'cursor-2' ? page2 : page1))

    const { wrapper, queryClient } = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '載入更多')!.trigger('click')
    await flushPromises()
    expect(wrapper.findAll('tbody > tr').length).toBe(2)

    // r1 is consumed on the server AFTER its page was loaded and the admin moved on to page 2 —
    // a refresh of the current view must not keep offering to release it.
    page1 = {
      items: [reservation({ publicId: 'r1', status: 'Consumed', rowVersion: 'BBB=', availableActions: [] })],
      nextCursor: 'cursor-2',
      hasMore: true,
    }
    await queryClient.invalidateQueries()
    await flushPromises()

    const r1Row = wrapper.findAll('tbody > tr').find((row) => row.text().includes('ORD-1'))!
    expect(r1Row.text()).toContain('Consumed')
    expect(r1Row.findAll('button').filter((button) => button.text() === '釋放')).toHaveLength(0)
    // And page 2 is still rendered — the refresh replayed the whole page list, not just page 1.
    expect(wrapper.findAll('tbody > tr').length).toBe(2)
  })

  /** 組長 PR #37 round-2 review, item 3: changing the status filter while on page two must not
   * fire "new status + old cursor" (the backend rejects a cursor issued under other filters) —
   * the draft only reaches the query together with the cursor reset when 搜尋 submits. */
  it('does not send the old cursor when the status filter changes on a later page', async () => {
    mockListReservations.mockResolvedValueOnce({
      items: [reservation({ publicId: 'r1' })],
      nextCursor: 'cursor-2',
      hasMore: true,
    })
    const { wrapper } = mountPage()
    await flushPromises()

    mockListReservations.mockResolvedValueOnce({
      items: [reservation({ publicId: 'r2', order: { publicId: 'o2', orderNumber: 'ORD-2' } })],
      nextCursor: null,
      hasMore: false,
    })
    await wrapper.findAll('button').find((button) => button.text() === '載入更多')!.trigger('click')
    await flushPromises()
    const callsBefore = mockListReservations.mock.calls.length

    // Changing the select alone fires nothing — the draft is not part of the query key.
    await wrapper.find('select[aria-label="狀態"]').setValue('Active')
    await flushPromises()
    expect(mockListReservations.mock.calls.length).toBe(callsBefore)

    mockListReservations.mockResolvedValueOnce({ items: [], nextCursor: null, hasMore: false })
    await wrapper.find('form[aria-label="保留篩選"]').trigger('submit')
    await flushPromises()

    // The submit's query carries the new status and NO cursor — never "Active + cursor-2".
    const lastCall = mockListReservations.mock.calls.at(-1)![0]
    expect(lastCall).toMatchObject({ status: 'Active' })
    expect(lastCall.cursor).toBeUndefined()
    expect(mockListReservations.mock.calls.every(
      (call) => !(call[0].status === 'Active' && call[0].cursor === 'cursor-2'))).toBe(true)
  })
})

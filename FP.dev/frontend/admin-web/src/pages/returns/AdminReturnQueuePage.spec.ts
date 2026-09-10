import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import PrimeVue from 'primevue/config'
import { chinesePaginationLocale } from '@doselect/web-shared/theme'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { endOfLocalDayExclusiveBoundary, startOfLocalDay } from '../../features/inventory/dateRange'

const { get } = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('../../api/client', () => ({ apiClient: { GET: get } }))
import AdminReturnQueuePage from './AdminReturnQueuePage.vue'

const row = (publicId: string, due: string, status = 'awaitingShipment') => ({
  publicId, returnNumber: `RET-${publicId}`, orderNumber: 'ORDER-DEMO', status,
  priority: 'normal', itemCount: 1, requestedAtUtc: '2026-09-01T00:00:00Z',
  returnShipmentDueAtUtc: due, needsAttention: status === 'awaitingShipment', rowVersion: 'AAAAAAAAAAA=',
})
const response = (items: unknown[], totalCount = 121) => ({ data: { items, totalCount, pageNumber: 1, pageSize: 20 }, error: undefined })
const wrappers: ReturnType<typeof mount>[] = []
function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/returns', component: AdminReturnQueuePage },
      { path: '/returns/:returnId', component: { template: '<div />' } },
    ],
  })
}

async function mountPage(router: Router = createTestRouter()) {
  if (router.currentRoute.value.matched.length === 0) {
    await router.push('/returns')
  }
  await router.isReady()
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const wrapper = mount(AdminReturnQueuePage, { global: {
    plugins: [[VueQueryPlugin, { queryClient }], [PrimeVue, { locale: chinesePaginationLocale }], router],
  } })
  wrappers.push(wrapper)
  return wrapper
}

describe('AdminReturnQueuePage', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-09-09T04:00:00Z'))
    get.mockReset().mockResolvedValue(response([row('1', '2026-09-10T04:00:00Z')]))
  })
  afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.useRealTimers() })

  it('requests the actual last page instead of stopping at the first 20 cases', async () => {
    const router = createTestRouter()
    const wrapper = await mountPage(router)
    await flushPromises()
    await wrapper.get('[aria-label="第 7 頁"]').trigger('click')
    await flushPromises()
    expect(get).toHaveBeenLastCalledWith('/api/v1/admin/returns', expect.objectContaining({
      params: { query: expect.objectContaining({ PageNumber: 7, PageSize: 20 }) },
    }))
    expect(router.currentRoute.value.query.page).toBe('7')
  })

  it('applies a status selection immediately and resets pagination', async () => {
    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.get('[aria-label="第 7 頁"]').trigger('click')
    await flushPromises()
    await wrapper.get('select[aria-label="退貨狀態"]').setValue('awaitingShipment')
    await flushPromises()
    expect(get).toHaveBeenLastCalledWith('/api/v1/admin/returns', expect.objectContaining({
      params: { query: expect.objectContaining({ Statuses: ['awaitingShipment'], PageNumber: 1 }) },
    }))
  })

  it('distinguishes passed deadlines from upcoming and completed cases', async () => {
    get.mockResolvedValue(response([
      row('expired', '2026-08-26T04:00:00Z'),
      row('boundary', '2026-09-09T04:00:00Z'),
      row('soon', '2026-09-10T04:00:00Z'),
      row('done', '2026-08-26T04:00:00Z', 'completed'),
    ]))
    const wrapper = await mountPage()
    await flushPromises()
    const rows = wrapper.findAll('tbody tr')
    expect(rows[0]!.text()).toContain('已逾期')
    expect(rows[1]!.text()).toContain('已逾期')
    expect(rows[2]!.text()).toContain('即將逾期')
    expect(rows[3]!.text()).not.toContain('逾期')
  })

  it('debounces the latest search, resets the page and cancels pending input on unmount', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'] })
    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.get('[aria-label="第 7 頁"]').trigger('click')
    await flushPromises()
    const calls = get.mock.calls.length
    const input = wrapper.get('input[aria-label="退貨或訂單編號"]')
    await input.setValue('RET-')
    await vi.advanceTimersByTimeAsync(200)
    await input.setValue(' RET-latest ')
    await vi.advanceTimersByTimeAsync(299)
    expect(get).toHaveBeenCalledTimes(calls)
    await vi.advanceTimersByTimeAsync(1)
    await flushPromises()
    expect(get).toHaveBeenLastCalledWith('/api/v1/admin/returns', expect.objectContaining({
      params: { query: expect.objectContaining({ Q: 'RET-latest', PageNumber: 1 }) },
    }))
    const updatedCalls = get.mock.calls.length
    await input.setValue('not-submitted')
    wrapper.unmount()
    wrappers.splice(wrappers.indexOf(wrapper), 1)
    await vi.advanceTimersByTimeAsync(300)
    expect(get).toHaveBeenCalledTimes(updatedCalls)
  })

  it('restores every list control from the URL and sends the matching API query', async () => {
    const router = createTestRouter()
    await router.push({ path: '/returns', query: {
      q: 'DS202609090030',
      status: 'awaitingShipment',
      reason: 'Defective',
      from: '2026-09-01',
      to: '2026-09-09',
      sort: 'requestedAsc',
      pageSize: '50',
      page: '2',
    } })

    const wrapper = await mountPage(router)
    await flushPromises()

    expect(wrapper.get('input[aria-label="退貨或訂單編號"]').element).toHaveProperty('value', 'DS202609090030')
    expect(wrapper.get('select[aria-label="退貨狀態"]').element).toHaveProperty('value', 'awaitingShipment')
    expect(wrapper.get('select[aria-label="退貨原因"]').element).toHaveProperty('value', 'Defective')
    expect(wrapper.get('select[aria-label="退貨案件排序"]').element).toHaveProperty('value', 'requestedAsc')
    expect(wrapper.get('select[aria-label="每頁筆數"]').element).toHaveProperty('value', '50')
    expect(get).toHaveBeenLastCalledWith('/api/v1/admin/returns', expect.objectContaining({
      params: { query: expect.objectContaining({
        Q: 'DS202609090030',
        Statuses: ['awaitingShipment'],
        ReasonCodes: ['Defective'],
        From: startOfLocalDay('2026-09-01').toISOString(),
        To: endOfLocalDayExclusiveBoundary('2026-09-09').toISOString(),
        Sort: 'requestedAsc',
        PageNumber: 2,
        PageSize: 50,
      }) },
    }))
  })

  it('writes changed filters and pagination back to the URL', async () => {
    const router = createTestRouter()
    await router.push('/returns')
    const wrapper = await mountPage(router)
    await flushPromises()

    await wrapper.get('select[aria-label="退貨原因"]').setValue('WrongItem')
    await wrapper.get('input[aria-label="申請日期從"]').setValue('2026-09-02')
    await wrapper.get('input[aria-label="申請日期到"]').setValue('2026-09-08')
    await wrapper.get('select[aria-label="退貨案件排序"]').setValue('shipmentDeadlineAsc')
    await wrapper.get('select[aria-label="每頁筆數"]').setValue('50')
    await flushPromises()

    expect(router.currentRoute.value.query).toMatchObject({
      reason: 'WrongItem',
      from: '2026-09-02',
      to: '2026-09-08',
      sort: 'shipmentDeadlineAsc',
      pageSize: '50',
    })
    expect(get).toHaveBeenLastCalledWith('/api/v1/admin/returns', expect.objectContaining({
      params: { query: expect.objectContaining({
        ReasonCodes: ['WrongItem'],
        Sort: 'shipmentDeadlineAsc',
        PageNumber: 1,
        PageSize: 50,
      }) },
    }))
  })

  it('recovers to the first page when a refreshed tail page has no remaining cases', async () => {
    const wrapper = await mountPage()
    await flushPromises()
    get.mockResolvedValue(response([], 0))
    await wrapper.get('[aria-label="第 7 頁"]').trigger('click')
    await flushPromises()
    await flushPromises()
    expect(wrapper.text()).toContain('目前沒有退貨案件')
    expect(get).toHaveBeenLastCalledWith('/api/v1/admin/returns', expect.objectContaining({
      params: { query: expect.objectContaining({ PageNumber: 1 }) },
    }))
  })
})

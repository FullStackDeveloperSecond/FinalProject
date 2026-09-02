import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const mockShipBatch = vi.fn()

vi.mock('../features/shipping/api', () => ({
  listConvenienceStores: vi.fn(),
  createConvenienceStore: vi.fn(),
  updateConvenienceStore: vi.fn(),
  listPackageLimitVersions: vi.fn(),
  createPackageLimitVersion: vi.fn(),
  publishPackageLimitVersion: vi.fn(),
  shipBatch: mockShipBatch,
}))

const { default: ShipmentBatchesPage } = await import('./ShipmentBatchesPage.vue')
const { setBatchShipmentSelection, clearBatchShipmentSelection, batchShipmentSelection }
  = await import('../features/shipping/batchSelection')

function candidate(index: number) {
  return {
    publicId: `order-${index}`,
    orderNumber: `DS${index}`,
    rowVersion: `RV${index}`,
    summaryStatus: 'awaitingShipment',
    fulfillmentStatus: 'Pending',
  }
}

function result(overrides: Record<string, unknown> = {}) {
  return {
    batchPublicId: 'batch-1',
    total: 2,
    succeeded: 1,
    failed: 1,
    createdAtUtc: '2026-09-02T00:00:00Z',
    items: [
      {
        sourceRowNumber: 1,
        orderPublicId: 'order-1',
        orderNumber: 'DS1',
        status: 'Shipped',
        trackingNumber: 'SIM123',
        errorCode: null,
        message: null,
      },
      {
        sourceRowNumber: 2,
        orderPublicId: 'order-2',
        orderNumber: 'DS2',
        status: 'Failed',
        trackingNumber: null,
        errorCode: 'shipping_order_not_ready',
        message: 'The order is not paid.',
      },
    ],
    ...overrides,
  }
}

async function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/shipping/batches', component: ShipmentBatchesPage },
      { path: '/orders', component: { template: '<div />' } },
    ],
  })
  await router.push('/shipping/batches')
  await router.isReady()
  return mount(ShipmentBatchesPage, {
    global: { plugins: [[VueQueryPlugin, { queryClient }], router] },
  })
}

describe('ShipmentBatchesPage', () => {
  beforeEach(() => {
    clearBatchShipmentSelection()
    // 每次呼叫都要回不同的值。固定回同一把鍵的話，「重試沿用同一把」那支測試就算把程式改成
    // 每次送出都重新產生，也照樣是綠的——反向驗證抓到的正是這個。
    let issued = 0
    vi.stubGlobal('crypto', { ...globalThis.crypto, randomUUID: vi.fn(() => `key-${++issued}`) })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    mockShipBatch.mockReset()
  })

  it('tells the operator to pick orders first when nothing is selected', async () => {
    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('尚未選取訂單')
    expect(wrapper.find('button[type="button"]').exists()).toBe(false)
  })

  it('sends the selected orders with their row versions and the chosen action', async () => {
    setBatchShipmentSelection([candidate(1), candidate(2)])
    mockShipBatch.mockResolvedValue(result())
    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('input[type="radio"][value="markShipped"]').setValue()
    await wrapper.find('button[type="button"]').trigger('click')
    await flushPromises()

    expect(mockShipBatch).toHaveBeenCalledWith({
      orders: [
        { orderPublicId: 'order-1', rowVersion: 'RV1' },
        { orderPublicId: 'order-2', rowVersion: 'RV2' },
      ],
      shippingAction: 'markShipped',
      idempotencyKey: 'key-1',
    })
  })

  it('shows every row of the result, including the failed ones and their error codes', async () => {
    setBatchShipmentSelection([candidate(1), candidate(2)])
    mockShipBatch.mockResolvedValue(result())
    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('button[type="button"]').trigger('click')
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('成功 1 筆、失敗 1 筆')
    expect(text).toContain('SIM123')
    expect(text).toContain('shipping_order_not_ready')
  })

  /**
   * 送出後那些訂單的 RowVersion 已經被推進，留著等於讓管理員拿過期版本再送一次。
   */
  it('clears the carried selection once the batch has been sent', async () => {
    setBatchShipmentSelection([candidate(1), candidate(2)])
    mockShipBatch.mockResolvedValue(result())
    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('button[type="button"]').trigger('click')
    await flushPromises()

    expect(batchShipmentSelection.value).toHaveLength(0)
  })

  /**
   * 網路斷線後重按送出，帶的必須是同一把冪等鍵。換一把新的就等於承認「重試 = 新的一批」，
   * 一旦第一次其實已經送達，第二次就會把同一批訂單再出一次貨。
   */
  it('reuses the same idempotency key when a failed submission is retried', async () => {
    setBatchShipmentSelection([candidate(1)])
    mockShipBatch.mockRejectedValueOnce(new Error('network down')).mockResolvedValue(result())
    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('button[type="button"]').trigger('click')
    await flushPromises()
    await wrapper.find('button[type="button"]').trigger('click')
    await flushPromises()

    expect(mockShipBatch).toHaveBeenCalledTimes(2)
    expect(mockShipBatch.mock.calls[0][0].idempotencyKey)
      .toBe(mockShipBatch.mock.calls[1][0].idempotencyKey)
  })
})

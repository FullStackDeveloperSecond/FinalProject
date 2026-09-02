import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'

const mockPreview = vi.fn()
const mockGetBatch = vi.fn()
const mockGetRows = vi.fn()
const mockConfirm = vi.fn()
const mockDownloadErrors = vi.fn()

vi.mock('../features/imports/api', () => ({
  downloadProductImportTemplate: vi.fn(),
  previewProductImport: vi.fn(),
  getProductImportBatch: vi.fn(),
  getProductImportRows: vi.fn(),
  downloadProductImportErrors: vi.fn(),
  confirmProductImport: vi.fn(),
  previewInventoryImport: (...args: unknown[]) => mockPreview(...args),
  getInventoryImportBatch: (...args: unknown[]) => mockGetBatch(...args),
  getInventoryImportRows: (...args: unknown[]) => mockGetRows(...args),
  downloadInventoryImportErrors: (...args: unknown[]) => mockDownloadErrors(...args),
  confirmInventoryImport: (...args: unknown[]) => mockConfirm(...args),
}))

const { default: InventoryImportPage } = await import('./InventoryImportPage.vue')

function batch(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'batch-9',
    importType: 'InventoryAdjustment',
    templateVersion: 1,
    status: 'Ready',
    createdByAdminUserId: 'admin-1',
    createdAtUtc: '2026-09-02T00:00:00Z',
    expiresAtUtc: '2026-09-03T00:00:00Z',
    rowCount: 4,
    newCount: 0,
    updatedCount: 3,
    unchangedCount: 1,
    errorCount: 0,
    confirmedAtUtc: null,
    rowVersion: 'BBB=',
    ...overrides,
  }
}

function rowsPage(items: unknown[] = []) {
  return { items, nextCursor: null, hasMore: false }
}

function row(overrides: Record<string, unknown> = {}) {
  return {
    dataset: 'InventoryAdjustments',
    sourceRowNumber: 2,
    importKey: 'SKU-1',
    action: 'Update',
    errorCodes: [],
    normalizedPayloadJson: '{}',
    ...overrides,
  }
}

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(InventoryImportPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
}

async function attachFile(wrapper: ReturnType<typeof mountPage>) {
  const input = wrapper.find('input[aria-label="庫存調整 CSV"]')
  Object.defineProperty(input.element, 'files', {
    value: [new File(['sku_code,target_on_hand,reason_code,note'], 'stock.csv', { type: 'text/csv' })],
    configurable: true,
  })
  await input.trigger('change')
}

describe('InventoryImportPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    for (const mock of [mockPreview, mockGetBatch, mockGetRows, mockConfirm, mockDownloadErrors]) {
      mock.mockReset()
    }
  })

  /**
   * 六個原因碼由規格固定（匯入暫存與庫存調整設計.md），而且 Other 必填說明。把契約寫在畫面上，
   * 管理員不必為了四個欄位去翻文件——這也是防止「猜一個原因碼、整批被退」的最便宜方式。
   */
  it('states the field contract including every accepted reason code', async () => {
    const wrapper = mountPage()
    await flushPromises()

    const text = wrapper.text()
    for (const code of ['StocktakeDifference', 'Damaged', 'Lost', 'ReturnRestock', 'DataCorrection', 'Other']) {
      expect(text).toContain(code)
    }
    expect(text).toContain('不可低於已保留數量')
    expect(text).toContain('Other 時必填')
  })

  it('does not allow upload until a file is chosen', async () => {
    const wrapper = mountPage()
    await flushPromises()

    const submit = () => wrapper.findAll('button').find((button) => button.text() === '上傳並預覽')!
    expect(submit().attributes('disabled')).toBeDefined()

    await attachFile(wrapper)
    expect(submit().attributes('disabled')).toBeUndefined()
  })

  it('uploads the single dataset and shows the preview statistics', async () => {
    mockPreview.mockResolvedValue(batch())
    mockGetBatch.mockResolvedValue(batch())
    mockGetRows.mockResolvedValue(rowsPage([row()]))

    const wrapper = mountPage()
    await attachFile(wrapper)
    await wrapper.find('form[aria-label="庫存匯入上傳"]').trigger('submit')
    await flushPromises()

    expect(mockPreview).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('預覽結果')
    expect(wrapper.text()).toContain('可提交')
  })

  /**
   * 庫存匯入獨有的失敗：Preview 之後庫存被動過，那份盤點差異就不再成立，整批拒絕並要求重新
   * 預覽。訊息必須說出「重新上傳」，否則管理員只會重按一次確認。
   */
  it('tells the admin to re-preview when stock moved after the preview', async () => {
    mockPreview.mockResolvedValue(batch())
    mockGetBatch.mockResolvedValue(batch())
    mockGetRows.mockResolvedValue(rowsPage([row()]))
    mockConfirm.mockRejectedValue(
      new ApiError('conflict', { status: 409, code: 'concurrency_conflict' }))

    const wrapper = mountPage()
    await attachFile(wrapper)
    await wrapper.find('form[aria-label="庫存匯入上傳"]').trigger('submit')
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '確認匯入')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('整批未套用')
    expect(wrapper.text()).toContain('重新上傳')
  })

  it('sends the batch RowVersion when confirming', async () => {
    mockPreview.mockResolvedValue(batch())
    mockGetBatch.mockResolvedValue(batch())
    mockGetRows.mockResolvedValue(rowsPage([row()]))
    mockConfirm.mockResolvedValue(batch({ status: 'Committed' }))

    const wrapper = mountPage()
    await attachFile(wrapper)
    await wrapper.find('form[aria-label="庫存匯入上傳"]').trigger('submit')
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '確認匯入')!.trigger('click')
    await flushPromises()

    expect(mockConfirm).toHaveBeenCalledWith('batch-9', 'BBB=')
  })

  it('downloads the error CSV for the current batch', async () => {
    mockPreview.mockResolvedValue(batch({ status: 'Invalid', errorCount: 1 }))
    mockGetBatch.mockResolvedValue(batch({ status: 'Invalid', errorCount: 1 }))
    mockGetRows.mockResolvedValue(rowsPage([row({ action: 'Error', errorCodes: ['import_lookup_not_found'] })]))
    mockDownloadErrors.mockResolvedValue(new Blob(['dataset'], { type: 'text/csv' }))

    const wrapper = mountPage()
    await attachFile(wrapper)
    await wrapper.find('form[aria-label="庫存匯入上傳"]').trigger('submit')
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '下載錯誤 CSV')!.trigger('click')
    await flushPromises()

    expect(mockDownloadErrors).toHaveBeenCalledWith('batch-9')
  })
})

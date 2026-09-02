import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'

const mockDownloadTemplate = vi.fn()
const mockPreview = vi.fn()
const mockGetBatch = vi.fn()
const mockGetRows = vi.fn()
const mockConfirm = vi.fn()
const mockDownloadErrors = vi.fn()

vi.mock('../features/imports/api', () => ({
  downloadProductImportTemplate: (...args: unknown[]) => mockDownloadTemplate(...args),
  previewProductImport: (...args: unknown[]) => mockPreview(...args),
  getProductImportBatch: (...args: unknown[]) => mockGetBatch(...args),
  getProductImportRows: (...args: unknown[]) => mockGetRows(...args),
  downloadProductImportErrors: (...args: unknown[]) => mockDownloadErrors(...args),
  confirmProductImport: (...args: unknown[]) => mockConfirm(...args),
  previewInventoryImport: vi.fn(),
  getInventoryImportBatch: vi.fn(),
  getInventoryImportRows: vi.fn(),
  downloadInventoryImportErrors: vi.fn(),
  confirmInventoryImport: vi.fn(),
}))

const { default: ProductImportPage } = await import('./ProductImportPage.vue')

function batch(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'batch-1',
    importType: 'Product',
    templateVersion: 1,
    status: 'Ready',
    createdByAdminUserId: 'admin-1',
    createdAtUtc: '2026-09-02T00:00:00Z',
    expiresAtUtc: '2026-09-03T00:00:00Z',
    rowCount: 3,
    newCount: 2,
    updatedCount: 1,
    unchangedCount: 0,
    errorCount: 0,
    confirmedAtUtc: null,
    rowVersion: 'AAA=',
    ...overrides,
  }
}

function rowsPage(items: unknown[] = []) {
  return { items, nextCursor: null, hasMore: false }
}

function row(overrides: Record<string, unknown> = {}) {
  return {
    dataset: 'Products',
    sourceRowNumber: 2,
    importKey: 'P1',
    action: 'Insert',
    errorCodes: [],
    normalizedPayloadJson: '{}',
    ...overrides,
  }
}

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(ProductImportPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
}

/** jsdom 的 input.files 是唯讀的，所以直接定義屬性。 */
async function attachFile(wrapper: ReturnType<typeof mountPage>, label: string) {
  const input = wrapper.find(`input[aria-label="${label}"]`)
  Object.defineProperty(input.element, 'files', {
    value: [new File(['header'], `${label}.csv`, { type: 'text/csv' })],
    configurable: true,
  })
  await input.trigger('change')
}

async function attachAllThreeFiles(wrapper: ReturnType<typeof mountPage>) {
  for (const label of ['Products CSV', 'SKUs CSV', 'Specifications CSV']) {
    await attachFile(wrapper, label)
  }
}

describe('ProductImportPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    for (const mock of [mockDownloadTemplate, mockPreview, mockGetBatch, mockGetRows, mockConfirm, mockDownloadErrors]) {
      mock.mockReset()
    }
  })

  /**
   * 三個資料集都必填——少一個就送出去，只會拿回一個後端已經知道會拒絕的請求。
   *
   * 關鍵是「選了一部分」那個狀態：只驗「一個都沒選時停用」的話，把條件從 && 改成 || 測試照樣是
   * 綠的（反向驗證時發現的）。
   */
  it('does not allow upload until all three datasets are chosen', async () => {
    const wrapper = mountPage()
    await flushPromises()

    const submit = () => wrapper.findAll('button').find((button) => button.text() === '上傳並預覽')!
    expect(submit().attributes('disabled')).toBeDefined()

    await attachFile(wrapper, 'Products CSV')
    expect(submit().attributes('disabled')).toBeDefined()

    await attachFile(wrapper, 'SKUs CSV')
    expect(submit().attributes('disabled')).toBeDefined()

    await attachFile(wrapper, 'Specifications CSV')
    expect(submit().attributes('disabled')).toBeUndefined()
  })

  /**
   * 組長 PR #89 item 6：XLSX 是規格明列的另一條路。選了 XLSX 模式就只送 workbook，三個 CSV 欄位
   * 不再是必填——否則管理員得為了一個 XLSX 再湊三個空 CSV。
   */
  it('uploads a single workbook when the XLSX mode is chosen', async () => {
    mockPreview.mockResolvedValue(batch())
    mockGetBatch.mockResolvedValue(batch())
    mockGetRows.mockResolvedValue(rowsPage([row()]))

    const wrapper = mountPage()
    await wrapper.find('input[type="radio"][value="workbook"]').setValue()
    const submit = () => wrapper.findAll('button').find((button) => button.text() === '上傳並預覽')!
    expect(submit().attributes('disabled')).toBeDefined()

    await attachFile(wrapper, '商品匯入 XLSX')
    expect(submit().attributes('disabled')).toBeUndefined()
    await wrapper.find('form[aria-label="商品匯入上傳"]').trigger('submit')
    await flushPromises()

    expect(mockPreview).toHaveBeenCalledWith(
      expect.objectContaining({ workbook: expect.any(File) }),
      1,
    )
    expect(mockPreview.mock.calls[0][0].products).toBeUndefined()
  })

  it('uploads the three datasets and shows the preview statistics', async () => {
    mockPreview.mockResolvedValue(batch())
    mockGetBatch.mockResolvedValue(batch())
    mockGetRows.mockResolvedValue(rowsPage([row()]))

    const wrapper = mountPage()
    await attachAllThreeFiles(wrapper)
    await wrapper.find('form[aria-label="商品匯入上傳"]').trigger('submit')
    await flushPromises()

    expect(mockPreview).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('預覽結果')
    expect(wrapper.text()).toContain('可提交')
  })

  /**
   * 有錯誤列時整批不會套用，所以確認鈕必須是停用的——讓管理員按下去才被後端拒絕，只是多一次
   * 白跑，而且會讓人以為「錯誤列會被跳過」。
   */
  it('disables confirm and explains why when the batch has error rows', async () => {
    mockPreview.mockResolvedValue(batch({ status: 'Invalid', errorCount: 2 }))
    mockGetBatch.mockResolvedValue(batch({ status: 'Invalid', errorCount: 2 }))
    mockGetRows.mockResolvedValue(rowsPage([row({ action: 'Error', errorCodes: ['import_validation_failed'] })]))

    const wrapper = mountPage()
    await attachAllThreeFiles(wrapper)
    await wrapper.find('form[aria-label="商品匯入上傳"]').trigger('submit')
    await flushPromises()

    const confirm = wrapper.findAll('button').find((button) => button.text() === '確認匯入')!
    expect(confirm.attributes('disabled')).toBeDefined()
    // 錯誤列的數量要講出來，管理員才知道要修幾列。
    expect(wrapper.text()).toContain('有 2 列錯誤')
    expect(wrapper.text()).toContain('整批不會套用')
  })

  it('sends the batch RowVersion when confirming', async () => {
    mockPreview.mockResolvedValue(batch())
    mockGetBatch.mockResolvedValue(batch())
    mockGetRows.mockResolvedValue(rowsPage([row()]))
    mockConfirm.mockResolvedValue(batch({ status: 'Committed', confirmedAtUtc: '2026-09-02T01:00:00Z' }))

    const wrapper = mountPage()
    await attachAllThreeFiles(wrapper)
    await wrapper.find('form[aria-label="商品匯入上傳"]').trigger('submit')
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '確認匯入')!.trigger('click')
    await flushPromises()

    expect(mockConfirm).toHaveBeenCalledWith('batch-1', 'AAA=')
    expect(wrapper.text()).toContain('已套用 3 列')
  })

  /** 整批單一交易，所以每一種失敗訊息都要說「整批未套用」。 */
  it('says the whole batch was left untouched when confirm conflicts', async () => {
    mockPreview.mockResolvedValue(batch())
    mockGetBatch.mockResolvedValue(batch())
    mockGetRows.mockResolvedValue(rowsPage([row()]))
    mockConfirm.mockRejectedValue(
      new ApiError('conflict', { status: 409, code: 'concurrency_conflict' }))

    const wrapper = mountPage()
    await attachAllThreeFiles(wrapper)
    await wrapper.find('form[aria-label="商品匯入上傳"]').trigger('submit')
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '確認匯入')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('整批未套用')
  })

  it('explains an in-progress batch rather than showing a raw error', async () => {
    mockPreview.mockRejectedValue(
      new ApiError('conflict', { status: 409, code: 'import_batch_in_progress' }))

    const wrapper = mountPage()
    await attachAllThreeFiles(wrapper)
    await wrapper.find('form[aria-label="商品匯入上傳"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('未結束的商品匯入批次')
  })

  it('downloads the template', async () => {
    mockDownloadTemplate.mockResolvedValue(new Blob(['PK'], { type: 'application/zip' }))

    const wrapper = mountPage()
    await wrapper.findAll('button').find((button) => button.text() === '下載匯入模板')!.trigger('click')
    await flushPromises()

    expect(mockDownloadTemplate).toHaveBeenCalledTimes(1)
  })
})

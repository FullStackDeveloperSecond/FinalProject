import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { describe, expect, it, vi } from 'vitest'

const mockUpdateSku = vi.fn()
const mockDeleteSku = vi.fn()

vi.mock('../../features/skus/api', () => ({
  updateSku: mockUpdateSku,
  deleteSku: mockDeleteSku,
  createSku: vi.fn(),
}))

const { default: SkuEditorRow } = await import('./SkuEditorRow.vue')

function baseSku(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'sku-1',
    skuCode: 'SKU-1',
    product: { publicId: 'p1', productCode: 'P1', nameZhTw: 'Product 1' },
    nameZhTw: 'Original Name',
    listPrice: 100,
    unitCost: 60,
    weightKg: null,
    lengthCm: null,
    widthCm: null,
    heightCm: null,
    status: 'Draft',
    isDefault: false,
    requiresPrepayment: false,
    specifications: [],
    inventory: null,
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: '2026-01-01T00:00:00Z',
    rowVersion: 'AAA=',
    ...overrides,
  }
}

function mountRow(sku: ReturnType<typeof baseSku>) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(
    { components: { SkuEditorRow }, template: '<table><tbody><SkuEditorRow :sku="sku" product-public-id="p1" /></tbody></table>' },
    { data: () => ({ sku }), global: { plugins: [[VueQueryPlugin, { queryClient }]] } },
  )
}

describe('SkuEditorRow', () => {
  it('offers the Unpublished lifecycle state while editing', async () => {
    const wrapper = mountRow(baseSku())

    await wrapper.find('button').trigger('click')

    const statuses = wrapper.find('select[aria-label="狀態"]').findAll('option').map((option) => option.attributes('value'))
    expect(statuses).toEqual(['Draft', 'Published', 'Unpublished'])
  })

  /** PR #24 review: a cancelled draft must not linger and get resubmitted on the next edit. */
  it('discards an abandoned edit when reopened after cancel', async () => {
    const wrapper = mountRow(baseSku({ listPrice: 100 }))

    await wrapper.find('button').trigger('click') // 編輯
    const priceInput = wrapper.find('input[aria-label="售價"]')
    await priceInput.setValue('999')
    const cancelButton = wrapper.findAll('button').find((button) => button.text() === '取消')
    await cancelButton!.trigger('click')

    const editButton = wrapper.findAll('button').find((button) => button.text() === '編輯')
    await editButton!.trigger('click')
    const reopenedPriceInput = wrapper.find('input[aria-label="售價"]')

    expect((reopenedPriceInput.element as HTMLInputElement).value).toBe('100')
  })

  /** PR #24 review: reopening edit after a save (same publicId, Vue reuses the component) must show the freshly-saved value, not the pre-edit closure value. */
  it('shows the freshly saved value when reopened after a successful save', async () => {
    mockUpdateSku.mockResolvedValueOnce(baseSku({ nameZhTw: 'Updated Name', rowVersion: 'BBB=' }))
    const wrapper = mountRow(baseSku({ nameZhTw: 'Original Name', rowVersion: 'AAA=' }))

    await wrapper.find('button').trigger('click') // 編輯
    await wrapper.find('input[aria-label="名稱"]').setValue('Updated Name')
    const saveButton = wrapper.findAll('button').find((button) => button.text() === '儲存')
    await saveButton!.trigger('click')
    await flushPromises()

    // Simulate the refetch that follows a successful mutation: the parent re-passes a new
    // SkuDto object (same publicId) with the server's current RowVersion.
    await wrapper.setData({ sku: baseSku({ nameZhTw: 'Updated Name', rowVersion: 'BBB=' }) })

    const editButton = wrapper.findAll('button').find((button) => button.text() === '編輯')
    await editButton!.trigger('click')
    const nameInput = wrapper.find('input[aria-label="名稱"]')

    expect((nameInput.element as HTMLInputElement).value).toBe('Updated Name')

    await wrapper.findAll('button').find((button) => button.text() === '儲存')!.trigger('click')
    await flushPromises()

    expect(mockUpdateSku).toHaveBeenLastCalledWith('sku-1', expect.objectContaining({ rowVersion: 'BBB=' }))
  })

  /**
   * Regression test (組長 PR #24 round 4 review, item 1): the backend now rejects unsetting or
   * deleting the current default SKU directly — the row-level controls must not offer an action
   * that will always 409.
   */
  it('disables the delete button for the current default SKU', () => {
    const wrapper = mountRow(baseSku({ isDefault: true }))

    const deleteButton = wrapper.findAll('button').find((button) => button.text() === '刪除')

    expect(deleteButton!.attributes('disabled')).toBeDefined()
  })

  it('leaves the delete button enabled for a non-default SKU', () => {
    const wrapper = mountRow(baseSku({ isDefault: false }))

    const deleteButton = wrapper.findAll('button').find((button) => button.text() === '刪除')

    expect(deleteButton!.attributes('disabled')).toBeUndefined()
  })

  it('disables the isDefault checkbox while editing the current default SKU', async () => {
    const wrapper = mountRow(baseSku({ isDefault: true }))

    await wrapper.find('button').trigger('click') // 編輯

    const checkbox = wrapper.find('input[aria-label="預設 SKU"]')
    expect(checkbox.attributes('disabled')).toBeDefined()
  })
})

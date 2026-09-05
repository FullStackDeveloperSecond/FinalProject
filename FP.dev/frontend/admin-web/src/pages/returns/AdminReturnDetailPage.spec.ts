import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')

  return {
    data: ref<Record<string, unknown> | null>(null),
    isPending: ref(false),
    isError: ref(false),
    error: ref<unknown>(null),
    refetch: vi.fn(),
    reviewMutateAsync: vi.fn(),
    reviewIsPending: ref(false),
    reviewError: ref<unknown>(null),
    receiveMutateAsync: vi.fn(),
    receiveIsPending: ref(false),
    receiveError: ref<unknown>(null),
    inspectMutateAsync: vi.fn(),
    inspectIsPending: ref(false),
    inspectError: ref<unknown>(null),
    extendMutateAsync: vi.fn(),
    extendIsPending: ref(false),
    extendError: ref<unknown>(null),
  }
})

vi.mock('../../features/returns/queries', () => ({
  useAdminReturnDetailQuery: () => ({
    data: mocks.data,
    isPending: mocks.isPending,
    isError: mocks.isError,
    error: mocks.error,
    refetch: mocks.refetch,
  }),
  useReviewReturnMutation: () => ({
    mutateAsync: mocks.reviewMutateAsync,
    isPending: mocks.reviewIsPending,
    error: mocks.reviewError,
  }),
  useReceiveReturnMutation: () => ({
    mutateAsync: mocks.receiveMutateAsync,
    isPending: mocks.receiveIsPending,
    error: mocks.receiveError,
  }),
  useInspectReturnMutation: () => ({
    mutateAsync: mocks.inspectMutateAsync,
    isPending: mocks.inspectIsPending,
    error: mocks.inspectError,
  }),
  useExtendShipmentDeadlineMutation: () => ({
    mutateAsync: mocks.extendMutateAsync,
    isPending: mocks.extendIsPending,
    error: mocks.extendError,
  }),
}))

const { default: AdminReturnDetailPage } = await import('./AdminReturnDetailPage.vue')

const returnId = '018f2e6a-0000-7000-8000-000000000060'

function returnRequest(overrides: Record<string, unknown> = {}) {
  return {
    publicId: returnId,
    returnNumber: 'RT-20260905-0001',
    orderPublicId: '018f2e6a-0000-7000-8000-000000000030',
    orderNumber: 'ORD-0001',
    status: 'requested',
    priority: 'normal',
    reasonCode: 'Defective',
    description: '面板有亮點',
    items: [
      {
        publicId: 'item-1',
        orderItemPublicId: 'oi-1',
        skuCodeSnapshot: 'SKU-1',
        productNameSnapshot: '商品一',
        description: null,
        quantity: 1,
        inspectionStatus: 'NotInspected',
        restockDisposition: null,
      },
    ],
    attachments: [],
    requestedAtUtc: '2026-09-01T00:00:00Z',
    approvedAtUtc: null,
    receivedAtUtc: null,
    closedAtUtc: null,
    returnShipmentDueAtUtc: null,
    shipmentDeadlineExtended: false,
    shipment: null,
    availableActions: [],
    rowVersion: 'AAAAAAAAAAE=',
    ...overrides,
  }
}

function detail(availableActions: string[], returnOverrides: Record<string, unknown> = {}) {
  return {
    return: returnRequest(returnOverrides),
    inspections: [],
    refundableItemsPreview: [],
    history: [],
    availableActions,
  }
}

async function mountPage() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/returns', component: { template: '<div />' } },
      { path: '/returns/:returnId', component: AdminReturnDetailPage },
    ],
  })
  await router.push(`/returns/${returnId}`)
  await router.isReady()

  return mount(AdminReturnDetailPage, { global: { plugins: [router] } })
}

describe('AdminReturnDetailPage', () => {
  beforeEach(() => {
    mocks.data.value = null
    mocks.isPending.value = false
    mocks.isError.value = false
    mocks.error.value = null
    mocks.refetch.mockReset()
    mocks.reviewMutateAsync.mockReset().mockResolvedValue(undefined)
    mocks.reviewIsPending.value = false
    mocks.reviewError.value = null
    mocks.receiveMutateAsync.mockReset().mockResolvedValue(undefined)
    mocks.inspectMutateAsync.mockReset().mockResolvedValue(undefined)
    mocks.inspectIsPending.value = false
    mocks.inspectError.value = null
    mocks.extendMutateAsync.mockReset().mockResolvedValue(undefined)
  })

  // alex 2026-09-05 #109 裁定第 6、7 點：不得有靜默預設值，逼管理員自己選；
  // 選項沿用 generated contract 的六個合法值。
  it('offers the trusted-fields dropdown unselected by default, covering all six AssemblyFeeDisposition values', async () => {
    mocks.data.value = detail(['review'])
    const wrapper = await mountPage()
    await wrapper.find('[type="checkbox"]').setValue(false)

    const select = wrapper.findAll('select').at(-1)!
    expect(select.element.value).toBe('')
    const optionValues = select.findAll('option').map((option) => option.attributes('value'))
    expect(optionValues).toEqual([
      '',
      'notApplicable',
      'notStarted',
      'merchantCancelled',
      'assemblyFault',
      'merchantFaultWholeUnit',
      'completedPartialReturn',
    ])
  })

  // 裁定第 1 點：免寄回直接退款的審核路徑需要這兩欄，且必須都填妥才能核准。
  it('requires the trusted fields before approving when the review skips shipment', async () => {
    mocks.data.value = detail(['review'])
    const wrapper = await mountPage()

    await wrapper.find('[type="checkbox"]').setValue(false)
    const approveButton = wrapper.findAll('button').find((btn) => btn.text() === '核准')!
    expect(approveButton.attributes('disabled')).toBeDefined()

    const selects = wrapper.findAll('select')
    const dispositionSelect = selects.at(-1)!
    const inputs = wrapper.findAll('input[type="number"]')
    const shippingCostInput = inputs[0]!

    await dispositionSelect.setValue('merchantCancelled')
    expect(approveButton.attributes('disabled')).toBeDefined()

    await shippingCostInput.setValue('60')
    expect(approveButton.attributes('disabled')).toBeUndefined()

    await approveButton.trigger('click')
    await flushPromises()

    expect(mocks.reviewMutateAsync).toHaveBeenCalledWith(expect.objectContaining({
      approved: true,
      assemblyFeeDisposition: 'merchantCancelled',
      returnShippingCost: 60,
    }))
  })

  // 裁定第 2 點：需要寄回的審核路徑不要求這兩欄；曾取消勾選填值後再勾回，
  // payload 必須完全省略這兩欄，不能夾帶隱藏欄位的殘留值。
  it('omits the trusted fields when the review requires shipment, even after toggling and re-checking', async () => {
    mocks.data.value = detail(['review'])
    const wrapper = await mountPage()

    const approveButton = wrapper.findAll('button').find((btn) => btn.text() === '核准')!
    expect(approveButton.attributes('disabled')).toBeUndefined()

    // 取消勾選、填值，再重新勾選。
    const checkbox = wrapper.find('[type="checkbox"]')
    await checkbox.setValue(false)
    await wrapper.findAll('select').at(-1)!.setValue('merchantCancelled')
    await wrapper.findAll('input[type="number"]')[0]!.setValue('60')
    await checkbox.setValue(true)

    await approveButton.trigger('click')
    await flushPromises()

    expect(mocks.reviewMutateAsync).toHaveBeenCalledOnce()
    const submittedBody = mocks.reviewMutateAsync.mock.calls[0]![0] as Record<string, unknown>
    expect(submittedBody).not.toHaveProperty('assemblyFeeDisposition')
    expect(submittedBody).not.toHaveProperty('returnShippingCost')
  })

  // 裁定第 3 點：拒絕路徑的 payload 同樣不得包含這兩欄。
  it('omits the trusted fields on reject regardless of the checkbox state', async () => {
    mocks.data.value = detail(['review'])
    const wrapper = await mountPage()

    await wrapper.find('[type="checkbox"]').setValue(false)
    await wrapper.findAll('select').at(-1)!.setValue('merchantCancelled')
    await wrapper.findAll('input[type="number"]')[0]!.setValue('60')

    const rejectButton = wrapper.findAll('button').find((btn) => btn.text() === '拒絕')!
    await rejectButton.trigger('click')
    await flushPromises()

    expect(mocks.reviewMutateAsync).toHaveBeenCalledOnce()
    const submittedBody = mocks.reviewMutateAsync.mock.calls[0]![0] as Record<string, unknown>
    expect(submittedBody).toEqual(expect.objectContaining({ approved: false }))
    expect(submittedBody).not.toHaveProperty('assemblyFeeDisposition')
    expect(submittedBody).not.toHaveProperty('returnShippingCost')
  })

  // 裁定第 4 點：商品檢查路徑固定顯示並要求這兩欄，request body 要正確。
  it('always shows and requires the trusted fields for inspect, submitting the correct body', async () => {
    mocks.data.value = detail(['inspect'], { status: 'received' })
    const wrapper = await mountPage()

    const submitButton = wrapper.findAll('button').find((btn) => btn.text() === '送出檢查結果')!
    expect(submitButton.attributes('disabled')).toBeDefined()

    const selects = wrapper.findAll('select')
    // 第一個 select 是商品狀態、第二個是回補判定，組裝費處置是這個面板唯一的第三個 select。
    const dispositionSelect = selects.at(-1)!
    await dispositionSelect.setValue('notApplicable')
    expect(submitButton.attributes('disabled')).toBeDefined()

    const costInput = wrapper.find('input[type="number"]')
    await costInput.setValue('0')
    expect(submitButton.attributes('disabled')).toBeUndefined()

    await submitButton.trigger('click')
    await flushPromises()

    expect(mocks.inspectMutateAsync).toHaveBeenCalledWith(expect.objectContaining({
      assemblyFeeDisposition: 'notApplicable',
      returnShippingCost: 0,
      items: [expect.objectContaining({
        returnItemPublicId: 'item-1',
        conditionCode: 'Unopened',
        disposition: 'resellable',
      })],
    }))
  })

  // 裁定第 5 點：returnShippingCost 不得小於 0，必須允許合法的 0。
  it('blocks empty and negative shipping cost but allows zero', async () => {
    mocks.data.value = detail(['inspect'], { status: 'received' })
    const wrapper = await mountPage()

    const submitButton = wrapper.findAll('button').find((btn) => btn.text() === '送出檢查結果')!
    await wrapper.findAll('select').at(-1)!.setValue('notApplicable')
    const costInput = wrapper.find('input[type="number"]')

    expect(submitButton.attributes('disabled')).toBeDefined()

    await costInput.setValue('-1')
    expect(submitButton.attributes('disabled')).toBeDefined()

    await costInput.setValue('0')
    expect(submitButton.attributes('disabled')).toBeUndefined()
  })
})

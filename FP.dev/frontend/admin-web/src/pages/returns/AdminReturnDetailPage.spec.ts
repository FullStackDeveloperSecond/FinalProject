import { ApiError } from '@doselect/web-shared/api'
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
    reviewIsError: ref(false),
    reviewError: ref<unknown>(null),
    receiveMutateAsync: vi.fn(),
    receiveIsPending: ref(false),
    receiveError: ref<unknown>(null),
    inspectMutateAsync: vi.fn(),
    inspectIsPending: ref(false),
    inspectIsError: ref(false),
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
    isError: mocks.reviewIsError,
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
    isError: mocks.inspectIsError,
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

async function mountPage(errorHandler?: (error: unknown) => void) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/returns', component: { template: '<div />' } },
      { path: '/returns/:returnId', component: AdminReturnDetailPage },
    ],
  })
  await router.push(`/returns/${returnId}`)
  await router.isReady()

  return mount(AdminReturnDetailPage, {
    global: {
      plugins: [router],
      config: errorHandler ? { errorHandler } : {},
    },
  })
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
    mocks.reviewIsError.value = false
    mocks.reviewError.value = null
    mocks.receiveMutateAsync.mockReset().mockResolvedValue(undefined)
    mocks.inspectMutateAsync.mockReset().mockResolvedValue(undefined)
    mocks.inspectIsPending.value = false
    mocks.inspectIsError.value = false
    mocks.inspectError.value = null
    mocks.extendMutateAsync.mockReset().mockResolvedValue(undefined)
  })

  // alex 2026-09-05 #109 裁定第 6、7 點：不得有靜默預設值，逼管理員自己選；
  // 選項沿用 generated contract 的六個合法值。
  it('offers the trusted-fields dropdown unselected by default, covering all six AssemblyFeeDisposition values', async () => {
    mocks.data.value = detail(['review'])
    const wrapper = await mountPage()
    await wrapper.find('[type="checkbox"]').setValue(false)

    const select = wrapper.find('[name="reviewAssemblyFeeDisposition"]')
    expect((select.element as HTMLSelectElement).value).toBe('')
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

    const dispositionSelect = wrapper.find('[name="reviewAssemblyFeeDisposition"]')
    const shippingCostInput = wrapper.find('[name="reviewReturnShippingCost"]')

    await dispositionSelect.setValue('merchantCancelled')
    expect(approveButton.attributes('disabled')).toBeDefined()

    await shippingCostInput.setValue('60')
    expect(approveButton.attributes('disabled')).toBeUndefined()

    await approveButton.trigger('click')
    await flushPromises()

    expect(mocks.reviewMutateAsync).toHaveBeenCalledWith(expect.objectContaining({
      approved: true,
      assemblyFeeDisposition: 'merchantCancelled',
      returnShippingCost: '60',
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
    await wrapper.find('[name="reviewAssemblyFeeDisposition"]').setValue('merchantCancelled')
    await wrapper.find('[name="reviewReturnShippingCost"]').setValue('60')
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
    await wrapper.find('[name="reviewAssemblyFeeDisposition"]').setValue('merchantCancelled')
    await wrapper.find('[name="reviewReturnShippingCost"]').setValue('60')

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

    await wrapper.find('[name="inspectAssemblyFeeDisposition"]').setValue('notApplicable')
    expect(submitButton.attributes('disabled')).toBeDefined()

    await wrapper.find('[name="inspectReturnShippingCost"]').setValue('0')
    expect(submitButton.attributes('disabled')).toBeUndefined()

    await submitButton.trigger('click')
    await flushPromises()

    expect(mocks.inspectMutateAsync).toHaveBeenCalledWith(expect.objectContaining({
      assemblyFeeDisposition: 'notApplicable',
      returnShippingCost: '0',
      items: [expect.objectContaining({
        returnItemPublicId: 'item-1',
        conditionCode: 'Unopened',
        disposition: 'resellable',
      })],
    }))
  })

  // 2026-09-05 #111 review P2 裁定：不得用 JavaScript 浮點數靜默改寫退貨運費。
  // 這裡逐一驗證 review／inspect 兩條路徑對同一組輸入的判斷與送出結果一致，
  // 且金額一律以原始字串送出，不經過 Number() 或任何四捨五入。
  it.each([
    { label: 'review（免寄回）', panel: 'review' as const },
    { label: 'inspect（商品檢查）', panel: 'inspect' as const },
  ])('blocks negative, empty and more-than-two-decimal shipping cost, allows integral and two-decimal values ($label)', async ({ panel }) => {
    mocks.data.value = panel === 'review'
      ? detail(['review'])
      : detail(['inspect'], { status: 'received' })
    const wrapper = await mountPage()

    if (panel === 'review') {
      await wrapper.find('[type="checkbox"]').setValue(false)
    }

    const dispositionName = panel === 'review' ? 'reviewAssemblyFeeDisposition' : 'inspectAssemblyFeeDisposition'
    const costName = panel === 'review' ? 'reviewReturnShippingCost' : 'inspectReturnShippingCost'
    const submitButton = wrapper.findAll('button')
      .find((btn) => btn.text() === (panel === 'review' ? '核准' : '送出檢查結果'))!
    const costInput = wrapper.find(`[name="${costName}"]`)

    await wrapper.find(`[name="${dispositionName}"]`).setValue('notApplicable')

    for (const blocked of ['', '-1', '1.005', '0.001', 'abc']) {
      await costInput.setValue(blocked)
      expect(submitButton.attributes('disabled'), `"${blocked}" must block submission`).toBeDefined()
    }

    for (const allowed of ['0', '1.01', '60']) {
      await costInput.setValue(allowed)
      expect(submitButton.attributes('disabled'), `"${allowed}" must be accepted`).toBeUndefined()
    }

    await costInput.setValue('1.01')
    await submitButton.trigger('click')
    await flushPromises()

    const spy = panel === 'review' ? mocks.reviewMutateAsync : mocks.inspectMutateAsync
    const submittedBody = spy.mock.calls[0]![0] as Record<string, unknown>
    // 原始字串「1.01」原封不動送出——不是 Number(1.01) 也不是任何四捨五入的結果。
    expect(submittedBody.returnShippingCost).toBe('1.01')
  })

  it.each([
    { panel: 'review' as const, expectedMessage: '退貨運費超出允許範圍。' },
    { panel: 'inspect' as const, expectedMessage: '操作失敗，請稍後再試一次。' },
  ])('renders a non-conflict mutation failure without leaking a rejected promise ($panel)', async ({ panel, expectedMessage }) => {
    mocks.data.value = panel === 'review'
      ? detail(['review'])
      : detail(['inspect'], { status: 'received' })
    const unhandledErrors: unknown[] = []
    const wrapper = await mountPage(error => unhandledErrors.push(error))

    if (panel === 'review') {
      await wrapper.find('[type="checkbox"]').setValue(false)
    }

    const dispositionName = panel === 'review' ? 'reviewAssemblyFeeDisposition' : 'inspectAssemblyFeeDisposition'
    const costName = panel === 'review' ? 'reviewReturnShippingCost' : 'inspectReturnShippingCost'
    const submitText = panel === 'review' ? '核准' : '送出檢查結果'
    const mutationError = panel === 'review'
      ? new ApiError(expectedMessage, { status: 400, code: 'validation_failed' })
      : new Error('network unavailable')
    const mutateAsync = panel === 'review' ? mocks.reviewMutateAsync : mocks.inspectMutateAsync
    const isMutationError = panel === 'review' ? mocks.reviewIsError : mocks.inspectIsError
    const error = panel === 'review' ? mocks.reviewError : mocks.inspectError

    await wrapper.find(`[name="${dispositionName}"]`).setValue('notApplicable')
    await wrapper.find(`[name="${costName}"]`).setValue('1.01')
    mutateAsync.mockImplementationOnce(async () => {
      error.value = mutationError
      isMutationError.value = true
      throw mutationError
    })

    await wrapper.findAll('button').find(button => button.text() === submitText)!.trigger('click')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain(expectedMessage)
    expect(unhandledErrors).toEqual([])
  })
})

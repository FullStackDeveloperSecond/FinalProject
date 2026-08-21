import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import SupportTicketDetailPage from './SupportTicketDetailPage.vue'

const supportMocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')

  return {
    ticket: ref<Record<string, unknown> | null>(null),
    ticketPending: ref(false),
    ticketError: ref(false),
    ticketFailure: ref<unknown>(null),
    refetch: vi.fn(),
    addMessage: {
      isPending: ref(false),
      isError: ref(false),
      error: ref<unknown>(null),
      mutateAsync: vi.fn(),
    },
    cancel: {
      isPending: ref(false),
      isError: ref(false),
      error: ref<unknown>(null),
      mutateAsync: vi.fn(),
    },
    upload: {
      isPending: ref(false),
      isError: ref(false),
      error: ref<unknown>(null),
      mutateAsync: vi.fn(),
      reset: vi.fn(),
    },
  }
})

vi.mock('../../features/support/queries', () => ({
  useSupportTicketQuery: () => ({
    data: supportMocks.ticket,
    isPending: supportMocks.ticketPending,
    isError: supportMocks.ticketError,
    error: supportMocks.ticketFailure,
    refetch: supportMocks.refetch,
  }),
  useAddSupportMessageMutation: () => supportMocks.addMessage,
  useCancelSupportTicketMutation: () => supportMocks.cancel,
  useUploadSupportAttachmentMutation: () => supportMocks.upload,
}))

function sampleTicket(availableActions = ['addMessage', 'cancel']) {
  return {
    publicId: '018f2e6a-0000-7000-8000-000000000001',
    ticketNumber: 'CS-20260819-0001',
    category: 'order',
    subject: '訂單延遲問題',
    status: 'open',
    priority: 'normal',
    firstResponseDueAtUtc: '2026-08-19T04:00:00Z',
    resolutionDueAtUtc: '2026-08-20T03:00:00Z',
    rowVersion: 'AAAAAAAAAAE=',
    availableActions,
    messages: [
      {
        publicId: '018f2e6a-0000-7000-8000-000000000002',
        senderType: 'member',
        body: '請協助確認配送進度',
        sentAtUtc: '2026-08-19T03:00:00Z',
      },
    ],
  }
}

async function mountPage(errorHandler?: (error: unknown) => void) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/support/tickets', component: { template: '<div />' } },
      { path: '/support/tickets/:ticketId', component: { template: '<div />' } },
    ],
  })
  await router.push('/support/tickets/018f2e6a-0000-7000-8000-000000000001')
  await router.isReady()

  return mount(SupportTicketDetailPage, {
    global: {
      plugins: [router],
      config: errorHandler ? { errorHandler } : {},
    },
  })
}

function selectFile(wrapper: Awaited<ReturnType<typeof mountPage>>, file: File) {
  const input = wrapper.get('#support-ticket-attachment-input')
  Object.defineProperty(input.element, 'files', {
    configurable: true,
    value: [file],
  })
  return input.trigger('change')
}

describe('SupportTicketDetailPage', () => {
  beforeEach(() => {
    supportMocks.ticket.value = sampleTicket()
    supportMocks.ticketPending.value = false
    supportMocks.ticketError.value = false
    supportMocks.ticketFailure.value = null
    supportMocks.addMessage.isPending.value = false
    supportMocks.addMessage.isError.value = false
    supportMocks.addMessage.error.value = null
    supportMocks.cancel.isPending.value = false
    supportMocks.cancel.isError.value = false
    supportMocks.cancel.error.value = null
    supportMocks.upload.isPending.value = false
    supportMocks.upload.isError.value = false
    supportMocks.upload.error.value = null
    supportMocks.refetch.mockReset()
    supportMocks.addMessage.mutateAsync.mockReset().mockResolvedValue(undefined)
    supportMocks.cancel.mutateAsync.mockReset().mockResolvedValue(undefined)
    supportMocks.upload.mutateAsync.mockReset()
    supportMocks.upload.reset.mockReset()
  })

  it('provides a labelled, constrained attachment input only while replies are allowed', async () => {
    const wrapper = await mountPage()
    const input = wrapper.get('#support-ticket-attachment-input')

    expect(wrapper.get('section[aria-labelledby="support-ticket-attachments-title"] h2').text())
      .toBe('上傳附件')
    expect(wrapper.get('label[for="support-ticket-attachment-input"]').text())
      .toContain('選擇檔案')
    expect(input.attributes('type')).toBe('file')
    expect(input.attributes('accept'))
      .toBe('.png,.jpg,.jpeg,.pdf,image/png,image/jpeg,application/pdf')

    wrapper.unmount()
    supportMocks.ticket.value = sampleTicket([])
    const restrictedWrapper = await mountPage()

    expect(restrictedWrapper.find('#support-ticket-attachment-input').exists()).toBe(false)
    expect(restrictedWrapper.find('textarea').exists()).toBe(false)
    expect(restrictedWrapper.find('.support-ticket-detail__cancel').exists()).toBe(false)
  })

  it('rejects unsupported extensions and files larger than 10 MiB before upload', async () => {
    const wrapper = await mountPage()

    await selectFile(wrapper, new File(['unsafe'], 'payload.exe', { type: 'application/octet-stream' }))
    expect(wrapper.get('[role="alert"]').text()).toContain('僅支援 PNG、JPEG 或 PDF')
    expect(wrapper.get('button[type="submit"]').attributes()).toHaveProperty('disabled')
    expect(supportMocks.upload.mutateAsync).not.toHaveBeenCalled()

    await selectFile(wrapper, new File(
      [new ArrayBuffer((10 * 1024 * 1024) + 1)],
      'too-large.pdf',
      { type: 'application/pdf' },
    ))
    expect(wrapper.get('[role="alert"]').text()).toContain('檔案大小不可超過 10 MB')
    expect(wrapper.get('button[type="submit"]').attributes()).toHaveProperty('disabled')

    await selectFile(wrapper, new File(
      [new ArrayBuffer(10 * 1024 * 1024)],
      'maximum-size.jpeg',
      { type: 'image/jpeg' },
    ))
    expect(wrapper.text()).toContain('已選擇「maximum-size.jpeg」（10.0 MB）')
    expect(wrapper.get('button[type="submit"]').attributes()).not.toHaveProperty('disabled')
    expect(supportMocks.upload.reset).toHaveBeenCalledTimes(3)
  })

  it('shows pending and safe success output without rendering sensitive attachment fields', async () => {
    let finishUpload!: (value: Record<string, unknown>) => void
    supportMocks.upload.mutateAsync.mockImplementation(() => {
      supportMocks.upload.isPending.value = true
      return new Promise((resolve) => {
        finishUpload = (value) => {
          supportMocks.upload.isPending.value = false
          resolve(value)
        }
      })
    })
    const wrapper = await mountPage()
    const file = new File(['receipt'], 'receipt.pdf', { type: 'application/pdf' })

    await selectFile(wrapper, file)
    await wrapper.get('.support-ticket-detail__attachment-form').trigger('submit')
    wrapper.vm.$forceUpdate()
    await wrapper.vm.$nextTick()

    expect(supportMocks.upload.mutateAsync).toHaveBeenCalledWith(file)
    expect(wrapper.get('.support-ticket-detail__attachment-form button').text()).toBe('上傳中…')
    expect(wrapper.get('.support-ticket-detail__attachment-form button').attributes())
      .toHaveProperty('disabled')

    finishUpload({
      publicId: '018f2e6a-0000-7000-8000-000000000099',
      originalFileName: 'receipt.pdf',
      mimeType: 'application/pdf',
      fileSizeBytes: 7,
      createdAtUtc: '2026-08-19T03:30:00Z',
      storageKey: 'private/member-7/secret-key',
      physicalPath: 'D:\\private\\secret.pdf',
      sha256: 'sensitive-hash',
      scannerOutput: 'internal-scanner-result',
    })
    await flushPromises()

    expect(wrapper.text()).toContain('已上傳')
    expect(wrapper.text()).toContain('receipt.pdf')
    expect(wrapper.text()).not.toContain('018f2e6a-0000-7000-8000-000000000099')
    expect(wrapper.text()).not.toContain('private/member-7/secret-key')
    expect(wrapper.text()).not.toContain('D:\\private\\secret.pdf')
    expect(wrapper.text()).not.toContain('sensitive-hash')
    expect(wrapper.text()).not.toContain('internal-scanner-result')
  })

  it('renders a safe backend upload error', async () => {
    supportMocks.upload.isError.value = true
    supportMocks.upload.error.value = new ApiError('附件格式與內容不符。', {
      status: 400,
      code: 'attachment_invalid',
      correlationId: 'request-attachment-400',
    })
    const wrapper = await mountPage()

    expect(wrapper.get('[role="alert"]').text()).toBe('附件格式與內容不符。')
    expect(wrapper.text()).not.toContain('request-attachment-400')
    expect(wrapper.text()).not.toContain('Stack Trace')
  })

  it('contains rejected upload, reply, and cancellation handlers in mutation error state', async () => {
    const escapedErrorHandler = vi.fn()
    const uploadFailure = new ApiError('附件上傳失敗。', {
      status: 400,
      code: 'attachment_invalid',
    })
    const replyFailure = new ApiError('訊息送出失敗。', {
      status: 409,
      code: 'row_version_conflict',
    })
    const cancelFailure = new ApiError('案件取消失敗。', {
      status: 409,
      code: 'row_version_conflict',
    })

    supportMocks.upload.mutateAsync.mockImplementation(async () => {
      supportMocks.upload.isError.value = true
      supportMocks.upload.error.value = uploadFailure
      throw uploadFailure
    })
    supportMocks.addMessage.mutateAsync.mockImplementation(async () => {
      supportMocks.addMessage.isError.value = true
      supportMocks.addMessage.error.value = replyFailure
      throw replyFailure
    })
    supportMocks.cancel.mutateAsync.mockImplementation(async () => {
      supportMocks.cancel.isError.value = true
      supportMocks.cancel.error.value = cancelFailure
      throw cancelFailure
    })

    const wrapper = await mountPage(escapedErrorHandler)
    const file = new File(['receipt'], 'receipt.pdf', { type: 'application/pdf' })

    await selectFile(wrapper, file)
    await expect(wrapper.get('.support-ticket-detail__attachment-form').trigger('submit'))
      .resolves.toBeUndefined()
    await flushPromises()
    expect(supportMocks.upload.mutateAsync).toHaveBeenCalledWith(file)
    expect(wrapper.get('.support-ticket-detail__attachment-form [role="alert"]').text())
      .toBe('附件上傳失敗。')

    const textarea = wrapper.get('textarea')
    await textarea.setValue('  補充配送資訊  ')
    await expect(wrapper.get('.support-ticket-detail__reply-form').trigger('submit'))
      .resolves.toBeUndefined()
    await flushPromises()
    expect(supportMocks.addMessage.mutateAsync).toHaveBeenCalledWith({
      body: '補充配送資訊',
      rowVersion: 'AAAAAAAAAAE=',
    })
    expect(wrapper.get('.support-ticket-detail__reply-form [role="alert"]').text())
      .toBe('訊息送出失敗。')
    expect((textarea.element as HTMLTextAreaElement).value).toBe('  補充配送資訊  ')

    await wrapper.get('.btn-danger').trigger('click')
    const cancelInput = wrapper.get('.support-ticket-detail__cancel input')
    await cancelInput.setValue('  已自行解決  ')
    await expect(wrapper.get('.support-ticket-detail__cancel form').trigger('submit'))
      .resolves.toBeUndefined()
    await flushPromises()
    expect(supportMocks.cancel.mutateAsync).toHaveBeenCalledWith({
      reasonCode: '已自行解決',
      rowVersion: 'AAAAAAAAAAE=',
    })
    expect(wrapper.get('.support-ticket-detail__cancel [role="alert"]').text())
      .toBe('案件取消失敗。')
    expect((cancelInput.element as HTMLInputElement).value).toBe('  已自行解決  ')
    expect(escapedErrorHandler).not.toHaveBeenCalled()
  })

  it('preserves reply and cancellation actions with the current row version', async () => {
    const wrapper = await mountPage()
    const textarea = wrapper.get('textarea')

    await textarea.setValue('  補充配送資訊  ')
    await wrapper.get('.support-ticket-detail__reply-form').trigger('submit')
    await flushPromises()
    expect(supportMocks.addMessage.mutateAsync).toHaveBeenCalledWith({
      body: '補充配送資訊',
      rowVersion: 'AAAAAAAAAAE=',
    })
    expect((textarea.element as HTMLTextAreaElement).value).toBe('')

    await wrapper.get('.btn-danger').trigger('click')
    const cancelInput = wrapper.get('.support-ticket-detail__cancel input')
    await cancelInput.setValue('  已自行解決  ')
    await wrapper.get('.support-ticket-detail__cancel form').trigger('submit')
    await flushPromises()
    expect(supportMocks.cancel.mutateAsync).toHaveBeenCalledWith({
      reasonCode: '已自行解決',
      rowVersion: 'AAAAAAAAAAE=',
    })
    expect(wrapper.get('.btn-danger').text()).toBe('取消這個案件')
  })
})

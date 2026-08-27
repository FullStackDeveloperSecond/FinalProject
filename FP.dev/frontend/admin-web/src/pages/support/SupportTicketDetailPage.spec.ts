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
    claim: {
      isPending: ref(false),
      isError: ref(false),
      error: ref<unknown>(null),
      mutateAsync: vi.fn(),
    },
  }
})

vi.mock('../../features/support/queries', () => ({
  useSupportTicketDetailQuery: () => ({
    data: supportMocks.ticket,
    isPending: supportMocks.ticketPending,
    isError: supportMocks.ticketError,
    error: supportMocks.ticketFailure,
    refetch: supportMocks.refetch,
  }),
  useClaimSupportTicketMutation: () => supportMocks.claim,
}))

const ticketId = '018f2e6a-0000-7000-8000-000000000001'

function sampleTicket(availableActions: string[] = ['claim']) {
  return {
    publicId: ticketId,
    ticketNumber: 'CS-20260819-0001',
    category: 'order',
    subject: '訂單延遲問題',
    status: 'open',
    priority: 'normal',
    orderPublicId: '018f2e6a-0000-7000-8000-000000000020',
    assignee: null,
    createdAtUtc: '2026-08-19T01:00:00Z',
    lastActivityAtUtc: '2026-08-19T03:00:00Z',
    firstResponseDueAtUtc: '2026-08-19T04:00:00Z',
    resolutionDueAtUtc: '2026-08-20T03:00:00Z',
    isOverdue: false,
    firstHumanResponseAtUtc: null,
    resolvedAtUtc: null,
    closedAtUtc: null,
    reopenCount: 0,
    rowVersion: 'AAAAAAAAAAE=',
    availableActions,
    adminEmail: 'admin-secret@example.test',
    internalTicketId: 9281,
    storageKey: 'private/support/secret-object',
    rawException: 'SecretException: internal stack trace',
    messages: [
      {
        publicId: '018f2e6a-0000-7000-8000-000000000002',
        senderType: 'member',
        aiGenerated: false,
        isInternal: false,
        body: '請協助確認配送進度',
        language: 'zh-TW',
        sentAtUtc: '2026-08-19T01:10:00Z',
      },
      {
        publicId: '018f2e6a-0000-7000-8000-000000000003',
        senderType: 'admin',
        aiGenerated: false,
        isInternal: true,
        body: '內部查核：物流商已回覆。',
        language: 'zh-TW',
        sentAtUtc: '2026-08-19T02:10:00Z',
        senderUserId: 'internal-admin-id',
      },
    ],
  }
}

async function mountPage(errorHandler?: (error: unknown) => void) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/support', component: { template: '<div />' } },
      { path: '/support/tickets/:ticketId', component: { template: '<div />' } },
    ],
  })
  await router.push(`/support/tickets/${ticketId}`)
  await router.isReady()

  return mount(SupportTicketDetailPage, {
    global: {
      plugins: [router],
      config: errorHandler ? { errorHandler } : {},
    },
  })
}

describe('SupportTicketDetailPage', () => {
  beforeEach(() => {
    supportMocks.ticket.value = sampleTicket()
    supportMocks.ticketPending.value = false
    supportMocks.ticketError.value = false
    supportMocks.ticketFailure.value = null
    supportMocks.refetch.mockReset()
    supportMocks.claim.isPending.value = false
    supportMocks.claim.isError.value = false
    supportMocks.claim.error.value = null
    supportMocks.claim.mutateAsync.mockReset().mockResolvedValue(undefined)
  })

  it('renders the public-safe detail and distinguishes an admin internal note', async () => {
    const wrapper = await mountPage()
    const messages = wrapper.findAll('.support-ticket-detail__message')

    expect(wrapper.get('h1').text()).toContain('CS-20260819-0001｜訂單延遲問題')
    expect(wrapper.text()).toContain('請協助確認配送進度')
    expect(messages).toHaveLength(2)
    expect(messages[0]?.classes()).not.toContain('support-ticket-detail__message--internal')
    expect(messages[1]?.classes()).toContain('support-ticket-detail__message--admin')
    expect(messages[1]?.classes()).toContain('support-ticket-detail__message--internal')
    expect(messages[1]?.get('.tag').text()).toBe('內部備註')
    expect(messages[1]?.text()).toContain('內部查核：物流商已回覆。')

    expect(wrapper.text()).not.toContain('admin-secret@example.test')
    expect(wrapper.text()).not.toContain('9281')
    expect(wrapper.text()).not.toContain('private/support/secret-object')
    expect(wrapper.text()).not.toContain('SecretException')
    expect(wrapper.text()).not.toContain('internal-admin-id')

    const buttonLabels = wrapper.findAll('button').map(button => button.text())
    expect(buttonLabels).toEqual(['受理這個案件'])
    expect(wrapper.find('textarea').exists()).toBe(false)
    expect(wrapper.find('form').exists()).toBe(false)
  })

  it('gates claim on the exact backend availableActions token', async () => {
    const allowed = await mountPage()
    expect(allowed.findAll('button').some(button => button.text() === '受理這個案件')).toBe(true)

    allowed.unmount()
    supportMocks.ticket.value = sampleTicket(['claimTicket'])
    const nearMatch = await mountPage()
    expect(nearMatch.findAll('button').some(button => button.text() === '受理這個案件')).toBe(false)

    nearMatch.unmount()
    supportMocks.ticket.value = sampleTicket([])
    const denied = await mountPage()
    expect(denied.findAll('button').some(button => button.text() === '受理這個案件')).toBe(false)

    denied.unmount()
    const missingActions = sampleTicket()
    Reflect.deleteProperty(missingActions, 'availableActions')
    supportMocks.ticket.value = missingActions
    const missing = await mountPage()
    expect(missing.findAll('button').some(button => button.text() === '受理這個案件')).toBe(false)

    missing.unmount()
    const nullActions = sampleTicket()
    Reflect.set(nullActions, 'availableActions', null)
    supportMocks.ticket.value = nullActions
    const nullValue = await mountPage()
    expect(nullValue.findAll('button').some(button => button.text() === '受理這個案件')).toBe(false)

    nullValue.unmount()
    const nonArrayActions = sampleTicket()
    Reflect.set(nonArrayActions, 'availableActions', 'claim')
    supportMocks.ticket.value = nonArrayActions
    const nonArray = await mountPage()
    expect(nonArray.findAll('button').some(button => button.text() === '受理這個案件')).toBe(false)
  })

  it('submits the current RowVersion and shows pending feedback', async () => {
    const wrapper = await mountPage()
    await wrapper.get('button').trigger('click')
    await flushPromises()

    expect(supportMocks.claim.mutateAsync).toHaveBeenCalledWith({ rowVersion: 'AAAAAAAAAAE=' })

    supportMocks.claim.isPending.value = true
    await wrapper.vm.$nextTick()
    expect(wrapper.get('button').text()).toBe('受理中…')
    expect(wrapper.get('button').attributes()).toHaveProperty('disabled')
  })

  it('contains a rejected claim and presents a safe 409 refresh path', async () => {
    const escapedErrorHandler = vi.fn()
    const conflict = new ApiError('The ticket changed.', {
      status: 409,
      code: 'support_ticket_assignment_conflict',
      correlationId: 'request-conflict-id',
    })
    supportMocks.claim.mutateAsync.mockImplementation(async () => {
      supportMocks.claim.isError.value = true
      supportMocks.claim.error.value = conflict
      throw conflict
    })
    const wrapper = await mountPage(escapedErrorHandler)

    await expect(wrapper.get('button').trigger('click')).resolves.toBeUndefined()
    await flushPromises()

    const alert = wrapper.get('[role="alert"]')
    expect(alert.text()).toContain('可能已被其他客服人員受理')
    expect(alert.text()).not.toContain('request-conflict-id')
    expect(alert.text()).not.toContain('Stack Trace')
    await alert.get('button').trigger('click')
    expect(supportMocks.refetch).toHaveBeenCalledOnce()
    expect(escapedErrorHandler).not.toHaveBeenCalled()
  })
})

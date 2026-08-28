import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import SupportHomePage from './SupportHomePage.vue'

const mocks = vi.hoisted(() => {
  const query = (data: unknown) => ({
    data: { value: data },
    isPending: { value: false },
    isError: { value: false },
    error: { value: null },
    refetch: vi.fn(),
  })
  const mutation = () => ({
    data: { value: undefined as unknown },
    isPending: { value: false },
    isError: { value: false },
    error: { value: null as unknown },
    mutateAsync: vi.fn(),
    reset: vi.fn(),
  })
  return {
    consent: query({ state: 'missing', policyVersion: 1, locale: null, decidedAtUtc: null }),
    usage: query({ usedRequests: 2, requestLimit: 20, budgetWarningActive: false }),
    orders: query({
      items: [{ publicId: '11111111-1111-1111-1111-111111111111', orderNumber: 'ORD-001', orderStatus: 'processing' }],
    }),
    tickets: query({
      items: [{ publicId: '22222222-2222-2222-2222-222222222222', ticketNumber: 'SUP-001', subject: '保固問題' }],
    }),
    grant: mutation(),
    withdraw: mutation(),
    send: mutation(),
  }
})

vi.mock('../../features/aiSupport/queries', () => ({
  useAiConsentQuery: () => mocks.consent,
  useAiUsageQuery: () => mocks.usage,
  useAiOrdersQuery: () => mocks.orders,
  useGrantAiConsentMutation: () => mocks.grant,
  useWithdrawAiConsentMutation: () => mocks.withdraw,
  useSendAiSupportMessageMutation: () => mocks.send,
}))

vi.mock('../../features/support/queries', () => ({
  useSupportTicketsQuery: () => mocks.tickets,
}))

const global = {
  stubs: {
    RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
    LoadingState: { template: '<p>loading</p>' },
    ErrorState: { template: '<p>error</p>' },
  },
}

describe('SupportHomePage', () => {
  beforeEach(() => {
    mocks.consent.data.value = { state: 'missing', policyVersion: 3, locale: null, decidedAtUtc: null }
    mocks.grant.isError.value = false
    mocks.grant.error.value = null
    mocks.grant.mutateAsync.mockReset().mockResolvedValue(undefined)
    mocks.withdraw.mutateAsync.mockReset().mockResolvedValue(undefined)
    mocks.send.data.value = undefined
    mocks.send.isError.value = false
    mocks.send.error.value = null
    mocks.send.mutateAsync.mockReset()
    mocks.send.reset.mockReset()
  })

  it('requires explicit consent and submits the server policy version', async () => {
    const wrapper = mount(SupportHomePage, { global })
    const button = wrapper.get('button')

    expect(button.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('姓名、電話、地址與 Email 不會提供給 AI')
    await wrapper.get('input[type="checkbox"]').setValue(true)
    await button.trigger('click')

    expect(mocks.grant.mutateAsync).toHaveBeenCalledWith({
      policyVersion: 3,
      locale: 'zh-TW',
      accepted: true,
    })
  })

  it('sends only explicitly selected owner references and renders trusted citations', async () => {
    mocks.consent.data.value = { state: 'granted', policyVersion: 1, locale: 'zh-TW' }
    const answer = {
      conversationPublicId: '33333333-3333-3333-3333-333333333333',
      interactionPublicId: '44444444-4444-4444-4444-444444444444',
      answer: '請由訂單頁提出申請。',
      citations: [{ type: 'order', label: 'ORD-001', resourcePublicId: '11111111-1111-1111-1111-111111111111', url: null }],
      usage: { remainingRequests: 17, resetAtUtc: '2026-08-29T16:00:00Z' },
      resultCode: 'answered',
      degradationMode: 'none',
      disclaimerKey: 'ai.answer.verifyImportantInformation',
    }
    mocks.send.data.value = answer
    mocks.send.mutateAsync.mockResolvedValue(answer)
    const wrapper = mount(SupportHomePage, { global })
    const references = wrapper.findAll('details input[type="checkbox"]')
    await references[0]!.setValue(true)
    await references[1]!.setValue(true)
    await wrapper.get('textarea').setValue('我可以申請退貨嗎？')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(mocks.send.mutateAsync).toHaveBeenCalledWith({
      conversationPublicId: null,
      message: '我可以申請退貨嗎？',
      referencedOrderPublicIds: ['11111111-1111-1111-1111-111111111111'],
      referencedSupportTicketPublicIds: ['22222222-2222-2222-2222-222222222222'],
      locale: 'zh-TW',
    })
    expect(wrapper.text()).toContain('請由訂單頁提出申請。')
    expect(wrapper.text()).toContain('ORD-001')
    expect(wrapper.text()).toContain('AI 回答可能有誤')
  })

  it('offers a human support case when AI is unavailable', () => {
    mocks.consent.data.value = { state: 'granted', policyVersion: 1, locale: 'zh-TW' }
    mocks.send.isError.value = true
    mocks.send.error.value = new ApiError('AI 暫時無法使用', {
      status: 503,
      code: 'ai_service_unavailable',
    })

    const wrapper = mount(SupportHomePage, { global })

    expect(wrapper.text()).toContain('AI 暫時無法使用')
    expect(wrapper.get('a[href="/support/tickets/new"]').text()).toContain('建立人工客服案件')
  })
})

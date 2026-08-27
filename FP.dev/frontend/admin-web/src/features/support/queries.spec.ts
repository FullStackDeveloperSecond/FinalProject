import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'

const ticketId = '018f2e6a-0000-7000-8000-000000000001'
let runHarness: () => void

const Harness = defineComponent({
  setup() {
    runHarness()
    return () => null
  },
})

describe('admin support queries', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.resetModules()
  })

  it('loads the SLA queue with credentials and the opaque cursor', async () => {
    const fetchStub = vi.fn<typeof fetch>().mockResolvedValue(Response.json({
      items: [],
      nextCursor: null,
      hasMore: false,
    }))
    vi.stubGlobal('fetch', fetchStub)
    const { useSupportSlaQueueQuery } = await import('./queries')
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    runHarness = () => useSupportSlaQueueQuery({ pageSize: 20, cursor: 'opaque+cursor/==' })
    const wrapper = mount(Harness, {
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })

    await vi.waitFor(() => expect(fetchStub).toHaveBeenCalledOnce())
    const [input, init] = fetchStub.mock.calls[0] ?? []
    const request = input instanceof Request ? input : new Request(String(input), init)
    const url = new URL(request.url)
    expect(url.pathname).toBe('/api/v1/admin/support-tickets/sla')
    expect(url.searchParams.get('PageSize')).toBe('20')
    expect(url.searchParams.get('Cursor')).toBe('opaque+cursor/==')
    expect(request.credentials).toBe('include')
    expect(request.headers.get('X-Correlation-ID')).toMatch(/^[0-9a-f]{32}$/)

    wrapper.unmount()
  })

  it('claims with the current RowVersion and refreshes detail and SLA data on success', async () => {
    const claimedTicket = {
      publicId: ticketId,
      rowVersion: 'AAAAAAAAAAI=',
    }
    const fetchStub = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(Response.json({ requestToken: 'admin-csrf-token' }))
      .mockResolvedValueOnce(Response.json(claimedTicket))
    vi.stubGlobal('fetch', fetchStub)
    const { useClaimSupportTicketMutation } = await import('./queries')
    const queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')
    let claim!: (request: { rowVersion: string }) => Promise<unknown>
    runHarness = () => {
      const mutation = useClaimSupportTicketMutation(ticketId)
      claim = request => mutation.mutateAsync(request)
    }
    const wrapper = mount(Harness, {
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })

    await expect(claim({ rowVersion: 'AAAAAAAAAAE=' })).resolves.toEqual(claimedTicket)
    expect(fetchStub).toHaveBeenCalledTimes(2)
    expect(fetchStub.mock.calls[0]?.[0])
      .toBe('http://localhost:5126/api/v1/security/antiforgery-token')
    expect(new Headers(fetchStub.mock.calls[0]?.[1]?.headers).get('X-DoSelect-Client')).toBe('admin')

    const [claimUrl, claimInit] = fetchStub.mock.calls[1] ?? []
    const claimRequest = claimUrl instanceof Request
      ? claimUrl
      : new Request(String(claimUrl), claimInit)
    expect(claimRequest.url)
      .toBe(`http://localhost:5126/api/v1/admin/support-tickets/${ticketId}/actions/claim`)
    expect(claimRequest.method).toBe('POST')
    expect(claimRequest.credentials).toBe('include')
    await expect(claimRequest.clone().json()).resolves.toEqual({ rowVersion: 'AAAAAAAAAAE=' })
    const headers = claimRequest.headers
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(headers.get('X-XSRF-TOKEN')).toBe('admin-csrf-token')
    expect(headers.get('X-Correlation-ID')).toMatch(/^[0-9a-f]{32}$/)
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: ['admin-support-ticket-detail', ticketId],
    })
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: ['admin-support-sla-queue'],
    })

    wrapper.unmount()
  })

  it('refreshes detail and SLA data when another administrator wins the claim race', async () => {
    const fetchStub = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(Response.json({ requestToken: 'admin-csrf-token' }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        status: 409,
        code: 'support_ticket_assignment_conflict',
        detail: 'The ticket has already been assigned.',
      }), {
        status: 409,
        headers: { 'Content-Type': 'application/problem+json' },
      }))
    vi.stubGlobal('fetch', fetchStub)
    const { useClaimSupportTicketMutation } = await import('./queries')
    const queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')
    let claim!: (request: { rowVersion: string }) => Promise<unknown>
    runHarness = () => {
      const mutation = useClaimSupportTicketMutation(ticketId)
      claim = request => mutation.mutateAsync(request)
    }
    const wrapper = mount(Harness, {
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })

    await expect(claim({ rowVersion: 'AAAAAAAAAAE=' })).rejects.toMatchObject({
      status: 409,
      code: 'support_ticket_assignment_conflict',
    })
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: ['admin-support-ticket-detail', ticketId],
    })
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: ['admin-support-sla-queue'],
    })

    wrapper.unmount()
  })
})

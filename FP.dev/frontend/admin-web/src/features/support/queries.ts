import {
  createAntiforgeryTokenProvider,
  createApiError,
  createCorrelationId,
  createNetworkError,
} from '@doselect/web-shared/api'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRefOrGetter } from 'vue'
import { computed, toValue } from 'vue'
import { apiBaseUrl } from '../../api/client'
import type {
  AdminSupportTicketDetailDto,
  AdminSupportTicketDto,
  ClaimSupportTicketRequest,
  SupportSlaQueuePage,
} from './types'

const slaQueueRootKey = 'admin-support-sla-queue'
const ticketDetailRootKey = 'admin-support-ticket-detail'

export const defaultSlaPageSize = 20

// AdminSupportTicketsController's endpoints predate the generated OpenAPI schema (see the
// comment in ./types), so they cannot be called through a typed `apiClient`. This provider
// replicates api/client.ts's antiforgery convention for the one write request (claim) this
// module needs to make.
const adminSupportAntiforgeryTokenProvider = createAntiforgeryTokenProvider({
  baseUrl: apiBaseUrl,
  client: 'admin',
})

async function getJson<T>(path: string): Promise<T> {
  let response: Response
  try {
    response = await fetch(`${apiBaseUrl}${path}`, {
      credentials: 'include',
      headers: { 'X-Correlation-ID': createCorrelationId() },
    })
  }
  catch (cause) {
    throw createNetworkError(cause)
  }

  if (!response.ok) {
    throw await createApiError(response)
  }

  return await response.json() as T
}

export interface SupportSlaQueueFilters {
  pageSize?: number
  cursor?: string
}

export function supportSlaQueueQueryKey(filters: SupportSlaQueueFilters = {}) {
  return [slaQueueRootKey, filters] as const
}

export function supportTicketDetailQueryKey(ticketId: string) {
  return [ticketDetailRootKey, ticketId] as const
}

export function useSupportSlaQueueQuery(filters: MaybeRefOrGetter<SupportSlaQueueFilters> = {}) {
  return useQuery({
    queryKey: computed(() => supportSlaQueueQueryKey(toValue(filters))),
    queryFn: (): Promise<SupportSlaQueuePage> => {
      const current = toValue(filters)
      const params = new URLSearchParams()
      params.set('pageSize', String(current.pageSize ?? defaultSlaPageSize))
      if (current.cursor) {
        params.set('cursor', current.cursor)
      }
      return getJson<SupportSlaQueuePage>(`/api/v1/admin/support-tickets/sla?${params.toString()}`)
    },
  })
}

export function useSupportTicketDetailQuery(ticketId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => supportTicketDetailQueryKey(toValue(ticketId))),
    queryFn: (): Promise<AdminSupportTicketDetailDto> =>
      getJson<AdminSupportTicketDetailDto>(`/api/v1/admin/support-tickets/${toValue(ticketId)}`),
    enabled: computed(() => Boolean(toValue(ticketId))),
  })
}

export function useClaimSupportTicketMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: ClaimSupportTicketRequest): Promise<AdminSupportTicketDto> => {
      const headers = new Headers({
        'Content-Type': 'application/json',
        'X-Correlation-ID': createCorrelationId(),
      })
      const token = await adminSupportAntiforgeryTokenProvider.getToken()
      if (token) {
        headers.set('X-XSRF-TOKEN', token)
      }

      let response: Response
      try {
        response = await fetch(
          `${apiBaseUrl}/api/v1/admin/support-tickets/${toValue(ticketId)}/actions/claim`,
          {
            method: 'POST',
            credentials: 'include',
            headers,
            body: JSON.stringify(request),
          },
        )
      }
      catch (cause) {
        throw createNetworkError(cause)
      }

      if (!response.ok) {
        throw await createApiError(response)
      }

      return await response.json() as AdminSupportTicketDto
    },
    onSuccess: async () => {
      // The claim response (AdminSupportTicketDto) omits IsOverdue/Messages, so it cannot
      // replace the cached AdminSupportTicketDetailDto directly — invalidate and refetch instead.
      await queryClient.invalidateQueries({ queryKey: supportTicketDetailQueryKey(toValue(ticketId)) })
      await queryClient.invalidateQueries({ queryKey: [slaQueueRootKey] })
    },
  })
}

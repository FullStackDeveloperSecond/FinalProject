import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRefOrGetter } from 'vue'
import { computed, toValue } from 'vue'
import { apiClient } from '../../api/client'
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
    queryFn: async (): Promise<SupportSlaQueuePage> => {
      const current = toValue(filters)
      const { data } = await apiClient.GET('/api/v1/admin/support-tickets/sla', {
        params: { query: { PageSize: current.pageSize ?? defaultSlaPageSize, Cursor: current.cursor } },
      })
      return data as SupportSlaQueuePage
    },
  })
}

export function useSupportTicketDetailQuery(ticketId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => supportTicketDetailQueryKey(toValue(ticketId))),
    queryFn: async (): Promise<AdminSupportTicketDetailDto> => {
      const { data } = await apiClient.GET('/api/v1/admin/support-tickets/{id}', {
        params: { path: { id: toValue(ticketId) } },
      })
      return data as AdminSupportTicketDetailDto
    },
    enabled: computed(() => Boolean(toValue(ticketId))),
  })
}

export function useClaimSupportTicketMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: ClaimSupportTicketRequest): Promise<AdminSupportTicketDto> => {
      const { data } = await apiClient.POST('/api/v1/admin/support-tickets/{id}/actions/claim', {
        params: { path: { id: toValue(ticketId) } },
        body: request,
      })
      return data as AdminSupportTicketDto
    },
    onSuccess: async () => {
      // The claim response (AdminSupportTicketDto) omits IsOverdue/Messages, so it cannot
      // replace the cached AdminSupportTicketDetailDto directly — invalidate and refetch instead.
      await queryClient.invalidateQueries({ queryKey: supportTicketDetailQueryKey(toValue(ticketId)) })
      await queryClient.invalidateQueries({ queryKey: [slaQueueRootKey] })
    },
  })
}

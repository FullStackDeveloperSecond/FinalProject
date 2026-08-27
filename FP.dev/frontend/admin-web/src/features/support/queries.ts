import { isApiError } from '@doselect/web-shared/api'
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/vue-query'
import type { MaybeRefOrGetter } from 'vue'
import { computed, toValue } from 'vue'
import { apiClient } from '../../api/client'
import type {
  AdminSupportTicketDetailDto,
  AdminSupportTicketDto,
  AssignSupportTicketRequest,
  CancelSupportTicketByAdminRequest,
  ChangeSupportTicketPriorityRequest,
  ChangeSupportTicketStatusRequest,
  ClaimSupportTicketRequest,
  ReopenSupportTicketRequest,
  SupportSlaQueuePage,
  TransferSupportTicketRequest,
} from './types'

const slaQueueRootKey = 'admin-support-sla-queue'
const ticketDetailRootKey = 'admin-support-ticket-detail'
// DES-23 requirement 七: every action's success/409-conflict path must also refetch the Case
// Workbench. No admin-web page consumes this query yet (Case Workbench has no frontend route in
// this baseline — only the backend endpoint exists), but the key is defined here so a future
// Case Workbench page can adopt it and immediately benefit from this invalidation.
const caseWorkbenchRootKey = 'admin-case-workbench'

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

// DEC-P282 / DES-23 requirement 七: on a 409 conflict, the UI must never keep a stale RowVersion
// or assignee on screen. The fix is sequencing, not just which queries get invalidated — mark the
// cached detail/SLA/workbench data stale FIRST (invalidateQueries), THEN await the refetch it
// triggers, so by the time a conflict message is shown the screen is already displaying the
// latest server state rather than the pre-conflict snapshot. All three projections (ticket
// detail, SLA queue, Case Workbench) are refreshed on both success and conflict, since either
// path can change what a queue or workbench listing shows for this ticket.
async function invalidateSupportProjections(queryClient: QueryClient, ticketId: string): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: supportTicketDetailQueryKey(ticketId) }),
    queryClient.invalidateQueries({ queryKey: [slaQueueRootKey] }),
    queryClient.invalidateQueries({ queryKey: [caseWorkbenchRootKey] }),
  ])
}

function isSupportAssignmentConflict(error: unknown): boolean {
  return isApiError(error) && error.status === 409 && error.code === 'support_ticket_assignment_conflict'
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
      await invalidateSupportProjections(queryClient, toValue(ticketId))
    },
    onError: async (error) => {
      if (isSupportAssignmentConflict(error)) {
        // Another administrator won the assignment race. Refresh all projections so the
        // assignee, RowVersion and available actions immediately reflect the server state.
        await invalidateSupportProjections(queryClient, toValue(ticketId))
      }
    },
  })
}

export function useAssignSupportTicketMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: AssignSupportTicketRequest): Promise<AdminSupportTicketDto> => {
      const { data } = await apiClient.POST('/api/v1/admin/support-tickets/{id}/actions/assign', {
        params: { path: { id: toValue(ticketId) } },
        body: request,
      })
      return data as AdminSupportTicketDto
    },
    onSuccess: async () => {
      await invalidateSupportProjections(queryClient, toValue(ticketId))
    },
    onError: async (error) => {
      if (isSupportAssignmentConflict(error)) {
        await invalidateSupportProjections(queryClient, toValue(ticketId))
      }
    },
  })
}

export function useTransferSupportTicketMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: TransferSupportTicketRequest): Promise<AdminSupportTicketDto> => {
      const { data } = await apiClient.POST('/api/v1/admin/support-tickets/{id}/actions/transfer', {
        params: { path: { id: toValue(ticketId) } },
        body: request,
      })
      return data as AdminSupportTicketDto
    },
    onSuccess: async () => {
      await invalidateSupportProjections(queryClient, toValue(ticketId))
    },
    onError: async (error) => {
      if (isSupportAssignmentConflict(error)) {
        await invalidateSupportProjections(queryClient, toValue(ticketId))
      }
    },
  })
}

export function useChangeSupportTicketPriorityMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: ChangeSupportTicketPriorityRequest): Promise<AdminSupportTicketDetailDto> => {
      const { data } = await apiClient.POST('/api/v1/admin/support-tickets/{id}/actions/change-priority', {
        params: { path: { id: toValue(ticketId) } },
        body: request,
      })
      return data as AdminSupportTicketDetailDto
    },
    onSuccess: async () => {
      await invalidateSupportProjections(queryClient, toValue(ticketId))
    },
    onError: async (error) => {
      if (isApiError(error) && error.status === 409) {
        await invalidateSupportProjections(queryClient, toValue(ticketId))
      }
    },
  })
}

export function useChangeSupportTicketStatusMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: ChangeSupportTicketStatusRequest): Promise<AdminSupportTicketDetailDto> => {
      const { data } = await apiClient.POST('/api/v1/admin/support-tickets/{id}/actions/change-status', {
        params: { path: { id: toValue(ticketId) } },
        body: request,
      })
      return data as AdminSupportTicketDetailDto
    },
    onSuccess: async () => {
      await invalidateSupportProjections(queryClient, toValue(ticketId))
    },
    onError: async (error) => {
      if (isApiError(error) && error.status === 409) {
        await invalidateSupportProjections(queryClient, toValue(ticketId))
      }
    },
  })
}

export function useCancelSupportTicketByAdminMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: CancelSupportTicketByAdminRequest): Promise<AdminSupportTicketDetailDto> => {
      const { data } = await apiClient.POST('/api/v1/admin/support-tickets/{id}/actions/cancel', {
        params: { path: { id: toValue(ticketId) } },
        body: request,
      })
      return data as AdminSupportTicketDetailDto
    },
    onSuccess: async () => {
      await invalidateSupportProjections(queryClient, toValue(ticketId))
    },
    onError: async (error) => {
      if (isApiError(error) && error.status === 409) {
        await invalidateSupportProjections(queryClient, toValue(ticketId))
      }
    },
  })
}

export function useReopenSupportTicketMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: ReopenSupportTicketRequest): Promise<AdminSupportTicketDetailDto> => {
      const { data } = await apiClient.POST('/api/v1/admin/support-tickets/{id}/actions/reopen', {
        params: { path: { id: toValue(ticketId) } },
        body: request,
      })
      return data as AdminSupportTicketDetailDto
    },
    onSuccess: async () => {
      await invalidateSupportProjections(queryClient, toValue(ticketId))
    },
    onError: async (error) => {
      if (isApiError(error) && error.status === 409) {
        await invalidateSupportProjections(queryClient, toValue(ticketId))
      }
    },
  })
}

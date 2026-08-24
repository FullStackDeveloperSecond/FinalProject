import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRefOrGetter } from 'vue'
import { computed, toValue } from 'vue'
import { apiClient } from '../../api/client'
import type {
  CancelSupportTicketRequest,
  CreateSupportMessageRequest,
  CreateSupportTicketRequest,
  SupportTicketCategory,
  SupportTicketDto,
  SupportTicketPage,
  SupportTicketStatus,
} from './types'

export interface SupportTicketListFilters {
  status?: SupportTicketStatus
  category?: SupportTicketCategory
}

const listRootKey = 'support-tickets'

export function supportTicketsQueryKey(filters: SupportTicketListFilters = {}) {
  return [listRootKey, 'list', filters] as const
}

export function supportTicketDetailQueryKey(ticketId: string) {
  return [listRootKey, 'detail', ticketId] as const
}

export function useSupportTicketsQuery(filters: MaybeRefOrGetter<SupportTicketListFilters> = {}) {
  return useQuery({
    queryKey: computed(() => supportTicketsQueryKey(toValue(filters))),
    queryFn: async (): Promise<SupportTicketPage> => {
      const current = toValue(filters)
      const { data } = await apiClient.GET('/api/v1/support-tickets', {
        params: {
          query: {
            Statuses: current.status ? [current.status] : undefined,
            Category: current.category,
          },
        },
      })
      return data as SupportTicketPage
    },
  })
}

export function useSupportTicketQuery(ticketId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => supportTicketDetailQueryKey(toValue(ticketId))),
    queryFn: async (): Promise<SupportTicketDto> => {
      const { data } = await apiClient.GET('/api/v1/support-tickets/{id}', {
        params: { path: { id: toValue(ticketId) } },
      })
      return data as SupportTicketDto
    },
    enabled: computed(() => Boolean(toValue(ticketId))),
  })
}

export function useCreateSupportTicketMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: CreateSupportTicketRequest): Promise<SupportTicketDto> => {
      const { data } = await apiClient.POST('/api/v1/support-tickets', { body: request })
      return data as SupportTicketDto
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: [listRootKey, 'list'] })
    },
  })
}

export function useAddSupportMessageMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: CreateSupportMessageRequest): Promise<SupportTicketDto> => {
      const { data } = await apiClient.POST('/api/v1/support-tickets/{id}/messages', {
        params: { path: { id: toValue(ticketId) } },
        body: request,
      })
      return data as SupportTicketDto
    },
    onSuccess: (data) => {
      queryClient.setQueryData(supportTicketDetailQueryKey(toValue(ticketId)), data)
    },
  })
}

export function useCancelSupportTicketMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: CancelSupportTicketRequest): Promise<SupportTicketDto> => {
      const { data } = await apiClient.POST('/api/v1/support-tickets/{id}/actions/cancel', {
        params: { path: { id: toValue(ticketId) } },
        body: request,
      })
      return data as SupportTicketDto
    },
    onSuccess: async (data) => {
      queryClient.setQueryData(supportTicketDetailQueryKey(toValue(ticketId)), data)
      await queryClient.invalidateQueries({ queryKey: [listRootKey, 'list'] })
    },
  })
}

import {
  createAntiforgeryTokenProvider,
  createApiError,
  createCorrelationId,
  createNetworkError,
} from '@doselect/web-shared/api'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRefOrGetter } from 'vue'
import { computed, toValue } from 'vue'
import { apiBaseUrl, apiClient } from '../../api/client'
import type {
  CancelSupportTicketRequest,
  CreateSupportMessageRequest,
  CreateSupportTicketRequest,
  SupportAttachmentDto,
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

// The attachment-upload endpoint (POST /api/v1/support-tickets/{id}/attachments) predates the
// generated OpenAPI schema (see the SupportAttachmentDto comment in ./types), so it cannot be
// called through the typed `apiClient`. This provider replicates api/client.ts's antiforgery
// convention for the one raw multipart request this module needs to make.
const attachmentAntiforgeryTokenProvider = createAntiforgeryTokenProvider({
  baseUrl: apiBaseUrl,
  client: 'member',
})

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

export function useUploadSupportAttachmentMutation(ticketId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (file: File): Promise<SupportAttachmentDto> => {
      const body = new FormData()
      body.append('file', file)

      const headers = new Headers({ 'X-Correlation-ID': createCorrelationId() })
      const token = await attachmentAntiforgeryTokenProvider.getToken()
      if (token) {
        headers.set('X-XSRF-TOKEN', token)
      }

      let response: Response
      try {
        response = await fetch(`${apiBaseUrl}/api/v1/support-tickets/${toValue(ticketId)}/attachments`, {
          method: 'POST',
          credentials: 'include',
          headers,
          body,
        })
      }
      catch (cause) {
        throw createNetworkError(cause)
      }

      if (!response.ok) {
        throw await createApiError(response)
      }

      return await response.json() as SupportAttachmentDto
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: supportTicketDetailQueryKey(toValue(ticketId)) })
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

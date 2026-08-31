import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { apiClient } from '../../api/client'
import type {
  AiConsentRequest,
  AiConsentStatus,
  AiOrderPage,
  AiSupportAnswer,
  AiSupportMessageRequest,
  AiUsage,
} from './types'

const keys = {
  consent: ['ai-support', 'consent'] as const,
  usage: ['ai-support', 'usage'] as const,
}

export function useAiConsentQuery() {
  return useQuery({
    queryKey: keys.consent,
    queryFn: async (): Promise<AiConsentStatus> => {
      const { data, error } = await apiClient.GET('/api/v1/ai/consents/current')
      if (error) throw error
      return data
    },
  })
}

export function useAiUsageQuery(enabled: () => boolean) {
  return useQuery({
    queryKey: keys.usage,
    queryFn: async (): Promise<AiUsage> => {
      const { data, error } = await apiClient.GET('/api/v1/ai/usage/me')
      if (error) throw error
      return data
    },
    enabled,
  })
}

export function useAiOrdersQuery(enabled: () => boolean) {
  return useQuery({
    queryKey: ['ai-support', 'orders'] as const,
    queryFn: async (): Promise<AiOrderPage> => {
      const { data, error } = await apiClient.GET('/api/v1/orders', {
        params: { query: { pageNumber: 1, pageSize: 10 } },
      })
      if (error) throw error
      return data
    },
    enabled,
  })
}

export function useGrantAiConsentMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: AiConsentRequest): Promise<AiConsentStatus> => {
      const { data, error } = await apiClient.POST('/api/v1/ai/consents', { body })
      if (error) throw error
      return data
    },
    onSuccess: (consent) => queryClient.setQueryData(keys.consent, consent),
  })
}

export function useWithdrawAiConsentMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (): Promise<AiConsentStatus> => {
      const { data, error } = await apiClient.DELETE('/api/v1/ai/consents/current')
      if (error) throw error
      return data
    },
    onSuccess: (consent) => queryClient.setQueryData(keys.consent, consent),
  })
}

export function useSendAiSupportMessageMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: AiSupportMessageRequest): Promise<AiSupportAnswer> => {
      const { data, error } = await apiClient.POST('/api/v1/ai/support/messages', { body })
      if (error) throw error
      return data
    },
    onSuccess: (answer) => {
      queryClient.setQueryData<AiUsage | undefined>(keys.usage, (usage) => usage
        ? { ...usage, usedRequests: Number(usage.requestLimit) - Number(answer.usage.remainingRequests) }
        : usage)
    },
  })
}

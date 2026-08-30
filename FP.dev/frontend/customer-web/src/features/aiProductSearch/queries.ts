import { useMutation } from '@tanstack/vue-query'
import { apiClient } from '../../api/client'
import type { AiProductSearchRequest, AiProductSearchResult } from './types'

export function useAiProductSearchMutation() {
  return useMutation({
    mutationFn: async (body: AiProductSearchRequest): Promise<AiProductSearchResult> => {
      const { data, error } = await apiClient.POST('/api/v1/ai/product-search/recommendations', { body })
      if (error) throw error
      return data
    },
  })
}

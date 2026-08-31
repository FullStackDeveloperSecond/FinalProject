import { useQuery } from '@tanstack/vue-query'
import { apiClient } from '../../api/client'
import type { AdminAiUsageReport } from './types'

export function useAdminAiUsageQuery() {
  return useQuery({
    queryKey: ['admin-ai-usage', 'last-30-days'] as const,
    queryFn: async (): Promise<AdminAiUsageReport> => {
      const { data, error } = await apiClient.GET('/api/v1/admin/ai/usage')
      if (error) throw error
      return data
    },
  })
}

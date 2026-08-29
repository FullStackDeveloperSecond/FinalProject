import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { toValue, type MaybeRefOrGetter } from 'vue'
import { apiClient } from '../../api/client'
import type { ReviewModerationRequest } from './types'

const reviewKeys = {
  list: (status: MaybeRefOrGetter<string>) => ['admin-reviews', 'list', toValue(status)] as const,
}
export function useAdminReviewsQuery(status: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: reviewKeys.list(status),
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/v1/admin/reviews', {
        params: { query: { status: toValue(status) || undefined } },
      })
      if (error) throw error
      return data
    },
  })
}

export function useModerateReviewMutation(status: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, action, body }: { id: string; action: string; body: ReviewModerationRequest }) => {
      const { data, error } = await apiClient.POST('/api/v1/admin/reviews/{id}/actions/{moderationAction}', {
        params: { path: { id, moderationAction: action } },
        body,
      })
      if (error) throw error
      return data
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: reviewKeys.list(status) }),
  })
}

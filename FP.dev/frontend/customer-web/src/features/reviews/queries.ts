import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { toValue, type MaybeRefOrGetter } from 'vue'
import { apiClient } from '../../api/client'
import type { CreateReviewRequest, UpdateReviewRequest } from './types'

const reviewKeys = {
  eligible: () => ['reviews', 'eligible'] as const,
  mine: () => ['reviews', 'mine'] as const,
  public: (productId: MaybeRefOrGetter<string>) => ['reviews', 'public', toValue(productId)] as const,
}

function invalidateMemberLists(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: reviewKeys.eligible() })
  void queryClient.invalidateQueries({ queryKey: reviewKeys.mine() })
}

export function useEligibleReviewItemsQuery() {
  return useQuery({
    queryKey: reviewKeys.eligible(),
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/v1/reviews/eligible-order-items', {})
      if (error) throw error
      return data
    },
  })
}

export function useMyReviewsQuery() {
  return useQuery({
    queryKey: reviewKeys.mine(),
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/v1/reviews/mine', {})
      if (error) throw error
      return data
    },
  })
}

export function usePublicProductReviewsQuery(productId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: reviewKeys.public(productId),
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/v1/products/{productId}/reviews', {
        params: { path: { productId: toValue(productId) } },
      })
      if (error) throw error
      return data
    },
  })
}

export function useCreateReviewMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreateReviewRequest) => {
      const { data, error } = await apiClient.POST('/api/v1/reviews', { body })
      if (error) throw error
      return data
    },
    onSuccess: () => invalidateMemberLists(queryClient),
  })
}

export function useUpdateReviewMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: UpdateReviewRequest }) => {
      const { data, error } = await apiClient.PUT('/api/v1/reviews/{id}', {
        params: { path: { id } },
        body,
      })
      if (error) throw error
      return data
    },
    onSuccess: () => invalidateMemberLists(queryClient),
  })
}

export function useSubmitReviewMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, rowVersion }: { id: string; rowVersion: string }) => {
      const { data, error } = await apiClient.POST('/api/v1/reviews/{id}/actions/submit', {
        params: { path: { id } },
        body: { rowVersion },
      })
      if (error) throw error
      return data
    },
    onSuccess: () => invalidateMemberLists(queryClient),
  })
}

export function useWithdrawReviewMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, rowVersion }: { id: string; rowVersion: string }) => {
      const { error } = await apiClient.DELETE('/api/v1/reviews/{id}', {
        params: { path: { id }, query: { rowVersion } },
      })
      if (error) throw error
    },
    onSuccess: () => invalidateMemberLists(queryClient),
  })
}

export function useUploadReviewImageMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, rowVersion, file }: { id: string; rowVersion: string; file: File }) => {
      const formData = new FormData()
      formData.set('file', file)
      formData.set('rowVersion', rowVersion)
      const { data, error } = await apiClient.POST('/api/v1/reviews/{id}/images', {
        params: { path: { id } },
        body: formData as unknown as { file?: string; rowVersion?: string },
      })
      if (error) throw error
      return data
    },
    onSuccess: () => invalidateMemberLists(queryClient),
  })
}

export function useDeleteReviewImageMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, sortOrder, rowVersion }: { id: string; sortOrder: number; rowVersion: string }) => {
      const { data, error } = await apiClient.DELETE('/api/v1/reviews/{id}/images/{sortOrder}', {
        params: { path: { id, sortOrder }, query: { rowVersion } },
      })
      if (error) throw error
      return data
    },
    onSuccess: () => invalidateMemberLists(queryClient),
  })
}

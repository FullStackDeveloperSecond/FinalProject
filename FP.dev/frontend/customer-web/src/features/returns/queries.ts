import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { toValue, type MaybeRefOrGetter } from 'vue'
import { apiClient } from '../../api/client'
import type { CreateReturnRequestBody } from './types'

const returnsKeys = {
  detail: (id: MaybeRefOrGetter<string>) => ['returns', 'detail', toValue(id)] as const,
}

export function useReturnQuery(returnId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: returnsKeys.detail(returnId),
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/v1/returns/{id}', {
        params: { path: { id: toValue(returnId) } },
      })
      if (error) {
        throw error
      }

      return data
    },
  })
}

export function useCreateReturnMutation(orderId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreateReturnRequestBody) => {
      const { data, error } = await apiClient.POST('/api/v1/orders/{orderId}/returns', {
        params: { path: { orderId: toValue(orderId) } },
        body,
      })
      if (error) {
        throw error
      }

      return data
    },
    onSuccess: (created) => {
      queryClient.setQueryData(returnsKeys.detail(created.publicId), created)
    },
  })
}

export function useUploadReturnAttachmentMutation(returnId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (file: File) => {
      const formData = new FormData()
      formData.set('file', file)
      const { data, error } = await apiClient.POST('/api/v1/returns/{id}/attachments', {
        params: { path: { id: toValue(returnId) } },
        // openapi-fetch skips JSON serialization for a FormData body and lets the browser set
        // the multipart boundary itself; the generated multipart type doesn't model that, so
        // this cast matches its declared shape rather than the real runtime value.
        body: formData as unknown as { file?: string },
      })
      if (error) {
        throw error
      }

      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: returnsKeys.detail(returnId) })
    },
  })
}

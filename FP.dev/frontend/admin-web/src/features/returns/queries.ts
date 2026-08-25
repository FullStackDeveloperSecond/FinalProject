import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { toValue, type MaybeRefOrGetter } from 'vue'
import { apiClient } from '../../api/client'
import type {
  ApproveReturnRequest,
  ExtendShipmentDeadlineRequest,
  InspectReturnRequest,
  ReceiveReturnRequest,
} from './types'

const returnsKeys = {
  list: () => ['admin-returns', 'list'] as const,
  detail: (id: MaybeRefOrGetter<string>) => ['admin-returns', 'detail', toValue(id)] as const,
}

export function useAdminReturnListQuery() {
  return useQuery({
    queryKey: returnsKeys.list(),
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/v1/admin/returns', {})
      if (error) {
        throw error
      }

      return data
    },
  })
}

export function useAdminReturnDetailQuery(returnId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: returnsKeys.detail(returnId),
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/v1/admin/returns/{id}', {
        params: { path: { id: toValue(returnId) } },
      })
      if (error) {
        throw error
      }

      return data
    },
  })
}

function useInvalidateAfterAction(returnId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return () => {
    queryClient.invalidateQueries({ queryKey: returnsKeys.detail(returnId) })
    queryClient.invalidateQueries({ queryKey: returnsKeys.list() })
  }
}

export function useReviewReturnMutation(returnId: MaybeRefOrGetter<string>) {
  const invalidate = useInvalidateAfterAction(returnId)
  return useMutation({
    mutationFn: async (body: ApproveReturnRequest) => {
      const { data, error } = await apiClient.POST('/api/v1/admin/returns/{id}/actions/review', {
        params: { path: { id: toValue(returnId) } },
        body,
      })
      if (error) {
        throw error
      }

      return data
    },
    onSuccess: invalidate,
  })
}

export function useReceiveReturnMutation(returnId: MaybeRefOrGetter<string>) {
  const invalidate = useInvalidateAfterAction(returnId)
  return useMutation({
    mutationFn: async (body: ReceiveReturnRequest) => {
      const { data, error } = await apiClient.POST('/api/v1/admin/returns/{id}/actions/receive', {
        params: { path: { id: toValue(returnId) } },
        body,
      })
      if (error) {
        throw error
      }

      return data
    },
    onSuccess: invalidate,
  })
}

export function useInspectReturnMutation(returnId: MaybeRefOrGetter<string>) {
  const invalidate = useInvalidateAfterAction(returnId)
  return useMutation({
    mutationFn: async (body: InspectReturnRequest) => {
      const { data, error } = await apiClient.POST('/api/v1/admin/returns/{id}/actions/inspect', {
        params: { path: { id: toValue(returnId) } },
        body,
      })
      if (error) {
        throw error
      }

      return data
    },
    onSuccess: invalidate,
  })
}

export function useExtendShipmentDeadlineMutation(returnId: MaybeRefOrGetter<string>) {
  const invalidate = useInvalidateAfterAction(returnId)
  return useMutation({
    mutationFn: async (body: ExtendShipmentDeadlineRequest) => {
      const { data, error } = await apiClient.POST(
        '/api/v1/admin/returns/{id}/actions/extend-shipment-deadline',
        { params: { path: { id: toValue(returnId) } }, body },
      )
      if (error) {
        throw error
      }

      return data
    },
    onSuccess: invalidate,
  })
}

import { computed, type Ref } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  executeAdminOrderAction,
  fetchAdminOrder,
  fetchAdminOrderRecipient,
  fetchAdminOrders,
  type AdminOrderActionRequestBody,
  type AdminOrderListFilters,
} from '../api'

export function useAdminOrderListQuery(filters: Ref<AdminOrderListFilters>) {
  return useQuery({
    queryKey: computed(() => ['admin-orders', 'list', filters.value] as const),
    queryFn: () => fetchAdminOrders(filters.value),
  })
}

export function useAdminOrderDetailQuery(publicId: Ref<string>) {
  return useQuery({
    queryKey: computed(() => ['admin-orders', 'detail', publicId.value] as const),
    enabled: computed(() => publicId.value.length > 0),
    queryFn: () => fetchAdminOrder(publicId.value),
  })
}

export function useAdminOrderRecipientQuery(publicId: Ref<string>, enabled: Ref<boolean>) {
  return useQuery({
    queryKey: computed(() => ['admin-orders', 'recipient', publicId.value] as const),
    enabled: computed(() => publicId.value.length > 0 && enabled.value),
    queryFn: () => fetchAdminOrderRecipient(publicId.value),
  })
}

export function useAdminOrderActionMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { publicId: string; actionName: string; request: AdminOrderActionRequestBody }) =>
      executeAdminOrderAction(input.publicId, input.actionName, input.request),
    onSuccess: async (_data, variables) => {
      await queryClient.invalidateQueries({ queryKey: ['admin-orders', 'detail', variables.publicId] })
      await queryClient.invalidateQueries({ queryKey: ['admin-orders', 'list'] })
    },
  })
}

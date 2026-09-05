import { computed, type Ref } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  executeAdminOrderAction,
  executeShipmentStatusAction,
  fetchAdminOrder,
  fetchAdminOrderRecipient,
  fetchAdminOrders,
  type AdminOrderActionRequestBody,
  type AdminOrderListFilters,
  type ShipmentStatusActionRequestBody,
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

/**
 * M-11 物流狀態命令。回應就是更新後的 AdminOrderDto（C1），直接寫進詳情快取，不用再打一次；
 * 列表的摘要狀態（已出貨／已完成）可能變了，一併失效。
 */
export function useShipmentStatusActionMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: {
      orderPublicId: string
      shipmentPublicId: string
      shipmentAction: string
      request: ShipmentStatusActionRequestBody
      idempotencyKey: string
    }) => executeShipmentStatusAction(input.shipmentPublicId, input.shipmentAction, input.request, input.idempotencyKey),
    onSuccess: async (data, variables) => {
      queryClient.setQueryData(['admin-orders', 'detail', variables.orderPublicId], data)
      await queryClient.invalidateQueries({ queryKey: ['admin-orders', 'list'] })
    },
  })
}

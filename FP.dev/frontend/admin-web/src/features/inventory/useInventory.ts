import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  listBalances,
  listMovements,
  listReservations,
  releaseReservation,
  type InventoryBalanceListParams,
  type InventoryMovementListParams,
  type InventoryReservationListParams,
} from './api'
import type { ReleaseReservationRequest } from './types'

export function useInventoryBalanceList(params: MaybeRefOrGetter<InventoryBalanceListParams>) {
  return useQuery({
    queryKey: computed(() => ['inventory', 'balances', toValue(params)] as const),
    queryFn: () => listBalances(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

export function useInventoryMovementList(params: MaybeRefOrGetter<InventoryMovementListParams>) {
  return useQuery({
    queryKey: computed(() => ['inventory', 'movements', toValue(params)] as const),
    queryFn: () => listMovements(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

export function useInventoryReservationList(params: MaybeRefOrGetter<InventoryReservationListParams>) {
  return useQuery({
    queryKey: computed(() => ['inventory', 'reservations', toValue(params)] as const),
    queryFn: () => listReservations(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

export function useReleaseReservation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: ReleaseReservationRequest }) =>
      releaseReservation(publicId, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['inventory', 'reservations'] })
      queryClient.invalidateQueries({ queryKey: ['inventory', 'balances'] })
      queryClient.invalidateQueries({ queryKey: ['inventory', 'movements'] })
    },
  })
}

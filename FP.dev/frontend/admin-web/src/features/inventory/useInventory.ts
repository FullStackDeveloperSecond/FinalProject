import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  acknowledgeReconciliationCase,
  closeReconciliationCase,
  listBalances,
  listMovements,
  listReconciliationCases,
  listReservations,
  releaseReservation,
  type InventoryBalanceListParams,
  type InventoryMovementListParams,
  type InventoryReconciliationCaseListParams,
  type InventoryReservationListParams,
  type ReconciliationCaseCloseAction,
} from './api'
import type { ReconciliationCaseResolutionRequest, ReleaseReservationRequest } from './types'

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

/** 頁碼、篩選都是 query key；不保留前一頁的可操作列，重新整理更新目前頁的狀態與 RowVersion。 */
export function useInventoryReservationList(params: MaybeRefOrGetter<Omit<InventoryReservationListParams, 'cursor'>>) {
  return useQuery({
    queryKey: computed(() => ['inventory', 'reservations', toValue(params)] as const),
    queryFn: () => listReservations(toValue(params)),
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

export function useInventoryReconciliationCaseList(params: MaybeRefOrGetter<InventoryReconciliationCaseListParams>) {
  return useQuery({
    queryKey: computed(() => ['inventory', 'reconciliation-cases', toValue(params)] as const),
    queryFn: () => listReconciliationCases(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

export function useAcknowledgeReconciliationCase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, rowVersion }: { publicId: string, rowVersion: string }) =>
      acknowledgeReconciliationCase(publicId, rowVersion),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['inventory', 'reconciliation-cases'] })
    },
  })
}

/**
 * dismiss 不動庫存、resolve 會把 Balance 改成帳本重算值並寫一筆零差額 Adjustment——所以結案後
 * 餘額與異動明細也要重抓，不只案件列表。
 */
export function useCloseReconciliationCase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, action, request }: {
      publicId: string
      action: ReconciliationCaseCloseAction
      request: ReconciliationCaseResolutionRequest
    }) => closeReconciliationCase(publicId, action, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['inventory', 'reconciliation-cases'] })
      queryClient.invalidateQueries({ queryKey: ['inventory', 'balances'] })
      queryClient.invalidateQueries({ queryKey: ['inventory', 'movements'] })
    },
  })
}

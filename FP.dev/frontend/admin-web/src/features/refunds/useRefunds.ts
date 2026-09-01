import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { executeRefund, getRefund, listRefunds, type RefundListParams } from './api'
import type { ExecuteRefundRequest } from './types'

export function useRefundList(params: MaybeRefOrGetter<RefundListParams>) {
  return useQuery({
    queryKey: computed(() => ['refunds', 'list', toValue(params)] as const),
    queryFn: () => listRefunds(toValue(params)),
    placeholderData: previous => previous,
  })
}

export function useRefund(refundPublicId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => ['refunds', 'detail', toValue(refundPublicId)] as const),
    queryFn: () => getRefund(toValue(refundPublicId)),
    enabled: computed(() => Boolean(toValue(refundPublicId))),
  })
}

export function useExecuteRefund() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      refundPublicId,
      request,
      idempotencyKey,
    }: {
      refundPublicId: string
      request: ExecuteRefundRequest
      idempotencyKey: string
    }) => executeRefund(refundPublicId, request, idempotencyKey),
    onSuccess: async (refund) => {
      queryClient.setQueryData(['refunds', 'detail', refund.publicId], refund)
      await queryClient.invalidateQueries({ queryKey: ['refunds', 'list'] })
    },
  })
}

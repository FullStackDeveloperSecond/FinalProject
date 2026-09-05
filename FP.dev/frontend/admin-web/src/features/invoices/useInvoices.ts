import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  getInvoice,
  getInvoiceIssuanceOrder,
  issueInvoice,
  listInvoices,
  type InvoiceListParams,
  voidInvoice,
} from './api'
import type { IssueSimulatedInvoiceRequest, VoidSimulatedInvoiceRequest } from './types'

export function useInvoiceList(params: MaybeRefOrGetter<InvoiceListParams>) {
  return useQuery({
    queryKey: computed(() => ['invoices', 'list', toValue(params)] as const),
    queryFn: () => listInvoices(toValue(params)),
    placeholderData: previous => previous,
  })
}

export function useInvoice(invoicePublicId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => ['invoices', 'detail', toValue(invoicePublicId)] as const),
    queryFn: () => getInvoice(toValue(invoicePublicId)),
    enabled: computed(() => Boolean(toValue(invoicePublicId))),
  })
}

export function useVoidInvoice() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ invoicePublicId, request }: {
      invoicePublicId: string
      request: VoidSimulatedInvoiceRequest
    }) => voidInvoice(invoicePublicId, request),
    onSuccess: async (invoice) => {
      queryClient.setQueryData(['invoices', 'detail', invoice.invoice.publicId], invoice)
      await queryClient.invalidateQueries({ queryKey: ['invoices', 'list'] })
    },
  })
}

export function useIssueInvoice() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ orderPublicId, request, idempotencyKey }: {
      orderPublicId: string
      request: IssueSimulatedInvoiceRequest
      idempotencyKey: string
    }) => issueInvoice(orderPublicId, request, idempotencyKey),
    onSuccess: async (invoice) => {
      queryClient.setQueryData(['invoices', 'detail', invoice.invoice.publicId], invoice)
      await queryClient.invalidateQueries({ queryKey: ['invoices', 'list'] })
    },
  })
}

export function useInvoiceIssuanceLookup() {
  return useMutation({
    mutationFn: (orderPublicId: string) => getInvoiceIssuanceOrder(orderPublicId),
  })
}

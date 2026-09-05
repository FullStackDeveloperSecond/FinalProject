import { apiClient } from '../../api/client'
import type {
  AdminInvoiceDto,
  AdminInvoicePage,
  IssueSimulatedInvoiceRequest,
  InvoiceIssuanceOrderDto,
  SimulatedInvoiceStatus,
  VoidSimulatedInvoiceRequest,
} from './types'

export interface InvoiceListParams {
  q?: string
  statuses?: SimulatedInvoiceStatus[]
  fromUtc?: string
  toUtc?: string
  pageNumber?: number
  pageSize?: number
}

export async function listInvoices(params: InvoiceListParams): Promise<AdminInvoicePage> {
  const { data } = await apiClient.GET('/api/v1/admin/invoices', {
    params: {
      query: {
        Q: params.q || undefined,
        Statuses: params.statuses?.length ? params.statuses : undefined,
        FromUtc: params.fromUtc,
        ToUtc: params.toUtc,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function getInvoice(invoicePublicId: string): Promise<AdminInvoiceDto> {
  const { data } = await apiClient.GET('/api/v1/admin/invoices/{id}', {
    params: { path: { id: invoicePublicId } },
  })
  return data!
}

export async function getInvoiceIssuanceOrder(
  orderPublicId: string,
): Promise<InvoiceIssuanceOrderDto> {
  const { data } = await apiClient.GET('/api/v1/admin/orders/{orderId}/invoice-issuance', {
    params: { path: { orderId: orderPublicId } },
  })
  return data!
}

export async function voidInvoice(
  invoicePublicId: string,
  request: VoidSimulatedInvoiceRequest,
): Promise<AdminInvoiceDto> {
  const { data } = await apiClient.POST('/api/v1/admin/invoices/{id}/actions/void', {
    params: { path: { id: invoicePublicId } },
    body: request,
  })
  return data!
}

export async function issueInvoice(
  orderPublicId: string,
  request: IssueSimulatedInvoiceRequest,
  idempotencyKey: string,
): Promise<AdminInvoiceDto> {
  const { data } = await apiClient.POST('/api/v1/admin/orders/{orderId}/invoices', {
    params: {
      path: { orderId: orderPublicId },
      header: { 'Idempotency-Key': idempotencyKey },
    },
    body: request,
  })
  return data!
}

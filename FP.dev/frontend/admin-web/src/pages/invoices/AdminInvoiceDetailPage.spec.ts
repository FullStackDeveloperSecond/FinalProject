import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import AdminInvoiceDetailPage from './AdminInvoiceDetailPage.vue'

const mocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')
  return {
    invoice: ref<Record<string, unknown> | null>(null),
    pending: ref(false),
    queryError: ref(false),
    queryFailure: ref<unknown>(null),
    refetch: vi.fn(),
    voidMutation: {
      isPending: ref(false),
      isError: ref(false),
      error: ref<unknown>(null),
      mutateAsync: vi.fn(),
    },
  }
})

vi.mock('../../features/invoices/useInvoices', () => ({
  useInvoice: () => ({
    data: mocks.invoice,
    isPending: mocks.pending,
    isError: mocks.queryError,
    error: mocks.queryFailure,
    refetch: mocks.refetch,
  }),
  useVoidInvoice: () => mocks.voidMutation,
}))

const invoicePublicId = '018f2e6a-0000-7000-8000-000000000050'

function sampleInvoice() {
  return {
    invoice: {
      publicId: invoicePublicId,
      invoiceNumber: 'DEMO-202609-000001',
      orderPublicId: '018f2e6a-0000-7000-8000-000000000051',
      status: 'issued',
      buyerType: 'individual',
      buyerEmailMasked: 'a***@example.com',
      carrierType: null,
      carrierValueMasked: null,
      companyTaxIdMasked: null,
      netAmount: 952,
      taxAmount: 48,
      grossAmount: 1000,
      currency: 'TWD',
      taxRate: 0.05,
      items: [{
        publicId: '018f2e6a-0000-7000-8000-000000000052',
        orderItemPublicId: '018f2e6a-0000-7000-8000-000000000053',
        kind: 'merchandise',
        productName: '測試商品',
        skuCode: 'SKU-1',
        quantity: 1,
        unitPrice: 1000,
        discountAmount: 0,
        netAmount: 952,
        taxAmount: 48,
        grossAmount: 1000,
      }],
      allowances: [],
      issuedAtUtc: '2026-09-01T01:00:00Z',
      voidedAtUtc: null,
      demoMarker: 'DEMO-NOT-A-TAX-INVOICE',
      rowVersion: 'AAAAAAAAAAE=',
    },
    orderNumber: 'ORD-20260901-0001',
    availableActions: ['void'],
  }
}

async function mountPage() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/invoices', component: { template: '<div />' } },
      { path: '/invoices/:invoiceId', component: { template: '<div />' } },
    ],
  })
  await router.push(`/invoices/${invoicePublicId}`)
  await router.isReady()
  return mount(AdminInvoiceDetailPage, { global: { plugins: [router] } })
}

describe('AdminInvoiceDetailPage', () => {
  beforeEach(() => {
    mocks.invoice.value = sampleInvoice()
    mocks.pending.value = false
    mocks.queryError.value = false
    mocks.queryFailure.value = null
    mocks.voidMutation.isPending.value = false
    mocks.voidMutation.isError.value = false
    mocks.voidMutation.error.value = null
    mocks.voidMutation.mutateAsync.mockReset().mockResolvedValue(undefined)
  })

  it('submits the server row version with the confirmed void reason', async () => {
    const wrapper = await mountPage()
    await wrapper.find('#invoice-void-reason').setValue('order_cancelled')
    await wrapper.find('#invoice-void-note').setValue('已核對整筆取消')
    await wrapper.find('input[type="checkbox"]').setValue(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mocks.voidMutation.mutateAsync).toHaveBeenCalledWith({
      invoicePublicId,
      request: {
        reasonCode: 'order_cancelled',
        note: '已核對整筆取消',
        rowVersion: 'AAAAAAAAAAE=',
      },
    })
  })

  it('explains that a succeeded refund requires an allowance', async () => {
    mocks.voidMutation.isError.value = true
    mocks.voidMutation.error.value = new ApiError('Conflict', {
      status: 409,
      code: 'invoice_allowance_required',
      correlationId: 'corr-invoice',
      traceId: 'trace-invoice',
    })

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('訂單已有成功退款，必須建立折讓而不能作廢')
  })
})

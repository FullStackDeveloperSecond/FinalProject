import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AdminInvoicesPage from './AdminInvoicesPage.vue'

const mocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')
  return {
    result: ref<Record<string, unknown> | null>(null),
    pending: ref(false),
    failed: ref(false),
    error: ref<unknown>(null),
    refetch: vi.fn(),
    issuanceSnapshot: ref<Record<string, unknown> | null>(null),
    lookupPending: ref(false),
    lookupFailed: ref(false),
    lookupError: ref<unknown>(null),
    lookup: vi.fn(),
    issuePending: ref(false),
    issueFailed: ref(false),
    issueError: ref<unknown>(null),
    issue: vi.fn(),
    listParams: null as { value: Record<string, unknown> } | null,
  }
})

vi.mock('../../features/invoices/useInvoices', () => ({
  useInvoiceList: (params: { value: Record<string, unknown> }) => {
    mocks.listParams = params
    return {
      data: mocks.result,
      isPending: mocks.pending,
      isError: mocks.failed,
      error: mocks.error,
      refetch: mocks.refetch,
    }
  },
  useInvoiceIssuanceLookup: () => ({
    data: mocks.issuanceSnapshot,
    isPending: mocks.lookupPending,
    isError: mocks.lookupFailed,
    error: mocks.lookupError,
    mutateAsync: mocks.lookup,
    reset: vi.fn(),
  }),
  useIssueInvoice: () => ({
    isPending: mocks.issuePending,
    isError: mocks.issueFailed,
    error: mocks.issueError,
    mutateAsync: mocks.issue,
  }),
}))

async function mountPage() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/invoices', component: { template: '<div />' } },
      { path: '/invoices/:invoiceId', component: { template: '<div />' } },
    ],
  })
  await router.push('/invoices')
  await router.isReady()
  return mount(AdminInvoicesPage, { global: { plugins: [router, PrimeVue] } })
}

describe('AdminInvoicesPage', () => {
  beforeEach(() => {
    mocks.pending.value = false
    mocks.failed.value = false
    mocks.error.value = null
    mocks.issuanceSnapshot.value = null
    mocks.lookupPending.value = false
    mocks.lookupFailed.value = false
    mocks.lookupError.value = null
    mocks.lookup.mockReset()
    mocks.issuePending.value = false
    mocks.issueFailed.value = false
    mocks.issueError.value = null
    mocks.issue.mockReset()
    mocks.listParams = null
  })

  it('renders the masked invoice summary and demo warning', async () => {
    mocks.result.value = {
      items: [{
        publicId: '018f2e6a-0000-7000-8000-000000000060',
        invoiceNumber: 'DEMO-202609-000001',
        orderPublicId: '018f2e6a-0000-7000-8000-000000000061',
        orderNumber: 'ORD-20260901-0001',
        status: 'issued',
        netAmount: 952,
        taxAmount: 48,
        grossAmount: 1000,
        issuedAtUtc: '2026-09-01T01:00:00Z',
        demoMarker: 'DEMO-NOT-A-TAX-INVOICE',
        rowVersion: 'AAAAAAAAAAE=',
      }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    }

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('DEMO 模擬發票')
    expect(wrapper.get('form[aria-label="發票搜尋"]').classes()).toContain('finance-filter')
    expect(wrapper.get('#invoice-query').element.parentElement?.classList.contains('finance-field')).toBe(true)
    expect(wrapper.get('.table-scroll').attributes('tabindex')).toBe('0')
    expect(wrapper.text()).toContain('DEMO-202609-000001')
    expect(wrapper.text()).toContain('ORD-20260901-0001')
    expect(wrapper.find('a[href="/invoices/018f2e6a-0000-7000-8000-000000000060"]').exists()).toBe(true)
  })

  it('looks up the order by its visible order number and issues with the resolved public id', async () => {
    const orderPublicId = '018f2e6a-0000-7000-8000-000000000061'
    const orderNumber = 'ORD-20260901-0001'
    const rowVersion = 'AQIDBAUGBwg='
    mocks.lookup.mockImplementation(async () => {
      const snapshot = {
        orderPublicId,
        orderNumber,
        orderIsPaid: true,
        orderIsCancelled: false,
        rowVersion,
        hasInvoice: false,
      }
      mocks.issuanceSnapshot.value = snapshot
      return snapshot
    })
    mocks.issue.mockResolvedValue({
      invoice: { publicId: '018f2e6a-0000-7000-8000-000000000060' },
    })
    const wrapper = await mountPage()

    expect(wrapper.get('label[for="invoice-order-number"]').text()).toBe('訂單號碼')
    await wrapper.get('#invoice-order-number').setValue(orderNumber)
    await wrapper.get('form[aria-label="手動開立模擬發票"]').trigger('submit')
    await wrapper.vm.$nextTick()

    expect(mocks.lookup).toHaveBeenCalledWith(orderNumber)
    expect(wrapper.text()).toContain(orderNumber)
    await wrapper.get('[data-test="issue-invoice"]').trigger('click')

    expect(mocks.issue).toHaveBeenCalledOnce()
    expect(mocks.issue.mock.calls[0]?.[0]).toMatchObject({
      orderPublicId,
      request: { orderRowVersion: rowVersion },
    })
    expect(mocks.issue.mock.calls[0]?.[0]?.idempotencyKey).toEqual(expect.any(String))
  })

  it('requests server-side ascending and descending sorting from sortable headers', async () => {
    mocks.result.value = {
      items: [{
        publicId: '018f2e6a-0000-7000-8000-000000000060',
        invoiceNumber: 'DEMO-202609-000001',
        orderPublicId: '018f2e6a-0000-7000-8000-000000000061',
        orderNumber: 'ORD-20260901-0001',
        status: 'issued',
        netAmount: 952,
        taxAmount: 48,
        grossAmount: 1000,
        issuedAtUtc: '2026-09-01T01:00:00Z',
        demoMarker: 'DEMO-NOT-A-TAX-INVOICE',
        rowVersion: 'AAAAAAAAAAE=',
      }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    }
    const wrapper = await mountPage()

    expect(mocks.listParams?.value).toMatchObject({ sort: 'invoiceNumberDesc' })
    const invoiceNumberHeader = wrapper.get('th[data-sort="invoiceNumber"]')
    expect(invoiceNumberHeader.attributes('aria-sort')).toBe('descending')
    await invoiceNumberHeader.get('button').trigger('click')
    expect(mocks.listParams?.value).toMatchObject({ sort: 'invoiceNumberAsc', pageNumber: 1 })
    expect(invoiceNumberHeader.attributes('aria-sort')).toBe('ascending')

    const orderNumberHeader = wrapper.get('th[data-sort="orderNumber"]')
    await orderNumberHeader.get('button').trigger('click')
    expect(mocks.listParams?.value).toMatchObject({ sort: 'orderNumberAsc', pageNumber: 1 })
  })
})
import PrimeVue from 'primevue/config'

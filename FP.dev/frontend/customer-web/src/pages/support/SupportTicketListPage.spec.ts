import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { createDoSelectClient } from '@doselect/web-shared/api'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import SupportTicketListPage from './SupportTicketListPage.vue'

// openapi-fetch resolves `fetch` once at client-creation time, so vi.stubGlobal('fetch', ...)
// never reaches the module-level `apiClient` singleton created in ../../api/client.ts —
// mirrors the pattern already used in src/api/shared-api.spec.ts (inject fetch directly).
vi.mock('../../api/client', async () => {
  const actual = await vi.importActual<typeof import('../../api/client')>('../../api/client')
  return {
    ...actual,
    get apiClient() {
      return createDoSelectClient({ baseUrl: 'http://localhost:5126', fetch: fetchStub })
    },
  }
})

let fetchStub: typeof fetch = async () => Response.json({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })

function samplePage() {
  return {
    items: [
      {
        publicId: '018f2e6a-0000-7000-8000-000000000001',
        ticketNumber: 'CS-20260819-0001',
        category: 'order',
        subject: '訂單延遲問題',
        status: 'open',
        priority: 'normal',
        lastActivityAtUtc: '2026-08-19T03:00:00Z',
        rowVersion: 'AAAAAAAAAAE=',
      },
    ],
    pageNumber: 1,
    pageSize: 20,
    totalCount: 1,
    totalPages: 1,
  }
}

function sampleTicketDetail() {
  return {
    publicId: '018f2e6a-0000-7000-8000-000000000001',
    ticketNumber: 'CS-20260819-0001',
    category: 'order',
    subject: '訂單延遲問題',
    status: 'open',
    priority: 'normal',
    createdAtUtc: '2026-08-19T02:00:00Z',
    lastActivityAtUtc: '2026-08-19T03:00:00Z',
    rowVersion: 'AAAAAAAAAAE=',
    messages: [
      {
        publicId: '018f2e6a-0000-7000-8000-0000000000a1',
        senderType: 'member',
        body: '請問我的訂單什麼時候出貨？',
        sentAtUtc: '2026-08-19T02:00:00Z',
      },
      {
        publicId: '018f2e6a-0000-7000-8000-0000000000a2',
        senderType: 'admin',
        body: '您好，訂單已在出貨排程中。',
        sentAtUtc: '2026-08-19T03:00:00Z',
      },
    ],
    attachments: [],
  }
}

describe('supportTicketListPage', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  afterEach(() => {
    localStorage.clear()
  })

  it('renders the caller\'s tickets returned by the API', async () => {
    fetchStub = async () => Response.json(samplePage())

    const router = createRouter({ history: createMemoryHistory(), routes: [] })
    const wrapper = mount(SupportTicketListPage, {
      global: {
        plugins: [router, [VueQueryPlugin, {
          queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }),
        }]],
      },
    })

    await vi.waitFor(() => {
      expect(wrapper.text()).toContain('CS-20260819-0001')
    })

    expect(wrapper.text()).toContain('訂單延遲問題')
    expect(wrapper.text()).toContain('共 1 筆')

    const expectedMobileLabels = ['案件編號', '分類', '主旨', '狀態', '最後活動時間', '檢視']
    expect(wrapper.findAll('tbody td').map((cell) => cell.attributes('data-label')))
      .toEqual(expectedMobileLabels)
    expect(wrapper.get('tbody a').attributes('href'))
      .toBe('/support/tickets/018f2e6a-0000-7000-8000-000000000001')
  })

  it('opens the case preview beside the list without covering it, and closes only via the close button', async () => {
    fetchStub = async (input) => {
      const url = String(typeof input === 'string' ? input : (input as Request).url)
      if (url.includes('/api/v1/support-tickets/018f2e6a-0000-7000-8000-000000000001')) {
        return Response.json(sampleTicketDetail())
      }
      return Response.json(samplePage())
    }

    const router = createRouter({ history: createMemoryHistory(), routes: [] })
    const wrapper = mount(SupportTicketListPage, {
      global: {
        plugins: [router, [VueQueryPlugin, {
          queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }),
        }]],
      },
    })

    await vi.waitFor(() => {
      expect(wrapper.text()).toContain('CS-20260819-0001')
    })

    // 預設只有列表，沒有詳細欄。
    expect(wrapper.get('.case-split').attributes('data-detail-open')).toBe('false')
    expect(wrapper.find('.case-split__detail').exists()).toBe(false)

    await wrapper.get('.support-tickets__preview-button').trigger('click')
    await vi.waitFor(() => {
      expect(wrapper.text()).toContain('請問我的訂單什麼時候出貨？')
    })

    // 列表與詳細同層並存：詳細不是覆蓋層，列表仍然在畫面上。
    expect(wrapper.get('.case-split').attributes('data-detail-open')).toBe('true')
    expect(wrapper.find('.case-split__list .support-tickets__table').exists()).toBe(true)
    expect(wrapper.find('.case-split__detail').exists()).toBe(true)
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)

    // 顧客端預覽只顯示往來訊息，不會出現客服內部備註。
    expect(wrapper.text()).not.toContain('內部備註')

    await wrapper.get('.case-split__close').trigger('click')
    expect(wrapper.find('.case-split__detail').exists()).toBe(false)
    expect(wrapper.get('.case-split').attributes('data-detail-open')).toBe('false')
  })

  it('lets the formal member policy reject an anonymous request', async () => {
    const fetchSpy = vi.fn(async () => Response.json({
      title: 'Authentication required',
      status: 401,
      code: 'authentication_required',
    }, {
      status: 401,
      headers: { 'Content-Type': 'application/problem+json' },
    }))
    fetchStub = fetchSpy

    const router = createRouter({ history: createMemoryHistory(), routes: [] })
    const wrapper = mount(SupportTicketListPage, {
      global: {
        plugins: [router, [VueQueryPlugin, {
          queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }),
        }]],
      },
    })

    await vi.waitFor(() => {
      expect(wrapper.text()).toContain('Authentication required')
    })
    expect(fetchSpy).toHaveBeenCalledOnce()
  })
})

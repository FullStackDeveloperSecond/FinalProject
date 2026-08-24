import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { createDoSelectClient } from '@doselect/web-shared/api'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import SupportTicketListPage from './SupportTicketListPage.vue'

const sessionStorageKey = 'doselect.dev-session'

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

describe('supportTicketListPage', () => {
  beforeEach(() => {
    localStorage.setItem(sessionStorageKey, 'kafen-test-member-a@doselect.local')
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
        plugins: [router, [VueQueryPlugin, { queryClient: new QueryClient() }]],
      },
    })

    await vi.waitFor(() => {
      expect(wrapper.text()).toContain('CS-20260819-0001')
    })

    expect(wrapper.text()).toContain('訂單延遲問題')
    expect(wrapper.text()).toContain('共 1 筆')
  })

  it('prompts an anonymous visitor to sign in instead of calling the API', () => {
    localStorage.clear()
    const fetchSpy = vi.fn(fetchStub)
    fetchStub = fetchSpy

    const router = createRouter({ history: createMemoryHistory(), routes: [] })
    const wrapper = mount(SupportTicketListPage, {
      global: {
        plugins: [router, [VueQueryPlugin, { queryClient: new QueryClient() }]],
      },
    })

    expect(wrapper.text()).toContain('請先登入')
    expect(fetchSpy).not.toHaveBeenCalled()
  })
})

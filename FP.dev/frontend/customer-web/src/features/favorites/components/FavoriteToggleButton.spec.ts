import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import { useSessionStore } from '../../../stores/session'
import type { CurrentUserDto } from '../../auth/api'
import type { Favorite } from '../types'

const testMember: CurrentUserDto = {
  publicId: 'member-1',
  displayName: '測試會員',
  emailMasked: 'm***@example.com',
  emailVerified: true,
  locale: 'zh-TW',
}

function favorite(productPublicId: string): Favorite {
  return {
    product: {
      productPublicId,
      productCode: 'PROD-1',
      name: '測試商品',
      listPrice: 1000,
      salePrice: null,
      currency: 'TWD',
      availability: 'available',
    },
    createdAtUtc: '2026-08-01T00:00:00Z',
  }
}

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockDelete = vi.fn()

vi.mock('../../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/client')>()
  return {
    ...actual,
    apiClient: { GET: (...args: unknown[]) => mockGet(...args), POST: (...args: unknown[]) => mockPost(...args), DELETE: (...args: unknown[]) => mockDelete(...args) },
  }
})

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

async function mountButton(productPublicId = 'product-1') {
  const { default: FavoriteToggleButton } = await import('./FavoriteToggleButton.vue')
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const pinia = createPinia()
  setActivePinia(pinia)

  const wrapper = mount(FavoriteToggleButton, {
    props: { productPublicId },
    global: {
      plugins: [[VueQueryPlugin, { queryClient }], pinia],
      stubs: globalStubs,
    },
  })
  return { wrapper, sessionStore: useSessionStore() }
}

beforeEach(() => {
  mockGet.mockReset()
  mockPost.mockReset()
  mockDelete.mockReset()
})

describe('FavoriteToggleButton', () => {
  it('shows a login link instead of the toggle when anonymous', async () => {
    const { wrapper, sessionStore } = await mountButton()
    sessionStore.status = 'anonymous'
    await flushPromises()

    expect(wrapper.text()).toContain('登入後收藏')
    expect(wrapper.find('button').exists()).toBe(false)
    expect(mockGet).not.toHaveBeenCalled()
  })

  it('shows the unfavorited state and adds the product on click', async () => {
    mockGet.mockResolvedValue({ data: [], error: undefined })
    mockPost.mockResolvedValue({ data: favorite('product-1'), error: undefined })

    const { wrapper, sessionStore } = await mountButton('product-1')
    sessionStore.status = 'authenticated'
    sessionStore.user = testMember
    await flushPromises()

    expect(wrapper.text()).toContain('加入收藏')
    expect(wrapper.get('button').attributes('aria-pressed')).toBe('false')

    await wrapper.get('button').trigger('click')
    await flushPromises()

    expect(mockPost).toHaveBeenCalledWith('/api/v1/members/me/favorites', {
      body: { productPublicId: 'product-1' },
    })
  })

  it('shows the favorited state and removes the product on click', async () => {
    mockGet.mockResolvedValue({ data: [favorite('product-1')], error: undefined })
    mockDelete.mockResolvedValue({ data: undefined, error: undefined })

    const { wrapper, sessionStore } = await mountButton('product-1')
    sessionStore.status = 'authenticated'
    sessionStore.user = testMember
    await flushPromises()

    expect(wrapper.text()).toContain('已收藏')
    expect(wrapper.get('button').attributes('aria-pressed')).toBe('true')

    await wrapper.get('button').trigger('click')
    await flushPromises()

    expect(mockDelete).toHaveBeenCalledWith('/api/v1/members/me/favorites/{productPublicId}', {
      params: { path: { productPublicId: 'product-1' } },
    })
  })

  it('shows an error message when the toggle mutation fails', async () => {
    mockGet.mockResolvedValue({ data: [], error: undefined })
    mockPost.mockResolvedValue({
      data: undefined,
      error: new ApiError('Service Unavailable', { status: 503, code: 'service_unavailable' }),
    })

    const { wrapper, sessionStore } = await mountButton('product-1')
    sessionStore.status = 'authenticated'
    sessionStore.user = testMember
    await flushPromises()

    await wrapper.get('button').trigger('click')
    await flushPromises()

    // Component logic (FavoriteToggleButton.vue): isApiError(caught) ? caught.message : fallback —
    // an ApiError's own message is shown verbatim, the Chinese fallback is only for non-ApiError throws.
    expect(wrapper.text()).toContain('Service Unavailable')
  })
})

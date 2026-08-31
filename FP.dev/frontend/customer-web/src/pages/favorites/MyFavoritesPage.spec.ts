import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import type { Favorite } from '../../features/favorites/types'

function favorite(overrides: Partial<Favorite['product']> = {}): Favorite {
  return {
    product: {
      productPublicId: 'product-1',
      productCode: 'PROD-1',
      name: 'RTX 4070',
      listPrice: 20000,
      salePrice: null,
      currency: 'TWD',
      availability: 'available',
      ...overrides,
    },
    createdAtUtc: '2026-08-01T00:00:00Z',
  }
}

const mockGet = vi.fn()
const mockDelete = vi.fn()

vi.mock('../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/client')>()
  return {
    ...actual,
    apiClient: { GET: (...args: unknown[]) => mockGet(...args), DELETE: (...args: unknown[]) => mockDelete(...args) },
  }
})

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

async function mountPage() {
  const { default: MyFavoritesPage } = await import('./MyFavoritesPage.vue')
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const pinia = createPinia()
  setActivePinia(pinia)

  return mount(MyFavoritesPage, {
    global: {
      plugins: [[VueQueryPlugin, { queryClient }], pinia],
      stubs: globalStubs,
    },
  })
}

beforeEach(() => {
  mockGet.mockReset()
  mockDelete.mockReset()
})

describe('MyFavoritesPage', () => {
  it('shows the loading state before the favorites resolve', async () => {
    mockGet.mockReturnValue(new Promise(() => {}))
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('收藏資料載入中')
  })

  it('shows an empty state when there are no favorites', async () => {
    mockGet.mockResolvedValueOnce({ data: [], error: undefined })
    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('目前沒有收藏商品')
  })

  it('renders each favorite with a link to the product page', async () => {
    mockGet.mockResolvedValueOnce({ data: [favorite()], error: undefined })
    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('RTX 4070')
    expect(wrapper.text()).toContain('NT$20,000')
    expect(wrapper.text()).toContain('現貨供應')
    expect(wrapper.find('a.favorite-card__name').exists()).toBe(true)
  })

  it('shows the sale price and strikes through the list price when discounted', async () => {
    mockGet.mockResolvedValueOnce({
      data: [favorite({ salePrice: 15000 })],
      error: undefined,
    })
    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('NT$15,000')
    expect(wrapper.find('.favorite-card__price-original').text()).toContain('NT$20,000')
  })

  it('renders an unlisted favorite as plain text, not a link', async () => {
    mockGet.mockResolvedValueOnce({
      data: [favorite({ availability: 'unlisted' })],
      error: undefined,
    })
    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('已下架')
    expect(wrapper.find('a.favorite-card__name').exists()).toBe(false)
    expect(wrapper.find('span.favorite-card__name').exists()).toBe(true)
  })

  it('shows an error state with a working retry', async () => {
    mockGet
      .mockResolvedValueOnce({
        data: undefined,
        error: new ApiError('Service Unavailable', { status: 503, code: 'service_unavailable' }),
      })
      .mockResolvedValueOnce({ data: [favorite()], error: undefined })
    const wrapper = await mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('Service Unavailable')

    await wrapper.get('button').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('RTX 4070')
  })

  it('removes a favorite and drops it from the list', async () => {
    mockGet
      .mockResolvedValueOnce({ data: [favorite()], error: undefined })
      .mockResolvedValueOnce({ data: [], error: undefined })
    mockDelete.mockResolvedValueOnce({ data: undefined, error: undefined })

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.get('button').trigger('click')
    await flushPromises()

    expect(mockDelete).toHaveBeenCalledWith('/api/v1/members/me/favorites/{productPublicId}', {
      params: { path: { productPublicId: 'product-1' } },
    })
    expect(wrapper.text()).toContain('目前沒有收藏商品')
  })

  it('shows a removal error without dropping the item from the list', async () => {
    mockGet.mockResolvedValue({ data: [favorite()], error: undefined })
    mockDelete.mockResolvedValueOnce({
      data: undefined,
      error: new ApiError('Conflict', { status: 409, code: 'concurrency_conflict' }),
    })

    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.get('button').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('RTX 4070')
    expect(wrapper.text()).toContain('Conflict')
  })
})

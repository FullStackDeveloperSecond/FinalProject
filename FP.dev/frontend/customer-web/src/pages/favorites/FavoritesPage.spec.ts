import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

function favoriteItem(overrides: Record<string, unknown> = {}) {
  return {
    productPublicId: '11111111-1111-1111-1111-111111111111',
    productCode: 'P1',
    name: '人體工學椅',
    brand: { code: 'ACME', name: 'Acme' },
    category: { code: 'CAT', name: 'Category' },
    price: { list: 1000, sale: null, currency: 'TWD' },
    primaryImage: null,
    availability: 'inStock',
    isPurchasable: true,
    createdAtUtc: '2026-08-20T00:00:00Z',
    ...overrides,
  }
}

const mocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')
  return {
    data: ref<{ items: Array<Record<string, unknown>>, totalCount: number, totalPages: number } | undefined>(
      { items: [], totalCount: 0, totalPages: 0 },
    ),
    isPending: ref(false),
    isError: ref(false),
    error: ref<unknown>(null),
    remove: { isPending: ref(false), mutate: vi.fn() },
  }
})

vi.mock('../../features/favorites/queries', () => ({
  useMyFavoritesQuery: () => ({
    data: mocks.data,
    isPending: mocks.isPending,
    isError: mocks.isError,
    error: mocks.error,
    refetch: vi.fn(),
  }),
  useRemoveFavoriteMutation: () => mocks.remove,
}))

const { default: FavoritesPage } = await import('./FavoritesPage.vue')

describe('FavoritesPage', () => {
  beforeEach(() => {
    mocks.remove.mutate.mockReset()
    mocks.data.value = { items: [], totalCount: 0, totalPages: 0 }
    mocks.isPending.value = false
    mocks.isError.value = false
  })

  it('shows an empty state when there are no favorites', () => {
    const wrapper = mount(FavoritesPage)
    expect(wrapper.text()).toContain('目前沒有收藏商品')
  })

  it('lists favorited products and links to their detail page', () => {
    mocks.data.value = { items: [favoriteItem()], totalCount: 1, totalPages: 1 }
    const wrapper = mount(FavoritesPage, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } })

    expect(wrapper.text()).toContain('人體工學椅')
    expect(wrapper.text()).toContain('現貨供應')
  })

  it('shows a delisted badge and does not link to the (unavailable) detail page', () => {
    mocks.data.value = {
      items: [favoriteItem({ availability: 'delisted', isPurchasable: false, price: null })],
      totalCount: 1,
      totalPages: 1,
    }
    const wrapper = mount(FavoritesPage, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } })

    expect(wrapper.text()).toContain('已下架')
    expect(wrapper.find('.favorite-card__link--delisted').exists()).toBe(true)
    expect(wrapper.find('a').exists()).toBe(false)
  })

  it('removes a favorite when "取消收藏" is clicked', async () => {
    mocks.data.value = { items: [favoriteItem()], totalCount: 1, totalPages: 1 }
    const wrapper = mount(FavoritesPage, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } })

    await wrapper.find('button').trigger('click')

    expect(mocks.remove.mutate).toHaveBeenCalledWith(
      '11111111-1111-1111-1111-111111111111',
      expect.any(Object),
    )
  })
})

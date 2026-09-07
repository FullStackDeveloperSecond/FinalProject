import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { apiClient } from '../../api/client'

const favoriteKeys = {
  list: (pageNumber: number, pageSize: number) => ['favorites', 'list', pageNumber, pageSize] as const,
  status: (productPublicId: string) => ['favorites', 'status', productPublicId] as const,
}

function invalidateFavorites(queryClient: ReturnType<typeof useQueryClient>) {
  return queryClient.invalidateQueries({ queryKey: ['favorites'] })
}

export function useMyFavoritesQuery(
  pageNumber: MaybeRefOrGetter<number>,
  pageSize: number,
  enabled: () => boolean = () => true,
) {
  return useQuery({
    queryKey: computed(() => favoriteKeys.list(toValue(pageNumber), pageSize)),
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/v1/members/me/favorites', {
        params: { query: { PageNumber: toValue(pageNumber), PageSize: pageSize } },
      })
      if (error) throw error
      return data
    },
    enabled: computed(enabled),
  })
}

export function useFavoriteStatusQuery(
  productPublicId: MaybeRefOrGetter<string | undefined>,
  enabled: () => boolean = () => true,
) {
  return useQuery({
    queryKey: computed(() => favoriteKeys.status(toValue(productPublicId) ?? '')),
    queryFn: async () => {
      const publicId = toValue(productPublicId)
      if (!publicId) throw new Error('Product public id is required.')
      const { data, error } = await apiClient.GET('/api/v1/members/me/favorites/{productId}', {
        params: { path: { productId: publicId } },
      })
      if (error) throw error
      return data
    },
    enabled: computed(() => enabled() && Boolean(toValue(productPublicId))),
  })
}

export function useAddFavoriteMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (productPublicId: string) => {
      const { error } = await apiClient.PUT('/api/v1/members/me/favorites/{productId}', {
        params: { path: { productId: productPublicId } },
      })
      if (error) throw error
    },
    onSuccess: () => invalidateFavorites(queryClient),
  })
}

export function useRemoveFavoriteMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (productPublicId: string) => {
      const { error } = await apiClient.DELETE('/api/v1/members/me/favorites/{productId}', {
        params: { path: { productId: productPublicId } },
      })
      if (error) throw error
    },
    onSuccess: () => invalidateFavorites(queryClient),
  })
}

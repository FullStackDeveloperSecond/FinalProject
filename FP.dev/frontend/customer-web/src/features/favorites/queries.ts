import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { apiClient } from '../../api/client'

const favoriteKeys = {
  list: (pageNumber: number, pageSize: number) => ['favorites', 'list', pageNumber, pageSize] as const,
}

function invalidateLists(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: ['favorites', 'list'] })
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

export function useAddFavoriteMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (productPublicId: string) => {
      const { error } = await apiClient.PUT('/api/v1/members/me/favorites/{productId}', {
        params: { path: { productId: productPublicId } },
      })
      if (error) throw error
    },
    onSuccess: () => invalidateLists(queryClient),
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
    onSuccess: () => invalidateLists(queryClient),
  })
}

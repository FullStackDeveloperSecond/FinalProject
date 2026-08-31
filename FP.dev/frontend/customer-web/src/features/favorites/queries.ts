import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, type MaybeRefOrGetter, toValue } from 'vue'
import { apiClient } from '../../api/client'

const favoriteKeys = {
  mine: () => ['favorites', 'mine'] as const,
}

function invalidateMine(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: favoriteKeys.mine() })
}

/**
 * 收藏只開放登入會員（S-01 規格）。`enabled` 預設 true——供已在 requiresAuth 頁面（例如
 * MyFavoritesPage）內使用；掛在公開商品頁的 FavoriteToggleButton 則必須傳入
 * `sessionStore.isAuthenticated`，避免對匿名訪客送出注定 401 的請求。
 */
export function useMyFavoritesQuery(enabled: MaybeRefOrGetter<boolean> = true) {
  return useQuery({
    queryKey: favoriteKeys.mine(),
    enabled: computed(() => toValue(enabled)),
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/v1/members/me/favorites', {})
      if (error) throw error
      return data
    },
  })
}

export function useAddFavoriteMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (productPublicId: string) => {
      const { data, error } = await apiClient.POST('/api/v1/members/me/favorites', {
        body: { productPublicId },
      })
      if (error) throw error
      return data
    },
    onSuccess: () => invalidateMine(queryClient),
  })
}

export function useRemoveFavoriteMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (productPublicId: string) => {
      const { error } = await apiClient.DELETE('/api/v1/members/me/favorites/{productPublicId}', {
        params: { path: { productPublicId } },
      })
      if (error) throw error
    },
    onSuccess: () => invalidateMine(queryClient),
  })
}

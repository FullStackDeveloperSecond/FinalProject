import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { createCategory, listCategories, updateCategory, type CategoryListParams } from './api'
import type { CreateCategoryRequest, UpdateCategoryRequest } from './types'

export function useCategoryList(params: MaybeRefOrGetter<CategoryListParams>) {
  return useQuery({
    queryKey: computed(() => ['categories', 'list', toValue(params)] as const),
    queryFn: () => listCategories(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

export function useCreateCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateCategoryRequest) => createCategory(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['categories', 'list'] }),
  })
}

export function useUpdateCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateCategoryRequest }) =>
      updateCategory(publicId, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['categories', 'list'] }),
  })
}

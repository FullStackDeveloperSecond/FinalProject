import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { createCategory, listCategories, updateCategory, type CategoryListParams } from './api'
import type { CreateCategoryRequest, UpdateCategoryRequest } from './types'
import { fetchAllPages } from '../shared/fetchAllPages'

export function useCategoryList(params: MaybeRefOrGetter<CategoryListParams>) {
  return useQuery({
    queryKey: computed(() => ['categories', 'list', toValue(params)] as const),
    queryFn: () => listCategories(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

/**
 * PR #24 review round 2: for resolving an existing association's code to a publicId, or
 * populating a "pick a parent" dropdown — needs every category regardless of the main
 * management table's pagination, not a single over-sized page (rejected server-side above
 * pageSize 100). See `fetchAllPages`.
 */
export function useFullCategoryList(params: MaybeRefOrGetter<Omit<CategoryListParams, 'pageNumber' | 'pageSize'>> = {}) {
  return useQuery({
    queryKey: computed(() => ['categories', 'full-list', toValue(params)] as const),
    queryFn: async () => ({
      items: await fetchAllPages((pageNumber, pageSize) =>
        listCategories({ ...toValue(params), pageNumber, pageSize })),
    }),
  })
}

// PR #24 review round 3: invalidating only ['categories','list'] left ['categories','full-list']
// (the parent-category picker) holding a stale set — a newly created category wouldn't appear as
// a selectable parent, and a rename wouldn't show up there, until something else invalidated it.
// Invalidate the whole 'categories' prefix so both match.
export function useCreateCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateCategoryRequest) => createCategory(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['categories'] }),
  })
}

export function useUpdateCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateCategoryRequest }) =>
      updateCategory(publicId, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['categories'] }),
  })
}

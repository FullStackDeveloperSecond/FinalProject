import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { createTag, listTags, updateTag, type TagListParams } from './api'
import type { CreateTagRequest, UpdateTagRequest } from './types'
import { fetchAllPages } from '../shared/fetchAllPages'

export function useTagList(params: MaybeRefOrGetter<TagListParams>) {
  return useQuery({
    queryKey: computed(() => ['tags', 'list', toValue(params)] as const),
    queryFn: () => listTags(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

/**
 * PR #24 review round 2: for resolving an existing association's code to a publicId — needs
 * every tag, not a single over-sized page (rejected server-side above pageSize 100).
 * See `fetchAllPages`.
 */
export function useFullTagList(params: MaybeRefOrGetter<Omit<TagListParams, 'pageNumber' | 'pageSize'>> = {}) {
  return useQuery({
    queryKey: computed(() => ['tags', 'full-list', toValue(params)] as const),
    queryFn: async () => ({
      items: await fetchAllPages((pageNumber, pageSize) =>
        listTags({ ...toValue(params), pageNumber, pageSize })),
    }),
  })
}

// PR #24 review round 3: invalidating only ['tags','list'] left ['tags','full-list'] (the tag
// checkbox list on ProductEditPage) holding a stale set — a newly created tag wouldn't be
// selectable, and a rename wouldn't show up there, until something else invalidated it.
// Invalidate the whole 'tags' prefix so both match.
export function useCreateTag() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateTagRequest) => createTag(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tags'] }),
  })
}

export function useUpdateTag() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateTagRequest }) =>
      updateTag(publicId, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tags'] }),
  })
}

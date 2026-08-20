import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { createTag, listTags, updateTag, type TagListParams } from './api'
import type { CreateTagRequest, UpdateTagRequest } from './types'

export function useTagList(params: MaybeRefOrGetter<TagListParams>) {
  return useQuery({
    queryKey: computed(() => ['tags', 'list', toValue(params)] as const),
    queryFn: () => listTags(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

export function useCreateTag() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateTagRequest) => createTag(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tags', 'list'] }),
  })
}

export function useUpdateTag() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateTagRequest }) =>
      updateTag(publicId, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tags', 'list'] }),
  })
}

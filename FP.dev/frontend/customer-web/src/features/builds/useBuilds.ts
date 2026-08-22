import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  addBuildToCart,
  checkCompatibility,
  createBuildList,
  createBuildShare,
  deleteBuildList,
  getBuildList,
  getSharedBuild,
  listBuildLists,
  revokeBuildShare,
  updateBuildList,
} from './api'
import type {
  AddBuildToCartRequest,
  CompatibilityCheckRequest,
  CreateBuildListRequest,
  UpdateBuildListRequest,
} from './types'

const buildListsQueryKey = ['build-lists'] as const

export function useBuildLists(params: MaybeRefOrGetter<{ pageNumber: number, pageSize: number }>) {
  return useQuery({
    queryKey: computed(() => [...buildListsQueryKey, 'list', toValue(params)] as const),
    queryFn: () => listBuildLists(toValue(params).pageNumber, toValue(params).pageSize),
    placeholderData: (previous) => previous,
  })
}

export function useBuildList(publicId: MaybeRefOrGetter<string | undefined>) {
  return useQuery({
    queryKey: computed(() => [...buildListsQueryKey, 'detail', toValue(publicId)] as const),
    queryFn: () => getBuildList(toValue(publicId)!),
    enabled: computed(() => Boolean(toValue(publicId))),
  })
}

export function useCreateBuildList() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateBuildListRequest) => createBuildList(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: buildListsQueryKey }),
  })
}

export function useUpdateBuildList() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateBuildListRequest }) =>
      updateBuildList(publicId, request),
    onSuccess: (buildList) => {
      queryClient.setQueryData([...buildListsQueryKey, 'detail', buildList.publicId], buildList)
      void queryClient.invalidateQueries({ queryKey: [...buildListsQueryKey, 'list'] })
    },
  })
}

export function useDeleteBuildList() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, rowVersion }: { publicId: string, rowVersion: string }) =>
      deleteBuildList(publicId, rowVersion),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: buildListsQueryKey }),
  })
}

export function useCreateBuildShare(publicId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => createBuildShare(toValue(publicId)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...buildListsQueryKey, 'detail', toValue(publicId)] }),
  })
}

export function useRevokeBuildShare(publicId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => revokeBuildShare(toValue(publicId)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...buildListsQueryKey, 'detail', toValue(publicId)] }),
  })
}

export function useSharedBuild(token: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => ['build-shares', toValue(token)] as const),
    queryFn: () => getSharedBuild(toValue(token)),
  })
}

export function useAddBuildToCart() {
  return useMutation({
    mutationFn: ({ publicId, request, idempotencyKey }: {
      publicId: string
      request: AddBuildToCartRequest
      idempotencyKey: string
    }) => addBuildToCart(publicId, request, idempotencyKey),
  })
}

/**
 * A mutation, not a query — the item list changes on every keystroke/row edit and there is no
 * stable cache key worth keeping (mirrors Cart's `useRevalidateCart`: caller triggers this
 * explicitly, e.g. from a debounced watcher, rather than TanStack Query auto-refetching it).
 */
export function useCompatibilityCheck() {
  return useMutation({
    mutationFn: (request: CompatibilityCheckRequest) => checkCompatibility(request),
  })
}

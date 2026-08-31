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

/**
 * 組長 PR #35 round-5 review (P3), following the precedent already set by 組長 PR #24 round 8 (P2)
 * in admin-web's `useSkus`: a `MaybeRefOrGetter<string>` that mutationFn and onSuccess each resolve
 * via `toValue()` pins the write target correctly at call time, but onSuccess re-reads the getter
 * at *completion* time — if the user navigates to another build list while the request is still in
 * flight, that later read picks up the new list's id and invalidates its cache entry instead of the
 * one this mutation actually wrote to. Round 4's fix made the id an optional mutation variable, but
 * the `?? toValue(publicId)` fallback left exactly that re-read in place for no-argument calls. The
 * id is now a required part of the mutation's own variables, supplied by the caller at `.mutate()`
 * time and never re-resolved afterward — mutationFn and onSuccess both receive the same frozen id
 * via `variables`, regardless of where the page has navigated to by the time the request settles.
 */
function invalidateBuildDetail(queryClient: ReturnType<typeof useQueryClient>, publicId: string) {
  return queryClient.invalidateQueries({ queryKey: [...buildListsQueryKey, 'detail', publicId] })
}

export function useCreateBuildShare() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (publicId: string) => createBuildShare(publicId),
    onSuccess: (_data, publicId) => invalidateBuildDetail(queryClient, publicId),
  })
}

export function useRevokeBuildShare() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (publicId: string) => revokeBuildShare(publicId),
    onSuccess: (_data, publicId) => invalidateBuildDetail(queryClient, publicId),
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

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
 * 組長 PR #35 round-4 review, P2: `mutationFn` 送出時讀到的是「當下」的 publicId，但 `onSuccess`
 * 若再呼叫一次 `toValue(publicId)`，在使用者已切到另一份清單時讀到的會是「新的」那一個——結果是
 * 改了 A 卻去 invalidate B，A 的 cache 反而保持舊狀態（B 則被無謂重抓）。改成把送出當下的 id 當成
 * mutation variables 傳進去，`onSuccess` 只認 `variables`，不重讀 route。呼叫端可以不帶參數
 * （沿用 composable 綁定的 getter），也可以顯式帶入自己 snapshot 的 id。
 */
export function useCreateBuildShare(publicId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    // 呼叫端不帶參數時 TanStack Query 會把 variables 設成 undefined（而不是「沒有引數」），
    // 所以 TypeScript 的預設參數只救得了 mutationFn，救不了 onSuccess——後者拿到的 variables
    // 仍是 undefined，query key 會變成 ['build-lists','detail',undefined]，等於沒 invalidate
    // 到任何東西。統一在這裡解析一次，兩邊都用同一個 resolved 值。
    mutationFn: (targetPublicId?: string) => createBuildShare(targetPublicId ?? toValue(publicId)),
    onSuccess: (_data, targetPublicId) => queryClient.invalidateQueries({
      queryKey: [...buildListsQueryKey, 'detail', targetPublicId ?? toValue(publicId)],
    }),
  })
}

export function useRevokeBuildShare(publicId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (targetPublicId?: string) => revokeBuildShare(targetPublicId ?? toValue(publicId)),
    onSuccess: (_data, targetPublicId) => queryClient.invalidateQueries({
      queryKey: [...buildListsQueryKey, 'detail', targetPublicId ?? toValue(publicId)],
    }),
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

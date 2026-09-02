import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  createConvenienceStore,
  createPackageLimitVersion,
  listConvenienceStores,
  listPackageLimitVersions,
  publishPackageLimitVersion,
  updateConvenienceStore,
  type ConvenienceStoreListParams,
} from './api'
import type {
  CreateConvenienceStoreRequest,
  CreatePackageLimitVersionRequest,
  UpdateConvenienceStoreRequest,
} from './types'

/**
 * 組長 PR #78 round-3 review [P2]：上一輪我只把 `placeholderData` 從版本清單拿掉，同一個檔案裡的
 * 門市清單卻留著——切換物流商、縣市、行政區、Active-only 或頁碼之後，新請求回來之前畫面上還是
 * 上一組門市，而且那些列的「編輯」「停用」按鈕照樣按得下去。管理員會在新篩選條件的畫面上改到
 * 或停用另一組查詢的門市。
 *
 * 與 usePackageLimitVersionList 一致：不沿用 placeholder，換 key 時 `isPending` 就是 true，頁面
 * 顯示載入中而不是舊資料。
 */
export function useConvenienceStoreList(params: MaybeRefOrGetter<ConvenienceStoreListParams>) {
  return useQuery({
    queryKey: computed(() => ['shipping', 'stores', toValue(params)] as const),
    queryFn: () => listConvenienceStores(toValue(params)),
  })
}

export function useCreateConvenienceStore() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateConvenienceStoreRequest) => createConvenienceStore(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['shipping', 'stores'] }),
  })
}

export function useUpdateConvenienceStore() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateConvenienceStoreRequest }) =>
      updateConvenienceStore(publicId, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['shipping', 'stores'] }),
  })
}

/**
 * 組長 PR #78 round-2 review item 1：query key 含 provider 沒錯，但 `placeholderData` 會在切換
 * 物流商時把「上一個物流商的版本清單」暫時顯示出來——那段期間按下發布，送出的會是「舊 provider
 * 的版本 PublicId／RowVersion ＋ 新的 providerCode」，是一個根本不存在的組合。
 *
 * 跨 provider 不沿用 placeholder：切換時就顯示載入中。這裡刻意不寫 `placeholderData`，讓
 * `isPending` 在換 key 時為 true。
 */
export function usePackageLimitVersionList(providerCode: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => ['shipping', 'package-limits', toValue(providerCode)] as const),
    queryFn: () => listPackageLimitVersions(toValue(providerCode)),
  })
}

export function useCreatePackageLimitVersion() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ providerCode, request }: { providerCode: string, request: CreatePackageLimitVersionRequest }) =>
      createPackageLimitVersion(providerCode, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['shipping', 'package-limits'] }),
  })
}

export function usePublishPackageLimitVersion() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ providerCode, versionPublicId, rowVersion }: {
      providerCode: string
      versionPublicId: string
      rowVersion: string
    }) => publishPackageLimitVersion(providerCode, versionPublicId, { rowVersion }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['shipping', 'package-limits'] }),
  })
}

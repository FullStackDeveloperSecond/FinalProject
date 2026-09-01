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

export function useConvenienceStoreList(params: MaybeRefOrGetter<ConvenienceStoreListParams>) {
  return useQuery({
    queryKey: computed(() => ['shipping', 'stores', toValue(params)] as const),
    queryFn: () => listConvenienceStores(toValue(params)),
    placeholderData: (previous) => previous,
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

export function usePackageLimitVersionList(providerCode: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => ['shipping', 'package-limits', toValue(providerCode)] as const),
    queryFn: () => listPackageLimitVersions(toValue(providerCode)),
    placeholderData: (previous) => previous,
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

import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  createSpecificationDefinition,
  disableSpecificationDefinition,
  listSpecificationDefinitions,
  updateSpecificationDefinition,
  type SpecificationDefinitionListParams,
} from './api'
import type {
  CreateSpecificationDefinitionRequest,
  UpdateSpecificationDefinitionRequest,
} from './types'

/**
 * 組長 PR #77 round-3 review [P2]：`placeholderData` 會跨分類、關鍵字、只顯示啟用中與頁碼保留上一
 * 組規格，而頁面只看 `isPending`——新請求還在飛的時候，畫面上是上一組查詢的規格，而且那些列的
 * 「編輯」「停用」按鈕照樣按得下去。管理員會在新條件的畫面上編輯或停用另一組查詢的規格。
 *
 * 不沿用 placeholder：換 key 時 `isPending` 就是 true，頁面顯示載入中而不是舊資料。寧可閃一下，
 * 也不給一組不屬於眼前條件、卻可以寫入的列。
 */
export function useSpecificationDefinitionList(
  params: MaybeRefOrGetter<SpecificationDefinitionListParams>,
) {
  return useQuery({
    queryKey: computed(() => ['specification-definitions', 'list', toValue(params)] as const),
    queryFn: () => listSpecificationDefinitions(toValue(params)),
  })
}

export function useCreateSpecificationDefinition() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateSpecificationDefinitionRequest) => createSpecificationDefinition(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['specification-definitions'] }),
  })
}

export function useUpdateSpecificationDefinition() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateSpecificationDefinitionRequest }) =>
      updateSpecificationDefinition(publicId, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['specification-definitions'] }),
  })
}

export function useDisableSpecificationDefinition() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, rowVersion }: { publicId: string, rowVersion: string }) =>
      disableSpecificationDefinition(publicId, { rowVersion }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['specification-definitions'] }),
  })
}

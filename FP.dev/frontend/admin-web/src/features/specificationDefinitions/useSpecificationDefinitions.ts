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

export function useSpecificationDefinitionList(
  params: MaybeRefOrGetter<SpecificationDefinitionListParams>,
) {
  return useQuery({
    queryKey: computed(() => ['specification-definitions', 'list', toValue(params)] as const),
    queryFn: () => listSpecificationDefinitions(toValue(params)),
    placeholderData: (previous) => previous,
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

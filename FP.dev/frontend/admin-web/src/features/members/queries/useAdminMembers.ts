import { computed, type Ref } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { createApiClient } from '../../../api/client'
import type {
  AdminMemberPaths,
  SetMemberAccountStatusRequest,
  UpdateAdminMemberProfileRequest,
} from '../api/paths'

function client() {
  return createApiClient<AdminMemberPaths>()
}

export interface AdminMemberListFilters {
  search: string
  status: string
  registeredFrom: string
  registeredTo: string
  pageNumber: number
  pageSize: number
}

export function useAdminMemberListQuery(filters: Ref<AdminMemberListFilters>) {
  return useQuery({
    queryKey: computed(() => ['admin-members', 'list', filters.value] as const),
    queryFn: async () => {
      const { data, error } = await client().GET('/api/v1/admin/members', {
        params: {
          query: {
            search: filters.value.search || undefined,
            status: filters.value.status || undefined,
            registeredFrom: filters.value.registeredFrom || undefined,
            registeredTo: filters.value.registeredTo || undefined,
            pageNumber: filters.value.pageNumber,
            pageSize: filters.value.pageSize,
          },
        },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useAdminMemberDetailQuery(publicId: Ref<string>) {
  return useQuery({
    queryKey: computed(() => ['admin-members', 'detail', publicId.value] as const),
    enabled: computed(() => publicId.value.length > 0),
    queryFn: async () => {
      const { data, error } = await client().GET('/api/v1/admin/members/{publicId}', {
        params: { path: { publicId: publicId.value } },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useUpdateAdminMemberProfileMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { publicId: string; request: UpdateAdminMemberProfileRequest }) => {
      const { error } = await client().PUT('/api/v1/admin/members/{publicId}', {
        params: { path: { publicId: input.publicId } },
        body: input.request,
      })
      if (error) {
        throw error
      }
    },
    onSuccess: async (_data, variables) => {
      await queryClient.invalidateQueries({ queryKey: ['admin-members', 'detail', variables.publicId] })
      await queryClient.invalidateQueries({ queryKey: ['admin-members', 'list'] })
    },
  })
}

export function useResetMemberPasswordMutation() {
  return useMutation({
    mutationFn: async (publicId: string) => {
      const { error } = await client().POST('/api/v1/admin/members/{publicId}/reset-password', {
        params: { path: { publicId } },
      })
      if (error) {
        throw error
      }
    },
  })
}

export function useSetMemberAccountStatusMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { publicId: string; request: SetMemberAccountStatusRequest }) => {
      const { error } = await client().POST('/api/v1/admin/members/{publicId}/status', {
        params: { path: { publicId: input.publicId } },
        body: input.request,
      })
      if (error) {
        throw error
      }
    },
    onSuccess: async (_data, variables) => {
      await queryClient.invalidateQueries({ queryKey: ['admin-members', 'detail', variables.publicId] })
      await queryClient.invalidateQueries({ queryKey: ['admin-members', 'list'] })
    },
  })
}

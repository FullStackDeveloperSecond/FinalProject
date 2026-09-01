import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  createAddress,
  deleteAddress,
  fetchAddresses,
  fetchProfile,
  updateAddress,
  updateProfile,
  type CreateMemberAddressRequest,
  type UpdateMemberAddressRequest,
  type UpdateMemberProfileRequest,
} from './api'

const memberKeys = {
  profile: () => ['members', 'me', 'profile'] as const,
  addresses: () => ['members', 'me', 'addresses'] as const,
}

export function useProfileQuery() {
  return useQuery({
    queryKey: memberKeys.profile(),
    queryFn: fetchProfile,
  })
}

export function useUpdateProfileMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: UpdateMemberProfileRequest) => updateProfile(body),
    onSuccess: profile => queryClient.setQueryData(memberKeys.profile(), profile),
  })
}

export function useAddressesQuery() {
  return useQuery({
    queryKey: memberKeys.addresses(),
    queryFn: fetchAddresses,
  })
}

function invalidateAddresses(queryClient: ReturnType<typeof useQueryClient>) {
  return queryClient.invalidateQueries({ queryKey: memberKeys.addresses() })
}

export function useCreateAddressMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateMemberAddressRequest) => createAddress(body),
    onSuccess: () => invalidateAddresses(queryClient),
  })
}

export function useUpdateAddressMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (params: { addressPublicId: string, body: UpdateMemberAddressRequest }) =>
      updateAddress(params.addressPublicId, params.body),
    onSuccess: () => invalidateAddresses(queryClient),
  })
}

export function useDeleteAddressMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (params: { addressPublicId: string, rowVersion: string }) =>
      deleteAddress(params.addressPublicId, params.rowVersion),
    onSuccess: () => invalidateAddresses(queryClient),
  })
}

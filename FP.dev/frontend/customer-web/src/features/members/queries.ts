import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { toValue, type MaybeRefOrGetter } from 'vue'
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

// `enabled` defaults to true so every existing member-only page call site (ProfilePage.vue,
// AddressesPage.vue) is unaffected; Checkout (Public Cart／Member) passes the session's
// isAuthenticated so a guest never fires a call that can only 401.
export function useProfileQuery(enabled: MaybeRefOrGetter<boolean> = true) {
  return useQuery({
    queryKey: memberKeys.profile(),
    queryFn: fetchProfile,
    enabled: () => toValue(enabled),
  })
}

export function useUpdateProfileMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: UpdateMemberProfileRequest) => updateProfile(body),
    onSuccess: profile => queryClient.setQueryData(memberKeys.profile(), profile),
  })
}

export function useAddressesQuery(enabled: MaybeRefOrGetter<boolean> = true) {
  return useQuery({
    queryKey: memberKeys.addresses(),
    queryFn: fetchAddresses,
    enabled: () => toValue(enabled),
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

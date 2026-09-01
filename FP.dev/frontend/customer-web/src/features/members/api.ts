import type { components } from '@doselect/web-shared/api'
import { apiClient } from '../../api/client'

export type MemberProfile = components['schemas']['MemberProfileResponse']
export type MemberAddress = components['schemas']['MemberAddressResponse']
export type UpdateMemberProfileRequest = components['schemas']['UpdateMemberProfileRequest']
export type CreateMemberAddressRequest = components['schemas']['CreateMemberAddressRequest']
export type UpdateMemberAddressRequest = components['schemas']['UpdateMemberAddressRequest']

export const SUPPORTED_LOCALES: ReadonlyArray<{ value: string, label: string }> = [
  { value: 'zh-TW', label: '繁體中文' },
  { value: 'ja-JP', label: '日本語' },
  { value: 'ko-KR', label: '한국어' },
]

export async function fetchProfile(): Promise<MemberProfile> {
  const { data } = await apiClient.GET('/api/v1/members/me')
  return data!
}

export async function updateProfile(body: UpdateMemberProfileRequest): Promise<MemberProfile> {
  const { data } = await apiClient.PUT('/api/v1/members/me', { body })
  return data!
}

export async function fetchAddresses(): Promise<MemberAddress[]> {
  const { data } = await apiClient.GET('/api/v1/members/me/addresses')
  return data!
}

export async function createAddress(body: CreateMemberAddressRequest): Promise<MemberAddress> {
  const { data } = await apiClient.POST('/api/v1/members/me/addresses', { body })
  return data!
}

export async function updateAddress(
  addressPublicId: string,
  body: UpdateMemberAddressRequest,
): Promise<MemberAddress> {
  const { data } = await apiClient.PUT('/api/v1/members/me/addresses/{id}', {
    params: { path: { id: addressPublicId } },
    body,
  })
  return data!
}

export async function deleteAddress(addressPublicId: string, rowVersion: string): Promise<void> {
  await apiClient.DELETE('/api/v1/members/me/addresses/{id}', {
    params: { path: { id: addressPublicId } },
    body: { rowVersion },
  })
}
